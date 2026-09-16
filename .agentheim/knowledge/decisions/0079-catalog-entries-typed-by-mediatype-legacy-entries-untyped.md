---
id: 0079
title: Catalog entries are typed by MediaType — `Entry_added` carries the media type, legacy entries stay untyped and are resolved at read time; the stored JSON key `movieSlug` and the `movie_slug` column are kept
scope: curation
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
related_tasks: [curation-cyxbc, books-f3sb2, curation-w9fkq]
related_research: []
---

# ADR 0079: Catalog entries are typed by MediaType

## Context

A catalog entry has been keyed by an **untyped slug** since the Curation BC was built: the
`Entry_added` event carries `{entryId, movieSlug, note}`, the projection stores it in
`catalog_entries.movie_slug` (`UNIQUE(catalog_slug, movie_slug)`), and the Shared DTOs expose it
as `MovieSlug` / `MovieName` / `MovieYear` / `MoviePosterRef` plus a server-computed
`RoutePrefix: string`. Series, then Games, were added to catalogs *conformistly* by reusing the
same slug field — a `// stores any media slug` comment on the column is the only trace.

The media type was never recorded. `CatalogProjection.getEntries` guesses it at read time by
LEFT JOIN order (`movie_list`, then `series_list`), defaulting the route to `movies`. Two
consequences surfaced while refining Books into catalogs (2026-09-16):

- **Games are broken in catalogs today.** The game detail page has the picker and
  `getCatalogsForGame`, but `getEntries` never joins `game_list` — a game entry renders as its raw
  slug, year 0, no cover, and links to `/movies/{slug}`. No test covers the projection queries.
- **Slugs collide across types.** Every media type slugs as `slugify(title)-year`, so a novel and a
  same-year film (or game) share a key. Under the untyped design the aggregate refuses the second as
  "already in this catalog", and the slug-only cascade used on media removal
  (`getEntriesByMediaSlug`) would delete the *other* type's entry.

Adding Books as a fourth guessed join would have deepened the same problem. The curation README
already states the intended language — an entry references a media item by `(MediaType, mediaId)` —
which the code never honoured. The builder's ruling: specific ubiquitous language in this BC;
knowing *which* kind of media an entry points at is worth an event-shape change.

## Decision

1. **A catalog entry references `(MediaType, slug)`.** `Catalogs.EntryAddedData` and `EntryState`
   gain `MediaType: Mediatheca.Shared.MediaType option`. Every new `Entry_added` is emitted with
   `Some` — the API's `AddCatalogEntryRequest` requires a `MediaType`. `None` exists only for
   **legacy events replayed from the store**; the decoder reads `mediaType` as an optional field and
   the encoder omits the key when `None`.
2. **Stored shapes are not rewritten.** The event JSON key stays `movieSlug` and the projection
   column stays `movie_slug` (the store is append-only; renaming a key would fork the decoder for no
   behaviour). The *Shared* vocabulary is renamed instead: `MediaSlug`, `Title`, `Year`,
   `PosterRef`, `MediaType` on `CatalogEntryDto`; `MediaSlug` + `MediaType` on `CatalogRef` and
   `AddCatalogEntryRequest`. `RoutePrefix` leaves the DTO; the client derives the route through a
   Shared `MediaType.routePrefix` helper (`movies` / `series` / `games` / `books`).
3. **Identity and duplicate rule in `decide`.** Two typed entries are duplicates iff
   `(MediaType, slug)` match. If *either* side is legacy-untyped, **slug equality alone rejects** —
   deliberately conservative, because the projection's `UNIQUE(catalog_slug, movie_slug)` still
   stands (see 5) and accepting the add would emit an event the projection cannot insert, a
   replay-breaking divergence. The rejection names the reason ("recorded before entries were typed").
