---
id: administration-cx92m
title: Audit whether the single shared SqliteConnection is safe under request×request concurrency, and decide per-operation connections vs. a global gate
status: doing
type: spike
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: [administration-mz6kp]
tags: [sqlite, concurrency, architecture, reliability]
related_adrs: [0003, 0024, 0026, 0028]
related_research: []
prior_art: [administration-tj8n2]
---

## Why
Fixing administration-tj8n2 (scheduled-job timers racing on the shared
`SqliteConnection`) surfaced a broader, pre-existing fact: the *entire* server
runs on **one** `SqliteConnection` (`conn`), built once in
`Composition.createConnection` (`Composition.fs:53`) and threaded through
everything — the domain Fable.Remoting API (`Api.create`, `Composition.fs:290`),
the admin `IAdminApi` (`Administration.create`, `Composition.fs:291`), and four
raw Giraffe stream routes wired directly on `conn` (`Composition.fs:311-323`).
Every module takes `conn` directly; there is no common serialization choke
point.

`Microsoft.Data.Sqlite.SqliteConnection` is not thread-safe for concurrent
command creation/disposal from multiple threads (ADR-0028's root-cause finding —
its own original crash was a plain `INSERT`, no transaction). Kestrel/Giraffe
genuinely dispatch concurrent requests on different thread-pool threads, so
**request×request** and **request×job** races on the shared `conn` are live
today.

Two facts landed the same day this spike was captured and largely pre-answer its
first question:
- **administration-tj8n2 shipped (ADR-0028):** scheduled jobs now use a
  *dedicated* connection + a per-command `SemaphoreSlim`, and ADR-0028 explicitly
  corrected the ADR-0024/0026 premise that WAL + `busy_timeout` made *one*
  connection object thread-safe across threads. The job path is fixed; the
  request path is not.
- **administration-a4d9b shipped:** its Playwright specs empirically proved that
  **concurrent `addFriend` calls crash the shared connection** with
  `SqliteConnection does not support nested transactions`
  (`tests/e2e/event-tail-follow.spec.ts:59-70` sequences its calls specifically
  to dodge this, citing this spike by name). So "is request×request concurrency
  unsafe?" is answered **yes, empirically** — this is a demonstrated correctness
  gap, not a theoretical one.

This is still a **single-user, self-hosted app**, so the practical crash
probability is low — hence non-blocking. But the answer is now known, which puts
the spike squarely in stop-loss territory: record the finding and the cheap
mitigation, decide deliberately, don't run an audit for its own sake.

## What
The builder chose to **keep this a full spike** (not collapse to decision-only).
Deliver all three: (1) the complete call-site finding, (2) the ADR-grade
recommendation, (3) implement the mitigation inline if cheap (it is — see below)
and capture the expensive part as a follow-up.

An architect pass (source-grounded, 2026-07-22) has already done the
investigation; the worker formalizes it, writes the ADR, and implements the
narrow gate. Its conclusions:

**Finding.** A grep for `conn.BeginTransaction()` across `src/Server` returns
exactly **3 request-reachable call sites** — the crash-producing (write) class:
- `EventStore.appendToStream` (`EventStore.fs:376`), reached via
  `Api.executeCommand` (`Api.fs:17-50`), used at ~132 sites across `Api.create`.
  This is the empirically-crashing path.
- `EventStore.importNdjson` (`EventStore.fs:505`), via
  `Administration.importEventsStreamHandler` (`Administration.fs:442`).
- `GameJournal.save` (`GameJournal.fs:64`), via `Api.fs:3024`.

Every read-only accessor on the shared `conn` is *technically* the same risk
class (ADR-0028: object-level, not just transaction-level), but both crashes
observed to date were **write×write**; read×write/read×read has never been
reproduced here.

