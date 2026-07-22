---
id: administration-cx92m
title: Audit whether the single shared SqliteConnection is safe under request×request concurrency, and decide per-operation connections vs. a global gate
status: backlog
type: spike
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [sqlite, concurrency, architecture, reliability]
related_adrs: [0003, 0024, 0026]
related_research: []
prior_art: [administration-tj8n2]
---

## Why
Fixing administration-tj8n2 (scheduled-job timers racing on the shared
`SqliteConnection`) surfaced a broader, pre-existing fact: the *entire* server
runs on **one** `SqliteConnection` (`conn`), built once in
`Composition.createConnection` and threaded through everything — the domain
Fable.Remoting API (`Api.create`), the admin `IAdminApi`
(`Administration.create`), projections (`Projection.startAllProjections`), the
projection-rebuild SSE handler, and the scheduled jobs. Every module takes
`conn` directly; there is no common serialization choke point.

`Microsoft.Data.Sqlite.SqliteConnection` is not thread-safe for concurrent
command creation/disposal from multiple threads (that is exactly what crashed
the process in tj8n2). Kestrel/Giraffe genuinely dispatch concurrent requests
on different thread-pool threads, so **request×request** and **request×job**
races on the shared `conn` are technically live today. They have not crashed
only because a single user rarely lands two DB-touching operations in the same
instant — not because the shared connection is structurally safe. ADR-0024 and
ADR-0026 reasoned about SQLite's file-level *write serialization* (WAL +
`busy_timeout`, ADR-0003), which governs contention between *separate*
connection objects and says nothing about one connection object used
concurrently across threads; tj8n2's ADR corrects that premise for the
scheduled-job path but deliberately leaves the app-wide question here.

This is a **single-user, self-hosted app**, so the practical probability of a
crash is low — hence this is a non-blocking investigation, not an urgent fix.
But it is a known correctness gap worth deciding deliberately rather than
leaving implicit.

## What
Investigate the request×request / request×job concurrency safety of the single
shared `conn`, and recommend an approach. Candidate directions to evaluate
(none prescribed):
- **Per-operation / pooled connections** — open a fresh `SqliteConnection` per
  request or per DB operation, relying on `Microsoft.Data.Sqlite`'s built-in
  per-connection-string pooling. Removes shared mutable state; costs a
  connection-lifecycle change across many call sites.
- **A single global gate** — serialize all `conn` access behind one lock/
  semaphore. Smallest conceptual change; makes every DB touch mutually
  exclusive (acceptable for a single user? measure).
- **Something in between** — e.g. a connection-per-scope abstraction, or
  scoped connections only on the write paths.

Output is a decision (an ADR) plus, if the recommendation is cheap and
low-risk, the refactor itself — otherwise a follow-up implementation task.

Spike stop-loss: if, mid-spike, the mitigation is already known and cheap,
record it and stop — don't run the full audit for its own sake once the answer
is clear.

## Acceptance criteria
- [ ] A written finding stating, concretely, which shared-`conn` access paths
      are unsafe under concurrent requests (name the modules/call sites).
- [ ] A recommendation between per-operation/pooled connections, a global gate,
      or a hybrid — with the trade-off reasoning for a single-user WAL-mode
      SQLite app, recorded as an ADR.
- [ ] If the recommended mitigation is cheap and low-risk, it is implemented and
      the Expecto suite + `npm run build` stay green; otherwise a follow-up
      implementation task is captured with the ADR referenced.

## Notes
- Motivated by administration-tj8n2; reference tj8n2's ADR (the scheduled-job
  connection fix) as the finding that prompted this.
- Relevant existing ADRs: 0003 (SQLite/WAL baseline), 0024 (projection rebuild
  over the shared connection), 0026 (job-runs recorder shared connection).
- Likely wants an `architect` and/or `researcher` pass during refinement before
  it's workable.
