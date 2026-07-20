---
id: administration-xjmda
title: Compensating-event composer — append corrective events from the admin UI
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-v4y9g, design-system-001]
blocks: []
tags: [admin-console, event-store, surgery]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why
The idiomatic event-sourcing fix for bad data is not mutating history but appending a corrective event. Before any raw-surgery tooling exists, the safe path should be the easy path: fix a wrong rating, a wrong date, a wrong slug by appending the correcting event from the stream drill-in page.

## What
- On a stream's drill-in page (administration-v4y9g), an "Append corrective event" action: pick an event type valid for that stream's BC, edit the payload JSON (pre-filled template from the chosen type), append via the normal `EventStore.appendToStream` with correct expected-position handling.
- Appended events flow through the projections like any other event (run catch-up after append, same as command handlers do).
- Guardrails: JSON payload validated against the chosen event type's decoder before append (reject unparseable payloads); confirmation step in a paper-overlay dialog showing exactly what will be appended.

## Acceptance criteria
- [ ] From a movie stream, appending e.g. a `Personal_rating_set` corrective event updates the projection and the movie detail page.
- [ ] A payload that the server-side decoder rejects is refused with a clear error, nothing appended.
- [ ] The appended event is indistinguishable from an organically produced one (same stream position sequence, metadata notes admin origin).

## Notes
Needs refinement: where does the list of "valid event types + payload templates per BC" come from? Options: reflect over the server event DUs, or hand-maintain a registry in Administration.fs. Reflection keeps it honest but Thoth encoding shapes vary — a spike may be warranted. Metadata should mark admin-originated events (e.g. `{"source":"admin-console"}`) for auditability.
