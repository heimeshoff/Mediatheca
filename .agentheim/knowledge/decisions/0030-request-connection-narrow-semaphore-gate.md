---
id: 0030
title: A single process-wide SemaphoreSlim guards the 3 request-reachable transaction-opening choke points on the shared request SqliteConnection, generalizing ADR-0028's per-command-lock idiom
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-cx92m, administration-tj8n2]
related_research: []
---

# ADR 0030: A single process-wide SemaphoreSlim guards the 3 request-reachable transaction-opening choke points on the shared request SqliteConnection, generalizing ADR-0028's per-command-lock idiom

## Context

administration-tj8n2 (ADR-0028) fixed a process-fatal race on the scheduled-job
timers' shared `SqliteConnection` by giving jobs a *dedicated* connection plus
a per-command `SemaphoreSlim`, and in doing so corrected a premise ADR-0024
and ADR-0026 had both relied on: WAL mode + a 5s `busy_timeout` make SQLite's
*file-level* write serialization across separate connections safe, but they do
**not** make *one* `Microsoft.Data.Sqlite.SqliteConnection` object safe for
concurrent command creation/disposal from multiple threads. ADR-0028 fixed the
job path; the request path — the entire server's Fable.Remoting API
(`Api.create`), the admin API (`Administration.create`), and several raw
Giraffe stream routes — still runs on **one** shared `conn`
(`Composition.createConnection`, opened once in `Composition.buildApp`), with
no serialization at all across concurrent request threads.

**This is not theoretical.** administration-a4d9b's Playwright specs
(`tests/e2e/event-tail-follow.spec.ts`) empirically proved that concurrent
`addFriend` calls crash the shared connection with `SqliteConnection does not
support nested transactions`, and had to sequence their calls specifically to
dodge it — citing this spike (administration-cx92m) by name as the deferred
fix. Kestrel/Giraffe genuinely dispatch concurrent requests on separate
thread-pool threads, so this is a live correctness gap in a single-user,
self-hosted app: low practical crash probability, but a real and now-known
one.

**Finding — the exact request-reachable transaction-opening choke points.** A
grep for `conn.BeginTransaction()` across `src/Server` returns exactly 3 call
sites reachable from a request thread on the shared `conn`:

- `EventStore.appendToStream` (`EventStore.fs:376`), reached via
  `Api.executeCommand`'s private core (`Api.fs`), used from ~130 call sites
  across `Api.create` and several private helper functions it or its callers
  invoke (`addMovieToLibraryImpl`/`addMovieToLibrary`,
  `addSeriesToLibraryImpl`/`addSeriesToLibrary`, `runSteamFamilyImport`,
  `steamFamilyImportHandler`, `runJellyfinImport`, `attachSteamToGameCore`).
  This is the empirically-crashing path (`addFriend`, among ~130 others).
- `EventStore.importNdjson` (`EventStore.fs:505`), via
  `Administration.importEventsStreamHandler` (`Administration.fs`).
- `GameJournal.save` (`GameJournal.fs`), reached from `Api.create`'s
  `saveGameJournal` and from `GameJournal.migrateFromContentBlocks`'s
  one-time startup migration.

Every other read-only or plain-write accessor on the shared `conn` is
*technically* the same object-level risk class (ADR-0028's finding is about
the connection object, not specifically about transactions), but every crash
observed to date — both ADR-0028's original job-timer crash and
administration-a4d9b's `addFriend` crash — has been **write×write** (two
`BeginTransaction` calls racing). Read×write and read×read races on the same
connection object have never been reproduced in this codebase.

## Decision

### One process-wide `SemaphoreSlim(1, 1)` (`requestDbLock`) guards the 3 choke points, acquired only around each one's synchronous body

`Composition.fs` builds `requestDbLock` once, beside ADR-0028's `jobConn`/
`jobDbLock`, and threads it as a parameter into `Api.create`,
`GameJournal.save` (and its `migrateFromContentBlocks` caller), and
`Administration.importEventsStreamHandler` — the same three functions the
finding above names. Each acquires the lock only around its own synchronous,
already-in-flight-together SQL work (the `dbLock.Wait() ... finally
dbLock.Release()` idiom ADR-0028 introduced for `jobDbLock`), never across an
awaited HTTP call, TMDB/RAWG/Steam/Jellyfin fetch, or SSE write. Concurrent
requests' network I/O still fully overlaps; only their brief moments of
direct `conn` command creation/disposal serialize. This is the same idiom
ADR-0028 established for `jobConn`/`jobDbLock`, generalized from the
dedicated job connection to the shared request connection.

