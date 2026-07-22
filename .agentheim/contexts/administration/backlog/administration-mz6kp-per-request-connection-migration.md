---
id: administration-mz6kp
title: Migrate Api.create/Administration.create and the raw Giraffe stream handlers from one shared SqliteConnection to per-request (factory-based) connections, retiring the ADR-0030 semaphore gate
status: backlog
type: refactor
context: administration
created: 2026-07-22
completed:
depends_on: [administration-cx92m]
blocks: []
tags: [sqlite, concurrency, architecture, reliability, refactor]
related_adrs: [0003, 0028]
related_research: []
prior_art: [administration-tj8n2]
---

## Why
administration-cx92m closes the empirically-observed request×request crash on
the shared `SqliteConnection` cheaply, with a process-wide `SemaphoreSlim`
serializing the 3 transaction-opening choke points (ADR-0030). That gate is the
*interim* state: it removes the crash but keeps the shared mutable connection and
its accepted residual read×write/read×read race, and it serializes writes that a
proper connection-per-request design would let run against the WAL file
independently.

The structurally correct fix — the one ADR-0030 explicitly defers as "not cheap"
— is to stop sharing one connection object at all: give each request/operation
its own `SqliteConnection` (via `Microsoft.Data.Sqlite`'s built-in
per-connection-string pooling), removing the shared mutable state by construction
and letting the semaphore gate be retired.

## What
Convert `Api.create` (`Api.fs`) and `Administration.create`
(`Administration.fs`) — and the 4 raw Giraffe stream handlers wired in
`Composition.fs:311-323` (`steamFamilyImportHandler`, `exportEventsStreamHandler`,
`importEventsStreamHandler`, `projectionRebuildStreamHandler`) — to take a
connection **factory** instead of a live `conn`, opening a scoped connection
(`use conn = factory()`) per operation. Retire the ADR-0030 `requestDbLock` once
the shared object is gone. Re-examine the SSE handlers' connection lifetime
explicitly — they hold a connection for potentially multi-minute operations, so
"one connection for the whole stream" may be the right scope there rather than
per-DB-call.

Known blast radius (from the cx92m architect pass, to be re-confirmed at work
time): ~150-200 edit sites across the two `create` records; the 4 stream
handlers; and **8 test files** (`AdministrationTests.fs`, `GameJournalTests.fs`,
`EventStoreNdjsonTests.fs`, `ProjectionRebuildTests.fs`, `EventStoreTests.fs`,
`PlaytimeTrackerTests.fs`, `FriendIntegrationTests.fs`,
`MoviesIntegrationTests.fs`) that each construct one `createInMemoryConnection()`
per test and pass it directly in. A plain `:memory:` database is private
per-connection, so a factory-per-call would silently hand out disconnected empty
databases — the tests must move to `Cache=Shared` in-memory connection strings or
a factory-based fixture. **This is the load-bearing risk of the migration** and
needs deciding before the production edits.

## Acceptance criteria
- [ ] (needs refinement — depends on ADR-0030 landing first) A connection-factory
      seam replaces the shared `conn` parameter of `Api.create` and
      `Administration.create`; no request path holds a reference to a
      process-shared `SqliteConnection`.
- [ ] The 4 raw Giraffe stream handlers open their own scoped connection with a
      deliberate, documented lifetime (per-stream vs. per-call).
- [ ] The ADR-0030 `requestDbLock` semaphore is removed, and the ADR is updated
      (or a superseding ADR written) to record that per-request connections
      replaced the interim gate.
- [ ] The 8 affected test files construct connections via the new
      factory/shared-cache pattern; `npm test` and `npm run build` stay green.
- [ ] The concurrent-`addFriend` e2e regression from cx92m still passes with the
      gate removed (per-request connections carry the safety on their own).

## Notes
- **Blocked on administration-cx92m / ADR-0030** — do not start until the interim
  gate exists and the connection strategy is decided; this task supersedes that
  gate. Needs a refinement pass (architect) once ADR-0030 is written to firm up
  the acceptance criteria above.
- Prior art: administration-tj8n2 / ADR-0028 already proved separate connections
  to the WAL file are the safe pattern (for the job path) — this generalizes that
  to every request.
- If working cx92m reveals the interim gate is sufficient for this single-user
  app indefinitely, this task may be dismissed rather than worked — it is the
  "do it properly" option, not a mandate.