4. **The projection resolves typed rows directly** — `catalog_entries.media_type TEXT NULL`,
   added by the idempotent `try ALTER TABLE … ADD COLUMN with _ -> ()` guard every other projection
   migration here uses — joining exactly the matching `*_list` table for all four media types.
   The join-order inference survives **only as the fallback for `media_type IS NULL`** rows, so
   legacy entries keep rendering exactly as before. Type-scoped lookups (`getCatalogsFor*`, the
   series-children `LIKE` lookup, and the removal cascade `getEntriesByMediaSlug`) filter on
   `media_type = @type OR media_type IS NULL`.
5. **`UNIQUE(catalog_slug, movie_slug)` is kept for now.** SQLite treats NULLs as distinct in a
   UNIQUE index, so widening it to `(catalog_slug, media_type, movie_slug)` would silently drop
   duplicate protection for every legacy row until they are backfilled. Widening the constraint and
   relaxing rule 3 to strict pair identity is a **follow-up**, gated on a projection rebuild that
   backfills `media_type` (the rebuild alone cannot — the legacy events carry no type; the backfill
   needs a one-off read-time inference written back, or a corrective event, to be decided then).

## Consequences

### Positive
- The BC's language and its code agree: an entry knows what it points at. Games and Books resolve
  by type, not by which table happens to match first.
- Slug collisions between types become representable for new entries; a removal cascade can no
  longer delete another type's entry.
- The stored event log is untouched; old databases upgrade via one nullable column.

### Negative
- Two identities coexist until the follow-up: typed pairs and legacy bare slugs. Rule 3 means a
  new book with the same slug as an old untyped movie entry is refused in that catalog.
- Every "add to catalog" call site (three modals today, four after Books) must send a `MediaType`
  — the DTO change is not a mechanical rename.

### Neutral
- `EventFormatting.crossLinkFields` maps `movieSlug` → `Movie-` for *all* events, so non-movie
  catalog entries already mislink in the event browser. Pre-existing, unrelated to this decision,
  worth its own small task.
- The three copies of the `CatalogManager` modal are consolidated into one component by the Books
  task that becomes its fourth consumer (`books-f3sb2`).

## Alternatives considered
- **Keep the untyped slug, add `book_list` (and `game_list`) to the join.** Smallest diff, but it
  entrenches guessing-by-join-order, leaves cross-type collisions unrepresentable, and contradicts
  the README's stated language. Rejected by the builder.
- **Rename the Shared DTO fields only.** Cosmetic; same defects as above.
- **Infer the type inside the projection handler at replay** (probe the four `*_list` tables when
  an untyped `Entry_added` arrives). Introduces a cross-projection read into a handler and depends on
  replay order across BCs; the read-time fallback keeps the handler pure and legacy rows exactly as
  they render today.
- **Rewrite / upcast stored events to carry the type.** Violates the append-only store; the
  fallback costs nothing by comparison.

## References
- `curation-cyxbc` (typed entries, projection, API, existing pages), `books-f3sb2` (book detail
  page + removal cascade).
- ADR-0055 / `GameProjection.fs` — the `try ALTER TABLE … ADD COLUMN with _ -> ()` migration idiom.
- ADR-0044 — projection drift discipline (the new column is projected, rebuildable, drift-checked).

## Amendment 2026-09-16 (curation-cyxbc, implementation)

§3's typed-pair duplicate rule cannot be enforced while §5 keeps `UNIQUE(catalog_slug, movie_slug)` — two typed entries of different `MediaType` sharing a slug in one catalog would be accepted by `decide` and then silently clobbered by the projection's `INSERT OR REPLACE` (the aggregate would hold two entries, the projection one: a replay-breaking divergence, confirmed by exercising it). Until the follow-up in §5 lands, `decide` rejects **any** same-slug add within a catalog, typed or not. §3's pair identity still governs resolution, type-scoped lookups (`getCatalogsFor*`) and the removal cascade (`getEntriesByMediaSlug`) — only the duplicate/uniqueness *key* is narrowed back to slug-only for now. Ruled by a `tactical-modeler` consult during implementation; see the follow-up task `curation-w9fkq` (backfill `media_type`, widen the UNIQUE, relax `Add_entry` back to strict pair identity).