`Api.executeCommand`'s private core (`executeCommandCore`) takes the lock as
an explicit parameter and is generic over the bounded context's event/state/
command types. Every one of its ~130 request-path call sites is unchanged
syntactically — `Api.create` and each of the handful of private helper
functions between `executeCommandCore`'s definition and `Api.create` shadow
the name locally via a full eta-expansion (`let executeCommand conn streamId
... = executeCommandCore dbLock conn streamId ...`, not a bare partial
application — F#'s value restriction would otherwise collapse the shadowed
binding to whichever bounded context's types its first call site
instantiates, breaking every other bounded context's calls). This keeps the
~130 call sites' own code identical to before the fix; only the small number
of function *signatures* between the core and `Api.create` gained a `dbLock`
parameter to pass the same instance through.

### The residual read×write/read×read race on the shared `conn` is accepted, not closed

Outside these 3 transaction-opening choke points, the shared `conn` object is
still touched directly, unguarded, by ordinary reads (`SELECT` queries via
`Donald`) and plain non-transactional writes (`Db.exec` calls like
`SettingsStore.setSetting`) from concurrent request threads. Per ADR-0028's
corrected premise, this remains a real risk class at the ADO.NET object
level — WAL + `busy_timeout` do not make it safe just because it isn't a
`BeginTransaction` call. This ADR explicitly does **not** close that
residual race: it has never been reproduced here (unlike the two write×write
crashes that motivated ADR-0028 and this ADR), and closing it would require
either the full per-request-connection migration (rejected below as not
cheap) or a much coarser, more expensive lock around every `conn` touch
(also rejected below). It is accepted as open, tracked by
administration-mz6kp, the follow-up that would close it structurally.

## Alternatives considered

- **A global lock over every `conn` touch, not just the 3 transaction-opening
  sites.** Rejected: this app has multi-minute foreground operations over
  `conn` (Steam Family import, `runJellyfinImport`'s full sync, projection
  rebuild) that would serialize behind ordinary library reads and each
  other's DB moments for the whole app if the lock were held that broadly —
  a worse trade than ADR-0028 already declined for the narrower job-path
  case. The narrow 3-choke-point gate serializes only the moments that have
  actually crashed.
- **Per-operation/pooled connections (the full migration).** Structurally
  the correct fix — it removes the shared mutable connection object
  entirely, closing the residual read×write/read×read race too — but not
  cheap: ADR-0028's own architect pass estimated ~150-200 edit sites across
  `Api.fs`/`Administration.fs`, the 4 raw Giraffe handlers, and 8 test files
  whose `:memory:` connections would need `Cache=Shared` or factory-based
  reconstruction to keep working per-test. Split to administration-mz6kp
  rather than attempted inline in a task whose stop-loss (ADR-0065) calls
  for recording the known-and-cheap mitigation and stopping once it's
  implemented.
- **A `dbLock: SemaphoreSlim option` bare partial-application shadow inside
  `Api.create` and its helper functions** (i.e., `let executeCommand =
  executeCommandCore requestDbLock`, without eta-expanding the remaining
  parameters). Tried first; rejected once it failed to compile across
  bounded contexts — F#'s value restriction only generalizes a let-bound
  function value when it is syntactically a function (explicit parameters),
  not when it's a bare application of a still-generic function. The full
  eta-expansion form was needed for `executeCommand` to remain usable across
  every bounded context's event/state/command types at every call site.

## Consequences

### Positive
- The empirically-observed `addFriend` crash (`SqliteConnection does not
  support nested transactions` under concurrent requests) cannot recur — the
  3 transaction-opening choke points can never race each other on the shared
  `conn` object.
- ADR-0028's per-command-lock idiom is now applied consistently on both
  connections this process holds (`jobConn`/`jobDbLock` for jobs, `conn`/
  `requestDbLock` for requests), rather than leaving the request path as the
  one place the corrected premise didn't yet have a fix.
- Cheap: one new `SemaphoreSlim`, threaded through a handful of function
  signatures; the ~130 individual `executeCommand`/`GameJournal.save`/
  `EventStore.importNdjson` call sites' own code is unchanged.
- A concurrent-burst Playwright regression
  (`tests/e2e/event-tail-follow.spec.ts`) and a real-temp-file-connection
  Expecto regression (`tests/Server.Tests/RequestConnectionConcurrencyTests.fs`,
  mirroring ADR-0028's `JobConnectionConcurrencyTests.fs`) both drive the
  real, unmodified `addFriend` choke point under concurrent load.

### Negative / accepted tradeoffs
- The residual read×write/read×read race on `conn` outside the 3
  transaction-opening sites remains open — a deliberate, documented
  acceptance, not an oversight, tracked by administration-mz6kp.
- Every function between `executeCommandCore` and `Api.create`
  (`addMovieToLibraryImpl`, `addMovieToLibrary`, `addSeriesToLibraryImpl`,
  `addSeriesToLibrary`, `runSteamFamilyImport`, `steamFamilyImportHandler`,
  `runJellyfinImport`, `attachSteamToGameCore`) now carries an extra
  `dbLock: SemaphoreSlim` parameter purely to pass the same instance through
  — mechanical, but it is one more parameter on 8 function signatures.
- `GameJournal.save` and `GameJournal.migrateFromContentBlocks` also gained a
  `dbLock` parameter, which rippled into 2 test files
  (`GameJournalTests.fs`, `AdministrationTests.fs`) that construct their own
  never-contended `SemaphoreSlim` per call, exactly like ADR-0028's
  `manualSyncTriggerLock` precedent.
- One more long-lived `SemaphoreSlim` for the process to hold; negligible.

### Neutral
- This is explicitly the *interim* state. administration-mz6kp (backlog,
  `depends_on: [administration-cx92m]`) is the structurally-correct
  per-request-connection migration that would retire `requestDbLock`
  entirely and close the residual race this ADR accepts; it may be dismissed
  if the interim gate proves sufficient for this single-user app
  indefinitely, but it is not a mandate either way.

## References

- `src/Server/Composition.fs` — `requestDbLock`, threaded into `Api.create`,
  `Administration.importEventsStreamHandler`, `Api.steamFamilyImportHandler`,
  `GameJournal.migrateFromContentBlocks`.
- `src/Server/Api.fs` — `executeCommandCore` (renamed from the former
  `executeCommand`, now takes `dbLock` as its first parameter and wraps its
  body in `dbLock.Wait() / finally dbLock.Release()`), the eta-expanded
  `executeCommand` shadow in `Api.create` and each intermediate helper
  function, and the `dbLock`/`requestDbLock` parameter threaded through
  `addMovieToLibraryImpl`/`addMovieToLibrary`,
  `addSeriesToLibraryImpl`/`addSeriesToLibrary`, `runSteamFamilyImport`,
  `steamFamilyImportHandler`, `runJellyfinImport`, `attachSteamToGameCore`.
- `src/Server/GameJournal.fs` — `save`'s and `migrateFromContentBlocks`'s new
  `dbLock` parameter.
- `src/Server/Administration.fs` — `importEventsStreamHandler`'s new
  `dbLock` parameter, acquired only around the `EventStore.importNdjson`
  call.
- `tests/Server.Tests/RequestConnectionConcurrencyTests.fs` — the Expecto
  regression coverage described above, mirroring ADR-0028's
  `JobConnectionConcurrencyTests.fs` shape.
- `tests/e2e/event-tail-follow.spec.ts` — `addFriends` now fires concurrently
  (`Promise.all`) instead of sequentially, plus a dedicated repeated-burst
  regression test.
- `tests/Server.Tests/GameJournalTests.fs`, `AdministrationTests.fs` — updated
  `GameJournal.save`/`migrateFromContentBlocks` call sites to pass a
  never-contended test-local `SemaphoreSlim`.
- ADR-0003 — SQLite/WAL baseline this fix builds on, not replaces.
- ADR-0024, ADR-0026 — the "single shared `conn` is safe for concurrent use"
  premise ADR-0028 first corrected for the job path; this ADR applies the
  same correction to the request path.
- ADR-0028 — the scheduled-job dedicated-connection-plus-per-command-lock
  fix whose exact idiom this ADR generalizes to the request connection.
- administration-tj8n2 — the task that shipped ADR-0028 and first surfaced
  the broader request×request question, deferred to this spike.
- administration-mz6kp — the deferred, structurally-correct
  per-request-connection migration this ADR's gate is an interim
  substitute for.
