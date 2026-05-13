---
id: 0003
title: SQLite as the sole persistence layer
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0003: SQLite as the sole persistence layer

> Backfill — the persistence choice for both event store and projections.

## Context

Mediatheca is a single-user, self-hosted app deployed in a Docker container on a personal Linux server. There are no concurrent writers, no horizontal scaling, no replica concerns, no team operating it. The database file lives next to the binary.

The application uses event sourcing ([[0002-event-sourcing-cqrs]]) so the storage layer needs:
- Reliable append-only writes for the event log.
- Indexed reads for projections.
- Transactional updates so one command's event(s) and any synchronous projection updates land together (or not at all).
- Trivial backup (a file copy).

## Decision

Use **SQLite** for both the event store and all projection read models, in a single database file (`mediatheca.db`) inside the server's `AppContext.BaseDirectory`. Pragmas: WAL mode (concurrent reads + serialized writes), NORMAL sync (good enough on a personal box), foreign keys enabled, 5s busy timeout.

Access SQLite via **Donald** (a lightweight F# wrapper over `Microsoft.Data.Sqlite`).

## Consequences

### Positive
- Zero ops. Backup = copy the file. Restore = put it back.
- Single transactional boundary across event-append + projection-update.
- Embedded — no separate process, no network hop, no auth surface.
- Donald keeps query code typed and concise without an ORM's weight.
- WAL gives reasonable concurrent read performance for the dashboard.

### Negative
- Single-machine ceiling. Cannot scale to multiple writers or a remote DB without a rewrite.
- Schema migrations are manual (no tooling like EF Migrations bolted on).
- Type affinity quirks if not careful — Donald helps but doesn't eliminate them.
- Backup-while-running needs WAL-aware tooling (`sqlite3 .backup`, not raw `cp`).

### Neutral
- Performance is fine at expected library sizes (low thousands of items, tens of thousands of events). Will revisit if event volume hits low millions.

## Alternatives considered

- **Postgres** — overkill for one user. Adds a service to operate, an auth surface, and a network hop for no benefit.
- **EventStoreDB / Marten** — purpose-built event stores. Too heavy for the scope; SQLite + a few well-indexed tables does the job.
- **LiteDB / RavenDB** — document stores would have made event payloads easy to write but ad-hoc projection queries harder.

## References

- `src/Server/EventStore.fs`, `src/Server/Projection.fs`.
- `CLAUDE.md` § "Tech Stack", § "Architecture".
