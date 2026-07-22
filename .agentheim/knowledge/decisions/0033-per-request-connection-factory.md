---
id: 0033
title: Each request and each long-running SSE operation opens and disposes its own SqliteConnection from a shared factory, retiring ADR-0030's requestDbLock
scope: administration
status: accepted
date: 2026-07-22
supersedes: [0030]
superseded_by: []
related_tasks: [administration-mz6kp]
related_research: []
---

# ADR 0033: Each request and each long-running SSE operation opens and disposes its own SqliteConnection from a shared factory, retiring ADR-0030's requestDbLock

## Context

ADR-0030 closed the empirically-observed request×request crash on the shared
`SqliteConnection` (`SqliteConnection does not support nested transactions`
under concurrent `addFriend` calls) cheaply, with a process-wide
`SemaphoreSlim` (`requestDbLock`) serializing the 3 (later 4, after
ADR-0032's compensating-event composer) request-reachable
transaction-opening choke points. It explicitly named this the *interim*
state: the shared mutable `conn` object stayed, the residual read×write/
read×read race outside those choke points stayed open (never reproduced,
but real per ADR-0028's corrected premise — `Microsoft.Data.Sqlite`'s WAL +
`busy_timeout` serialize writes at the *file* level across separate
connections, but do **not** make one `SqliteConnection` object safe for
concurrent command creation/disposal from multiple threads), and every
request continued serializing behind `requestDbLock` at its DB-touching
moments even though nothing about SQLite's file-level concurrency required
that.

ADR-0028 had already proved the alternative pattern safe, just at smaller
scale: give the scheduled-jobs timer its own dedicated `SqliteConnection`
(`jobConn`), separate from the request-serving `conn`, so the job path and
the request path can never race each other's command creation/disposal on
the same object. This ADR generalizes that exact pattern — a distinct
connection per independent unit of work — from "one dedicated connection for
the job subsystem" to "one connection per request/SSE operation, drawn from
a shared factory."

## Decision

### `Composition.buildApp` builds a `connectionFactory : unit -> SqliteConnection`

`createConnectionFactory (dbPath: string) : unit -> SqliteConnection`
(`Composition.fs`) returns a closure that opens a brand-new
`SqliteConnection` to the same `dbPath`, re-applies the per-connection
pragma block (`EventStore.configureConnection` — `busy_timeout=5000`,
`foreign_keys=ON`, `synchronous=NORMAL`, `journal_mode=WAL`; split out of
`EventStore.initialize` specifically so the factory calls only the pragma
step, never `CREATE TABLE`/FTS setup), and returns it open. Table/FTS
creation stays a one-time startup step on the original bootstrap `conn`
(`Composition.createConnection`, still used for `Composition.buildApp`'s own
single-threaded startup work — seeds, `backfillDirectors`,
`JellyfinSync.initialize`, `GameJournal.migrateFromContentBlocks`,
`Projection.startAllProjections`, `initializeJobRuns` — no longer shared
with request threads). `Microsoft.Data.Sqlite` pools physical connections
per connection string, so `use conn = factory()` is cheap: a warm pooled
handle, not a real file-open, on every call.

### `Api.create` and `Administration.create` take the factory, not a live connection

Both `create` functions' first parameter changes from a live `SqliteConnection`
to `factory: unit -> SqliteConnection`. Every request-serving record member
opens exactly one connection at entry — `use conn = factory()`, inside the
`async`/`task` computation expression, so its scope disposes the connection
when the operation completes (never a bare `let`, never a `use` outside the
async scope — either leaks the connection past the request). Multi-append
members (`removeMovie`, the Steam/Jellyfin import loops) hold that one
connection across every append they make; there is no cross-call transaction
today (`executeCommand`/`appendToStream`/`importNdjson` are each their own
committed transaction), so single-connection-per-member is atomicity-safe.

Two members can't simply open-and-dispose inline because they spawn
genuinely detached background work that outlives the triggering request:
- `triggerJellyfinSync` forwards the `factory` itself to
  `JellyfinSync.triggerSync` (whose signature changed from taking a live
  `conn` to taking `factory`), which opens its own `use conn = factory()`
  *inside* the `Async.Start`-spawned background async — a connection scoped
  to the whole background sync's lifetime, not the triggering request's.
- `runJobNow` is unaffected: it never touched the request-serving `conn` at
  all — it delegates entirely to the recorder built over ADR-0028's
  dedicated `jobConn`/`jobDbLock`, untouched by this ADR.

### `EventStore.executeCommandCore` and every request-reachable transaction site drop `dbLock`

`Api.executeCommandCore`, `GameJournal.save`/`migrateFromContentBlocks`,
`Administration.importEventsStreamHandler`, and
`Administration.appendCompensatingEventCore` (ADR-0032's fourth site) all
lose their `dbLock: SemaphoreSlim` parameter and the
`dbLock.Wait() ... finally dbLock.Release()` wrapper around their bodies —
there is no longer a shared connection object for a lock to protect. The
eta-expanded generic `executeCommand` shadow in `Api.create` and its
intermediate helper functions (`addMovieToLibraryImpl`/`addMovieToLibrary`,
`addSeriesToLibraryImpl`/`addSeriesToLibrary`, `runSteamFamilyImport`,
`steamFamilyImportHandler`, `runJellyfinImport`, `attachSteamToGameCore`) is
retained exactly as ADR-0030 required (F#'s value restriction still forces
the full eta-expansion, not a bare partial application), just without the
`dbLock` argument.

`requestDbLock` itself is deleted from `Composition.fs` — `grep -rn
requestDbLock src/` returns nothing. ADR-0028's `jobConn`/`jobDbLock` and
the vestigial `manualSyncTriggerLock` are untouched: they guard a
*different* connection object (the dedicated job connection) for a
*different* reason (two job triggers on that one object), orthogonal to the
request-connection sharing this ADR removes.

### The five raw Giraffe SSE handlers open one connection per stream, not per DB call

`steamFamilyImportHandler` (`Api.fs`), `exportEventsStreamHandler`,
`importEventsStreamHandler`, `projectionRebuildStreamHandler`, and
`driftCheckStreamHandler` (`Administration.fs`) each take `factory` in place
of a live `conn` and open exactly one `use conn = factory()` at handler
entry, held for the whole stream — these are rare, long-running,
operator-initiated operations, and `importNdjson` in particular is one
atomic transaction that must stay on one connection throughout.
`driftCheckStreamHandler` keeps its private throwaway `Data
Source=:memory:` shadow connection (ADR-0031) exactly as-is; only its
*live* connection becomes `factory()`-sourced.

### The Composition-level config-getter closures also move off the shared bootstrap `conn`

`getTmdbConfig`/`getRawgConfig`/`getSteamConfig`/`getJellyfinConfig`
(`Composition.fs`) read live settings from the database and are invoked from
request-serving record members — potentially concurrently, across different
in-flight requests. Left closing over the single bootstrap `conn`, they
would have silently re-introduced exactly the object-level race this ADR
otherwise eliminates (concurrent `SELECT` command creation/disposal on one
shared connection from multiple threads is the same hazard class ADR-0028
identified, not limited to writes or to `BeginTransaction`). Each now opens
its own short-lived `use conn = connectionFactory()` per call instead.

## Alternatives considered

- **Leave `requestDbLock` in place indefinitely (do nothing).** ADR-0030
  explicitly left this open as an option ("it may be dismissed if the
  interim gate proves sufficient... but it is not a mandate either way").
  Rejected here because the residual read×write/read×read race ADR-0030
  accepted as open is real (per ADR-0028's corrected premise) even though
  unreproduced, and the interim gate serializes every request's DB moment
  even when nothing about SQLite's file-level concurrency requires it —
  paying an ongoing tax for a problem the connection-per-request pattern
  removes by construction rather than by locking.
- **A coarser lock covering every `conn` touch, closing the residual race
  without a full connection-per-request migration.** Rejected for the same
  reason ADR-0030 rejected it at the choke-point scale: this app has
  multi-minute foreground operations over a connection (Steam Family import,
  `runJellyfinImport`, projection rebuild) that would serialize behind
  ordinary library reads and each other's DB moments for the whole app if
  the lock covered every touch.
- **A connection pool sized >1 with manual checkout/return instead of a bare
  factory closure.** Rejected as unnecessary complexity: `Microsoft.Data.Sqlite`
  already pools physical connections per connection string internally, so a
  bare `unit -> SqliteConnection` factory gets pooling's cheapness for free
  without a second, hand-rolled pool on top of it (YAGNI — a one-method
  factory function needs no interface either).

## Consequences

### Positive
- The residual read×write/read×read race ADR-0030 explicitly accepted as
  open is closed structurally: there is no longer a shared mutable
  connection object on the request path for any access pattern to race on.
- `requestDbLock` and its associated request-path serialization are
  retired — concurrent requests' DB moments, not just their network I/O, now
  overlap freely (each on its own connection, safe per ADR-0028's
  file-level-serialization finding).
- The `RequestConnectionConcurrencyTests.fs` regression is a *stronger*
  proof than before: 25 concurrent `addFriend` calls now each open a
  genuinely separate connection (not a lock-serialized shared one) and still
  produce zero exceptions and zero lost/duplicated writes — direct evidence
  the file-level WAL+`busy_timeout` serialization ADR-0028 proved for the
  job path holds for real concurrent request connections too.
- `EventStore.configureConnection`, a clean split of the pragma block, makes
  the "pragmas apply per-connection, schema applies once" distinction
  explicit and enforced (the factory literally cannot call table/FTS
  creation — it isn't a parameter it has access to).

### Negative / accepted tradeoffs
- Every request-serving record member and SSE handler across `Api.fs`
  (172 members) and `Administration.fs` (13 members plus 4 handlers) gained
  a `use conn = factory ()` line — a wide, mechanical edit, though each site
  is a one-line, uniform change.
- `triggerJellyfinSync`/`JellyfinSync.triggerSync` needed a real signature
  change (`conn` → `factory`, `runImport: unit -> Async<...>` →
  `runImport: SqliteConnection -> Async<...>`) rather than a mechanical
  `use conn = factory()` insertion, because its background work is
  genuinely detached (`Async.Start`) from the triggering request — a
  `factory()` result cannot cross that boundary without becoming a
  use-after-dispose bug.
- Under genuine per-request connections, a same-stream write race now
  surfaces as `EventStore.ConcurrencyConflict` ("please retry") via
  `busy_timeout` rather than via the semaphore — intended behavior,
  essentially never hit in this single-user app (no two browser tabs racing
  a write to the exact same stream in the same instant).
- 4 test files (`AdministrationTests.fs`, `GameJournalTests.fs`,
  `JobRunsTests.fs`, `RequestConnectionConcurrencyTests.fs`) needed their
  fixtures rebuilt around a new shared `tests/Server.Tests/TestDb.fs`
  helper (`TestDb.withTempDbFactory`) — a per-test, file-backed SQLite
  fixture (not shared-cache `:memory:`, which is destroyed when its last
  connection closes and doesn't use WAL, so it wouldn't exercise the
  file-level serialization this migration ships).
- A pre-existing, unrelated test flake was discovered (not caused) during
  this work: `Administration.fs`'s `runningJobs` claim guard is a
  module-level `ConcurrentDictionary` shared across the whole test process,
  and `JobRunsTests.fs` happened to reuse job names
  (`"Job C"`/`"Job D"`/`"Job E"`) also used by
  `JobConnectionConcurrencyTests.fs`, occasionally colliding under
  Expecto's default parallel test execution. Mitigated narrowly by
  namespacing `JobRunsTests.fs`'s job names; the underlying shared-singleton
  test-fixture architecture is tracked separately
  (administration-jrflk, backlog).

### Neutral
- `GameJournal.migrateFromContentBlocks` still runs on the single-threaded
  bootstrap `conn`, unaffected by this ADR beyond losing its now-unnecessary
  `dbLock` parameter — it always ran once, at startup, before any request
  concurrency existed.

## References

- `src/Server/Composition.fs` — `createConnectionFactory`, `connectionFactory`,
  the four config-getter closures (`getTmdbConfig`/`getRawgConfig`/
  `getSteamConfig`/`getJellyfinConfig`), `requestDbLock` removed entirely.
- `src/Server/EventStore.fs` — `configureConnection` (public, split out of
  `initialize`).
- `src/Server/Api.fs` — `executeCommandCore` and every intermediate helper
  function lose `dbLock`; `create`'s `factory` parameter; every record
  member's `use conn = factory ()`; `steamFamilyImportHandler`'s `factory`
  parameter.
- `src/Server/Administration.fs` — `appendCompensatingEventCore` loses
  `dbLock`; `create`'s `factory` parameter; every record member's
  `use conn = factory ()`; the four raw handlers' `factory` parameter.
- `src/Server/GameJournal.fs` — `save`/`migrateFromContentBlocks` lose
  `dbLock`.
- `src/Server/JellyfinSync.fs` — `triggerSync`'s `factory` parameter and the
  `use conn = factory ()` moved inside the spawned background async;
  `runImport`'s signature change to take the connection as an argument.
- `tests/Server.Tests/TestDb.fs` — the new shared per-test temp-file DB
  fixture (`TempDb`, `withTempDbFactory`).
- `tests/Server.Tests/AdministrationTests.fs`,
  `tests/Server.Tests/JobRunsTests.fs`,
  `tests/Server.Tests/RequestConnectionConcurrencyTests.fs` — rebuilt on
  `TestDb.withTempDbFactory`.
- `tests/Server.Tests/GameJournalTests.fs` — `dbLock` argument dropped from
  `GameJournal.save`/`migrateFromContentBlocks` call sites (still
  `:memory:` — these call the plain functions directly, no factory
  involved).
- ADR-0003 — SQLite/WAL baseline (file-level write serialization across
  separate connections) this migration leans on directly.
- ADR-0028 — proved the "dedicated connection per independent unit of work,
  serialized at the file level" pattern safe, at the scale of one dedicated
  job connection; this ADR generalizes it to one connection per
  request/SSE operation.
- ADR-0030 — the interim `requestDbLock` gate this ADR supersedes and
  retires.
- ADR-0032 — the compensating-event composer, whose
  `appendCompensatingEventCore` is the fourth (and last) request-reachable
  transaction site to lose `dbLock` here.
- administration-mz6kp — the task that shipped this migration.
- administration-jrflk (backlog) — the pre-existing, unrelated test-fixture
  flake discovered (not caused) during this task's verification.
