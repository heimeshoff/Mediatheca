---
id: administration-mz6kp
title: Migrate Api.create/Administration.create and the raw Giraffe stream handlers from one shared SqliteConnection to per-request (factory-based) connections, retiring the ADR-0030 semaphore gate
status: todo
type: refactor
context: administration
created: 2026-07-22
completed:
depends_on: [administration-cx92m]
blocks: []
tags: [sqlite, concurrency, architecture, reliability, refactor]
related_adrs: [0003, 0028, 0030, 0032]
related_research: []
prior_art: [administration-tj8n2, administration-cx92m]
---

## Why
administration-cx92m closed the empirically-observed request×request crash on
the shared `SqliteConnection` cheaply, with a process-wide `SemaphoreSlim`
serializing the request-reachable transaction-opening choke points (ADR-0030).
That gate is the *interim* state: it removes the crash but keeps the shared
mutable connection and its accepted residual read×write/read×read race, and it
serializes writes that a proper connection-per-request design would let run
against the WAL file independently.

The structurally correct fix — the one ADR-0030 explicitly defers as "not cheap"
— is to stop sharing one connection object at all: give each request/operation
its own `SqliteConnection` drawn from `Microsoft.Data.Sqlite`'s
connection-string-keyed pool, removing the shared mutable state **by
construction** so the ADR-0028-class object-level race cannot arise on the
request path and the semaphore gate is retired. ADR-0028 already proved this
exact pattern (separate connection to the WAL file, serialized at the SQLite
*file* level by `busy_timeout`) safe for the dedicated job connection; this
generalizes it to every request.

Builder decision (2026-07-22): refine to ready and ship the proper fix, not keep
the interim gate indefinitely.

## What
Replace the single shared `conn` parameter of `Api.create` (`src/Server/Api.fs`)
and `Administration.create` (`src/Server/Administration.fs`) — and of the raw
Giraffe SSE stream handlers — with a connection **factory**
`factory: unit -> SqliteConnection`, built once in `Composition.buildApp`
beside the existing `jobConn`. Each request-serving record member and each SSE
handler opens exactly one scoped connection at entry (`use conn = factory()`
inside its async/task scope) and disposes it when the operation completes. Retire
the ADR-0030 `requestDbLock` once the shared object is gone.

Per-connection pragmas (`busy_timeout=5000`, `foreign_keys=ON`,
`synchronous=NORMAL`) default to unsafe values and must be re-applied on **every**
open — so the factory calls a new public `EventStore.configureConnection` (the
pragma block, split out of `initialize`), and *only* that: table/FTS creation
stays a one-time startup step, never re-run per request. Pooling makes
`use conn = factory()` cheap (a warm pooled handle, not a real file-open).

**SSE handlers: one connection per stream, not per DB call.** Each SSE handler
(`steamFamilyImportHandler`, `exportEventsStreamHandler`,
`importEventsStreamHandler`, `projectionRebuildStreamHandler`,
`driftCheckStreamHandler`) opens one `use conn = factory()` at handler entry and
holds it for the whole stream — these are rare, single, long-running
operator-initiated operations (and `importNdjson` is one atomic transaction, so
it *must* be one connection). `driftCheckStreamHandler` keeps its private
throwaway `Data Source=:memory:` shadow connection exactly as-is (that is not a
factory connection); only its *live* connection becomes `factory()`.