**Recommendation (→ ADR-0030, next free number confirmed; latest is 0029).**
Wrap only the 3 transaction-opening choke points behind **one process-wide
`SemaphoreSlim(1,1)`** (e.g. `requestDbLock`, built in `Composition.fs` beside
ADR-0028's `jobDbLock`), acquired around the synchronous DB-touching body only,
never across awaited I/O — generalizing ADR-0028's exact idiom from the job
connection to the request connection. Rejected alternatives: a literal global
gate over *all* `conn` touches (would serialize ordinary library reads behind
this app's multi-minute foreground operations — Steam Family import, projection
rebuild — a worse trade than ADR-0028 already declined); and per-operation/pooled
connections (structurally correct but not cheap — see the follow-up). The ADR
must explicitly name the residual read×write/read×read command-object race as
*accepted, not closed* (real per ADR-0028's reasoning, never reproduced here).

**Cheap-or-not.** The narrow gate is **cheap → implement inline**: one new
`SemaphoreSlim` in `Composition.fs`, threaded into `Api.create`/
`Administration.create`, acquired at the 3 sites; test impact is mechanical
(construction call sites gain the parameter). The full per-request-connection
migration (option a) is **not cheap → split to administration-mz6kp**: ~150-200
edit sites across `Api.fs`/`Administration.fs`, the 4 raw Giraffe handlers, and
8 test files whose `:memory:` connections would need shared-cache or
factory-based reconstruction.

## Acceptance criteria
- [ ] Finding recorded in the ADR: the 3 `BeginTransaction` call sites reachable
      from request threads on the shared `conn` are enumerated by module +
      function (`Api.executeCommand` / `Api.fs:17-50`; `GameJournal.save` /
      `GameJournal.fs:64`; `EventStore.importNdjson` / `EventStore.fs:505`),
      distinguished from the broader read/plain-write race that ADR-0030 accepts
      as residual.
- [ ] ADR-0030 written and accepted: recommends the narrow `SemaphoreSlim` gate,
      explicitly generalizes ADR-0028's per-command-lock idiom to the request
      connection, and explicitly names the accepted residual read×write/read×read
      risk. (Confirm 0030 is still free at write time; renumber if a concurrent
      ADR landed.)
- [ ] A single process-wide `SemaphoreSlim(1,1)` guards `Api.executeCommand`'s
      body, `GameJournal.save`, and `importEventsStreamHandler`'s call into
      `EventStore.importNdjson` — acquired only around the synchronous
      DB-touching section, released before any awaited I/O.
- [ ] `tests/e2e/event-tail-follow.spec.ts`'s `addFriends` helper is changed to
      issue its `addFriend` calls **concurrently** (`Promise.all`), repeating the
      concurrent burst enough times to make a surviving race observable, and the
      spec passes with no `SqliteConnection does not support nested transactions`
      error — the regression proof, mirroring ADR-0028's
      `JobConnectionConcurrencyTests`.
- [ ] `npm test` (Expecto) and `npm run build` both stay green after the 3 call
      sites and their construction sites gain the new `SemaphoreSlim` parameter.
- [ ] The follow-up migration task (administration-mz6kp) exists in `backlog/`
      with `depends_on: [administration-cx92m]`, referencing ADR-0030 as the
      interim state it supersedes. (Captured at refine time — see Notes.)

## Notes
- Motivated by administration-tj8n2; ADR-0028 is the scheduled-job connection fix
  whose root-cause reasoning this spike generalizes to the request path.
- Relevant existing ADRs: 0003 (SQLite/WAL baseline), 0024 (projection rebuild
  over the shared connection), 0026 (job-runs recorder), 0028 (dedicated job
  connection + per-command lock — the pattern to generalize).
- The follow-up per-request-connection migration was captured at refine time as
  **administration-mz6kp** (backlog, depends_on this spike). If working the spike
  reveals the migration is unnecessary or should change shape, adjust or dismiss
  mz6kp accordingly.
- Existing in-codebase precedents for the lock idiom: `Api.fs:1120`
  `manualSyncTriggerLock`, and ADR-0028's `jobDbLock` in `Composition.fs`.
- All acceptance criteria are machine-checkable (ADR-0061; no `[human-eye]`). The
  concurrent-burst e2e assertion is probabilistic — repeat the burst rather than
  relying on a single pass to raise confidence the race is closed.
