---
id: 0022
title: Stream drill-in flattens typed projection DTOs and links dangling cross-references without verification
scope: administration
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [administration-v4y9g]
related_research: []
---

# ADR 0022: Stream drill-in flattens typed projection DTOs and links dangling cross-references without verification

## Context

`administration-v4y9g` adds a per-stream drill-in (`/admin/streams/<streamId>`)
showing one aggregate's full event history (via `EventStore.readStream` +
`EventFormatting.formatEvent`, per ADR-0002's event-sourcing model) side by
side with what the matching projection currently says about it. Two design
questions had no obvious single answer and are recorded here so a future
worker doesn't redo the same tradeoff analysis.

## Decision

### Projection panel reuses each BC's typed `getBySlug`, flattened to loose fields

`Administration.projectionRowFor` dispatches on `stream_id` prefix (`Movie-`,
`Series-`, `Game-`, `Friend-`, `Catalog-` — the same five prefixes
`boundedContextPrefixes` already knows from ADR-0020) straight to that BC's
existing `*Projection.getBySlug` (`MovieProjection.getBySlug`,
`SeriesProjection.getBySlug`, etc.) — the same query every media detail page
already uses — and flattens the result into `ProjectionStateRow { Kind;
Fields: (string * string) list; DetailLink }` rather than either (a) writing
new bespoke SQL against the raw projection tables, or (b) inventing a typed
"admin view" DTO per BC. `Fields` is display-only label/value pairs, chosen
per BC to be useful at a glance, not an exhaustive dump of the underlying
table.

Other stream prefixes (e.g. `ContentBlocks-`) get no projection row —
`ProjectionRows` is empty, not an error. The task's acceptance criteria only
called for Movie/Series/Game/Friend/Catalog; ContentBlocks streams already
show up fully in the timeline, just without a "current state" panel.

### Cross-links render without verifying the target stream exists

`EventFormatting.crossLinksFromPayload` extracts known reference fields
(`friendSlug`, `movieSlug`, `seriesSlug`, `gameSlug`) from a payload and
builds the target stream id (`Friend-<slug>`, etc.) by string prefixing —
it does **not** query the event store to confirm a stream with that id has
ever had an event appended. A dangling reference (the friend was later
removed; the slug was mistyped in an old event) still renders as a clickable
link.

This is safe *because* the drill-in page's own empty-state is unremarkable:
`StreamDetailDto` for a stream with no events is just `Entries = []`, and
`StreamDetail.Views` renders that as "No events found for this stream." —
the same message any stream getter shows for a stream that simply hasn't
been read yet by a projector. There is no 404, no error banner, no dead end.
Verifying existence server-side before offering a link would cost an extra
`EventStore.readStream` call (or a new "stream exists" query) per cross-link
per timeline render, for a case the UI already handles gracefully for free.

## Consequences

### Positive
- No new SQL to write or maintain for the projection panel — it rides on
  each BC's existing, already-tested `getBySlug`, so it can't drift from what
  the real detail page shows.
- Cross-link rendering is O(1) string work — no extra queries, no async
  existence checks blocking the timeline from rendering.
- Unknown/removed reference targets degrade to an ordinary empty timeline,
  not a broken link — satisfies the task's caution against "a link that 404s
  into an empty page."

### Negative
- `ProjectionStateRow.Fields` is loose (`(string * string) list`), not typed
  — a future consumer wanting structured access to a specific field (rather
  than display) would need to re-derive it, not read it off this DTO.
- If a BC's `getBySlug` becomes expensive (heavier joins added later), the
  drill-in pays that cost on every visit with no caching — acceptable today
  (admin tool, low traffic) but worth revisiting if it becomes true.
- A cross-link can point at a stream id that never existed and never will
  (e.g. a typo'd slug baked into an old event) — indistinguishable, from the
  drill-in's perspective, from "removed" or "not yet read." Good enough for
  an operator tool; would need a distinct signal if this became user-facing.

## Alternatives considered

- **Bespoke SQL per BC for the admin panel** — rejected: duplicates
  `getBySlug`'s query and risks drifting from what the real detail page shows.
- **Typed per-BC admin DTOs (`MovieAdminRow`, `SeriesAdminRow`, ...)** —
  rejected: five near-identical DTOs for a display-only panel is more
  surface than the loose `Fields` shape, for no query-time benefit (nothing
  downstream consumes these programmatically).
- **Verify cross-link targets exist before rendering (extra `EventStore`
  query per link)** — rejected: the empty-timeline fallback already makes a
  dangling link harmless, so the verification would cost real work for no
  behavioral difference the user would notice.

## References

- `src/Server/Administration.fs` — `projectionRowFor`, `toTimelineEntry`, `getStreamDetail`.
- `src/Server/EventFormatting.fs` — `crossLinksFromPayload`, `crossLinkFields`.
- `src/Shared/Shared.fs` — `StreamCrossLink`, `StreamTimelineEntry`, `ProjectionStateRow`, `StreamDetailDto`.
- `src/Client/Pages/StreamDetail/` — `Types.fs`, `State.fs`, `Views.fs`.
- `src/Client/Router.fs` — `Stream_detail` page case, `/admin/streams/<streamId>` route.
- `tests/Server.Tests/AdministrationTests.fs` — `getStreamDetail` test cases.
- ADR-0002 (event sourcing + CQRS) — the history/current-state juxtaposition this feature is built on.
- ADR-0020 (event explorer FTS5 + keyset pagination) — origin of `boundedContextPrefixes`, reused here.
