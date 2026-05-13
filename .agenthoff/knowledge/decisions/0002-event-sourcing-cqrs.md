---
id: 0002
title: Event sourcing + CQRS as the persistence model
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0002: Event sourcing + CQRS as the persistence model

> Backfill — the architectural choice the entire codebase is organized around.

## Context

Mediatheca is a *diary* as much as a *library*. The history of what happened — every watch session, every status change, every rating revision — is the product, not a side-effect of CRUD. A naive mutable model would have to layer change-tracking on top, and would lose the "this is what happened on Tuesday" framing.

Additionally, the dashboard surfaces (heatmaps, recently-watched, monthly breakdowns, HLTB comparisons) are intrinsically derived. Computing them off a normalized CRUD store would require either many ad-hoc queries or a parallel reporting layer — both more complex than projecting events into purpose-built read models.

The project is single-user, single-process, single-database; the operational tax of event sourcing (eventual consistency, projection rebuilds, schema evolution) is bounded.

## Decision

Persist every domain change as an **immutable event** in an append-only event store. Reads come from **projections** rebuilt from the event log, one per read concern. Commands enter the system, are validated against aggregate state reconstructed from events, and emit new events; events drive every projection.

Each bounded context (Movies, Series, Games, Friends, Curation) owns its own event family. The event store itself is shared infrastructure ([[administration]]).

## Consequences

### Positive
- Full audit trail by construction. The protocol log + event log answer "what happened" without extra machinery.
- Read models can be added without migrations — just write a new projection and replay.
- Domain modeling is forced into clear events (`Watch_session_recorded`, `Game_status_changed`, `Episode_watched`) rather than mutable fields.
- Refactors can replay the event stream through new code to validate behavior.
- The dashboard's many derived views fall out naturally as projections.

### Negative
- Adding any new field or behavior is two steps: event change + projection change. There is no "just add a column" shortcut.
- Projection rebuilds become a real operational concern once event counts grow.
- Eventual consistency between command-side and read-side (in practice negligible for single-user, but real).
- New developers (or LLMs) must understand the event-sourced control flow before they can change behavior safely.

### Neutral
- Schema evolution requires conscious thought (event upcasting) — but that's also a form of documentation.

## Alternatives considered

- **Plain CRUD with audit columns** — would have worked for a smaller scope, but would have made the diary surfaces and rich activity timeline much harder to express.
- **Snapshots + journal hybrid** — possible later optimization, not needed yet at this volume.
- **External event store (EventStore DB, Marten)** — overkill for a single-user SQLite-backed app. See [[0003-sqlite-persistence]].

## References

- `src/Server/EventStore.fs`, `src/Server/Projection.fs`, per-BC `*Projection.fs`.
- `CLAUDE.md` § "Architecture".