**Corrected blast radius** (architect re-scope after ADR-0030 landed — the
capture's "8 test files" estimate was wrong): the must-change test set is **4**
files — `AdministrationTests.fs`, `GameJournalTests.fs`, `JobRunsTests.fs`,
`RequestConnectionConcurrencyTests.fs` (the ADR-0030 addFriend regression, which
already exists). The other originally-listed files
(`EventStoreTests.fs`, `EventStoreNdjsonTests.fs`, `MoviesIntegrationTests.fs`,
`FriendIntegrationTests.fs`, `PlaytimeTrackerTests.fs`,
`ProjectionRebuildTests.fs`) call `conn`-taking helpers directly and compile
unchanged — verified by a green suite, not by mandated edits.

**Test-fixture decision (the load-bearing risk):** the create/SSE tests move to a
**per-test temp-file DB** with a production-shaped factory
(`Data Source={temp}/mediatheca-test-{guid}.db`), via a shared
`TestDb.withTempDbFactory` helper that bootstraps schema once and deletes the
`.db` + `-wal`/`-shm` sidecars on dispose. **Not** shared-cache `:memory:`: (1) a
shared-cache in-memory DB is destroyed when its last connection closes, so a
factory-per-call fixture would silently hand out an empty DB between two
operations; (2) in-memory DBs don't use WAL, so they wouldn't actually exercise
the file-level serialization this migration ships. The concurrency tests already
use exactly this temp-file pattern.

## Acceptance criteria
- [ ] `EventStore.configureConnection` (public) holds the per-connection pragma
      block (`busy_timeout`, `foreign_keys`, `synchronous`, `journal_mode`);
      `EventStore.initialize` calls it, and the `Composition` factory calls
      **only** it (no `CREATE TABLE`/FTS per open).
- [ ] `Composition.buildApp` builds `connectionFactory : unit -> SqliteConnection`;
      `Api.create` and `Administration.create` take it in place of `conn`. Neither
      `create` has a live-`SqliteConnection` first parameter anymore.
- [ ] No request path holds a process-shared connection: outside `Composition.fs`'s
      startup bootstrap `conn` and the ADR-0028 `jobConn`, every `conn` used inside
      an `Api`/`Administration` record member or SSE handler originates from a
      `use conn = factory()` in that same member/handler.
- [ ] `requestDbLock` is fully removed — `grep -rn requestDbLock src/` returns
      nothing; the `dbLock` parameter is gone from every request-reachable
      transaction site: `executeCommandCore`, `GameJournal.save` /
      `GameJournal.migrateFromContentBlocks`, `Administration.importEventsStreamHandler`,
      and `Administration.appendCompensatingEventCore` (the fourth site, added by
      ADR-0032 after ADR-0030). `jobConn`/`jobDbLock` (ADR-0028) and the vestigial
      `manualSyncTriggerLock` are left untouched — grep confirms they still exist.
- [ ] All five SSE handlers open exactly one `use conn = factory()` at handler
      entry and hold it for the whole stream, documented as "one connection per
      stream"; `driftCheckStreamHandler` additionally keeps its throwaway
      `Data Source=:memory:` shadow connection unchanged; the `dbLock.Wait()/finally
      Release()` block in `importEventsStreamHandler` is gone.
- [ ] Members that today return a helper's `Async`/`Task` directly are wrapped so
      the connection's `use` scope spans execution
      (`async { use conn = factory() … return! … }`); no `factory()` result escapes
      a disposing scope.
- [ ] The 4 must-change test files construct their `IMediathecaApi` / `IAdminApi` /
      `GameJournal` fixtures via the new temp-file `TestDb.withTempDbFactory` helper
      (deleting `.db` + `-wal`/`-shm` on dispose), dropping every live-`conn` +
      `SemaphoreSlim` create argument.
- [ ] `RequestConnectionConcurrencyTests.fs` (the ADR-0030 concurrent-`addFriend`
      regression) still passes with the semaphore removed — concurrent `addFriend`
      fires without `SqliteConnection does not support nested transactions` and
      without corruption. This Expecto test is the CI-run safety proof.
- [ ] `npm test` and `npm run build` both green (Fable compilation clean).
- [ ] The ADR-0030 gate is retired in the decision record: **ADR-0033** is written
      (`scope: administration`, `supersedes: [0030]`), and ADR-0030 flips to
      `status: superseded`, `superseded_by: [0033]`.
- [ ] `tests/e2e/event-tail-follow.spec.ts` concurrent-`addFriend` burst still
      passes with `requestDbLock` removed — run if the Playwright harness is
      available; otherwise the Expecto regression above stands as the proof.

## Notes
- **Unblocked:** administration-cx92m / ADR-0030 has landed (done 2026-07-22), which
  was the precondition this refinement waited on. Criteria above are architect-firmed
  and machine-checkable — this task is ready for a worker.
- **ADR to write (ADR-0033):** *"Each request and each long-running SSE operation
  opens and disposes its own `SqliteConnection` from a shared
  `unit -> SqliteConnection` factory (per-connection pragmas applied on open, pooled
  by connection string); the single shared request connection and ADR-0030's
  `requestDbLock` semaphore are removed — closing the residual read×write race
  ADR-0030 accepted — while ADR-0028's dedicated `jobConn`/`jobDbLock` and the
  vestigial `manualSyncTriggerLock` remain unchanged."* Supersede, not amend: this
  replaces the mechanism, and the interim's rationale is worth preserving in history.
- **Factory seam:** a bare `unit -> SqliteConnection` function (not an interface —
  one method, YAGNI), built in `Composition.buildApp` where `conn` is built today;
  one bootstrap `conn` stays in `Composition` for single-threaded startup work
  (seeds, `backfillDirectors`, `JellyfinSync.initialize`,
  `GameJournal.migrateFromContentBlocks`, `Projection.startAllProjections`,
  `initializeJobRuns`) — no longer shared with request threads.
- **Worker watch-outs (architect):**
  - `use`, not `let`, and **inside** the async/task block — a bare `let conn = factory()`
    or a `use` outside the async scope leaks the connection. Most likely worker error.
  - Keep exactly **one** `factory()` per member invocation; route every inner DB call
    through that one local `conn`. There is no cross-call transaction today (each
    `executeCommand`/`appendToStream`/`importNdjson` is its own committed transaction),
    so single-connection-per-member is atomicity-safe — but verify multi-append members
    (`removeMovie`, the Steam/Jellyfin import loops) before editing.
  - Per-connection pragmas are load-bearing: dropping `foreign_keys=ON` or
    `busy_timeout=5000` on the factory path silently loses FK enforcement / turns the
    5s wait into instant `SQLITE_BUSY`.
  - The eta-expanded generic `executeCommand` shadow (needed for F#'s value
    restriction across BCs) is retained but now calls `executeCommandCore conn …`
    with no `dbLock` argument.
  - Under genuine per-request connections a same-stream write race surfaces as
    `EventStore.ConcurrencyConflict` ("please retry") via `busy_timeout` rather than
    the semaphore — intended behavior, essentially never hit in this single-user app.
- Prior art: administration-tj8n2 / ADR-0028 proved separate connections to the WAL
  file safe (job path); administration-cx92m / ADR-0030 is the interim gate this
  supersedes.
