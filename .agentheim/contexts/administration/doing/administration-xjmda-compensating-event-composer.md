---
id: administration-xjmda
title: Compensating-event composer — append corrective events from the admin UI
status: doing
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-v4y9g, design-system-001]
blocks: [administration-wwc36]
tags: [admin-console, event-store, surgery]
related_adrs: [0002]
related_research: []
prior_art: []
---

## Why
The idiomatic event-sourcing fix for bad data is not mutating history but appending a corrective event. Before any raw-surgery tooling exists (administration-wwc36 builds after this), the safe path should be the easy path: fix a wrong rating, a wrong date, a wrong slug by appending the correcting event from the stream drill-in page.

## What
On a stream's drill-in page (administration-v4y9g, `src/Client/Pages/StreamDetail/`), an "Append corrective event" action driven entirely by each BC's **existing** `Serialization.serialize`/`deserialize` seam — no template registry, no DU reflection (the wire format diverges from the DU shape: `Game_status_changed` serializes `GameStatus` as a nested DU `"Case"` field, `EventFormatting.fs:224-226`).

- **Codec dispatch:** a new `Administration.fs` registry — one entry per BC — wrapping each BC's public `Serialization.serialize`/`deserialize` (`Movies.fs:306/343`, `Games.fs:368/438`, `Series.fs:735/790`, `Friends.fs:127/136`, `Catalogs.fs:228/250`, `ContentBlocks.fs:230/267`; all public, no exposure change) into one BC-agnostic `eventType -> rawData -> (canonicalEventType * canonicalData) option`, composed as `deserialize eventType data |> Option.map serialize`. Prefix-dispatched by stream id using the same `if/elif StartsWith` idiom `EventFormatting.formatEvent` already uses (`EventFormatting.fs:382-390`), reusing the prefix strings from `boundedContextPrefixes` (`Administration.fs`) — doc-comment the sync convention like `projectionTables` does.
- **Pick from types that exist (clone a real event):** operator picks an event type from those present under the stream's BC prefix; the composer pre-fills the payload JSON from the most recent **real** event of that type — the target stream first, falling back BC-prefix-wide.
- **Validate-by-round-trip append:** the bytes written to `events.data` are the **re-serialized canonical form** (`serialize (deserialize eventType edited |> Option.get)`), not the operator's raw edit — validation gate and canonicalization in one step. Append via `EventStore.appendToStream conn streamId (EventStore.getStreamPosition conn streamId) [eventData]` (`EventStore.fs:370`) — pure INSERT, expected-position concurrency-checked exactly like `Api.fs:32`. Never the explicit-rowid path (that is `importNdjson`'s empty-store territory only).
- **Catch-up:** after append, loop `Projection.runProjection conn handler` over the app-wide `projectionHandlers` list `Administration.create` already closes over (`Composition.fs`) — identical to the command-handler idiom at `Api.fs:48-49`. No new plumbing.
- **Audit metadata:** appended events carry `metadata = {"source":"admin-console"}` (`EventData.Metadata` accepts arbitrary JSON, `EventStore.fs:24-28`; organic handlers write `"{}"`). This is the one intentional, projection-invisible difference from an organic event.
- **Guardrail dialog:** a paper-overlay confirmation (ADR-0016) showing the exact canonicalized (post-round-trip) payload that will be appended.

## Acceptance criteria
- [ ] The "types seen" query returns every event type with ≥1 instance anywhere under the stream's BC prefix — Expecto test seeds two same-BC streams with disjoint type sets and asserts the union is returned for either stream.
- [ ] The pre-fill template is the most recent instance's payload on the target stream if one exists there, else the most recent instance's payload BC-prefix-wide — Expecto test covers both branches.
- [ ] The bytes written to `events.data` are the re-serialized canonical form, not the raw edited string — Expecto test asserts stored `data == serialize (deserialize eventType edited |> Option.get)` (not the raw input) for at least one BC whose wire format diverges from naive round-tripping.
- [ ] A payload for which `deserialize eventType data` returns `None` is refused: `appendToStream` is never called, no row inserted, an error surfaced — Expecto test asserts event count unchanged and an error result.
- [ ] Append uses `expectedPosition = EventStore.getStreamPosition conn streamId` read immediately before the call — Expecto test appends to the stream by another path between read and compose-append and asserts a concurrency conflict rather than a silent overwrite.
- [ ] After a successful append, `Projection.runProjection` has run for every handler in the app-wide `projectionHandlers` list — Expecto test asserts the affected BC's projection row reflects the new event with no separate rebuild step.
- [ ] The appended row's `metadata` is `{"source":"admin-console"}` while `event_type`/`data` shape and `stream_position`/`global_position` sequencing are indistinguishable from an organic event of the same type — Expecto test compares the composer-appended row against an organically-appended event of the same type (metadata the only permitted difference).
- [ ] From a movie stream, appending a `Personal_rating_set` corrective event updates the movie detail page's rating with no manual rebuild. [human-eye]
- [ ] The confirmation dialog renders in paper-overlay (ADR-0016) and shows the exact canonicalized payload that will be appended. [human-eye]

## Notes
**Resolved during refinement (architect, source-grounded):**
- Registry mechanism decided — the original "reflect over DUs vs hand-maintained registry" open question is closed in favor of the existing `serialize`/`deserialize` seam (clone-a-real-event). Reflection was rejected as dishonest about the wire format; a hand-maintained template registry was rejected as a second source of truth that drifts.
- Validate-by-round-trip is adopted deliberately: it is the validation gate and the canonicalization step at once, and is the only way to guarantee the "indistinguishable from an organically produced event" criterion given the DU-shape-vs-wire-format divergence.
- Two new `EventStore.fs` reads needed (near `getDistinctEventTypes`): a BC-prefix `DISTINCT event_type` query (index-backed via `idx_events_stream_id`), and a "most recent instance" query tried this-stream-first then prefix-wide.
- Catch-up reuses the injected `projectionHandlers` list — no new function.

**Residual open calls for the worker (confirm at implementation, not blockers):**
- Pre-fill fallback tiebreak: recommended this-stream-first then BC-prefix-wide; confirm if the builder prefers always-BC-wide.
- `IAdminApi` shape: whether "preview canonical payload for the dialog" and "commit append" are one RPC or two. Two-call (read-only preview → commit) gives the dialog a real canonical preview to render; single-call is fine for a single-user local app with no real TOCTOU risk. Not architecturally load-bearing.

**ADR at implementation:** warranted — "Compensating events via round-trip validation through each BC's existing serialize/deserialize seam, prefix-dispatched like `formatEvent`". This constrains how administration-wwc36's raw-surgery tooling is framed (wwc36 is the escape hatch for when this pattern does *not* apply). Worker takes the next free number at write time (latest committed is 0029; 0030 is contested by two other in-flight refinements per protocol.md — do not hardcode).

**Scope stays one task** — the server compose/validate/append endpoints and the client drill-in UI are one client/server capability seam and are only reviewable/testable together (contrast wwc36, correctly separate as a higher-risk raw-surgery capability).
