---
id: curation-w9fkq
title: Backfill `media_type` on legacy catalog entries, widen `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)`, and relax `Add_entry` back to strict `(MediaType, slug)` pair identity
status: backlog
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [curation-cyxbc]
blocks: []
tags: [catalogs, curation, typed-entries, follow-up]
related_adrs: [0079]
related_research: []
prior_art: [curation-cyxbc]
---

## Why

ADR-0079 (typed catalog entries) always intended a catalog entry's true identity to be the pair
`(MediaType, slug)`, not the bare slug — but its §5 deliberately kept
`catalog_entries`' `UNIQUE(catalog_slug, movie_slug)` unwidened, because SQLite treats `NULL`s
as distinct in a UNIQUE index, so widening it before every legacy (untyped) row has a real
`media_type` would silently drop duplicate protection for all of them.

`curation-cyxbc`'s implementation surfaced the sharp edge of that deferral: `Catalogs.decide`'s
`Add_entry` cannot honor §3's typed-pair duplicate rule (two different-typed entries CAN share a
slug) while §5's constraint stays slug-only — accepting such an add would emit an `Entry_added`
event the projection's `INSERT OR REPLACE` then silently clobbers (confirmed by exercising it).
A `tactical-modeler` consult during that task ruled: reject any same-slug add within one catalog,
typed or not, until this follow-up lands. That is strictly more conservative than the language
this BC's README states ("a media item appears at most once per catalog" was meant per-type, not
per-slug) — this task closes that gap.

## What

- A one-off backfill that fills `media_type` on every legacy (`media_type IS NULL`)
  `catalog_entries` row, using the exact same read-time inference
  `CatalogProjection.getEntries` already applies for legacy rows today (movie, then series,
  default movies) — written back so it survives a projection rebuild, not just inferred at read
  time. Decide the backfill's shape (imperative one-off script vs. a corrective event) — ADR-0079
  §5 flags this as open.
- Widen `catalog_entries`' `UNIQUE(catalog_slug, movie_slug)` to
  `UNIQUE(catalog_slug, media_type, movie_slug)` once the backfill guarantees no row is left
  `NULL`.
- Relax `Catalogs.decide`'s `Add_entry` duplicate rule back to ADR-0079 §3's original wording:
  two typed entries are duplicates iff `(MediaType, slug)` match; either side legacy-untyped still
  rejects on slug equality alone (untyped rows should no longer exist post-backfill, but the rule
  should still defend against it).
- Restore the same-catalog cross-type test coverage `curation-cyxbc` had to narrow to cross-catalog:
  `CatalogsTests.fs`'s "Add_entry rejects a same-slug entry of a different media type while the
  projection key is slug-only (ADR-0079 §5)" case flips back to an acceptance case, and
  `CatalogProjectionTests.fs` gets a same-catalog movie+book-sharing-a-slug case alongside the
  existing cross-catalog one.
- Append the amendment note (drafted in `curation-cyxbc`'s Outcome) to
  `.agentheim/knowledge/decisions/0079-catalog-entries-typed-by-mediatype-legacy-entries-untyped.md`
  if not already applied, and record this task's own resolution as a further dated note there.

## Acceptance criteria

- [ ] Every pre-existing `catalog_entries` row with `media_type IS NULL` is backfilled to a real
      `MediaType` value via the same inference `getEntries` uses today for legacy rows; the
      backfill is safe to run against a database that has already been backfilled (idempotent).
- [ ] `catalog_entries` gets `UNIQUE(catalog_slug, media_type, movie_slug)`, replacing
      `UNIQUE(catalog_slug, movie_slug)`.
- [ ] `Add_entry` accepts two typed entries of different `MediaType` sharing a slug in the SAME
      catalog again; an Expecto case proves it (replacing/complementing the rejection case
      `curation-cyxbc` added).
- [ ] A projection test proves a movie and a book sharing a slug in the SAME catalog each resolve
      to their own row via `getEntries`.
- [ ] Legacy-untyped vs. anything still rejects on slug equality alone (defensive; no legacy rows
      should remain after the backfill, but the rule stays).
- [ ] `npm run build`, `npm test`, `npm run test:client` are green.