---
id: curation-cyxbc
title: Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names
status: backlog
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [books-y9kxy, books-f33e2, design-system-001-formalize-styleguide]
blocks: [books-f3sb2]
tags: [books, games, catalogs, curation, typed-entries]
related_adrs: [0079, 0055, 0044]
related_research: []
prior_art: []
---

## Why

Catalogs are "I made a list" across media types; a reading list is the most natural list of all.
Refining Books into catalogs (2026-09-16) exposed that the entry key is an **untyped slug**
resolved by join order, and that this already fails: a game added from the game detail page renders
in the catalog as its raw slug, year 0, no cover, linking to `/movies/{slug}`, because
`CatalogProjection.getEntries` only ever joins `movie_list` and `series_list`. Slugs are
`slugify(title)-year` for every type, so a novel and its same-year film collide on the key, and the
slug-only removal cascade would delete the other type's entry.

The builder's ruling (ADR-0079): the Curation language says an entry references a media item by
`(MediaType, slug)` — make the code say it too, rather than add a fourth guessed join.

## What

**Event + aggregate (`src/Server/Catalogs.fs`)**
- `EntryAddedData` and `EntryState` gain `MediaType: Mediatheca.Shared.MediaType option`.
  Encoder writes `"mediaType": "Movie" | "Series" | "Game" | "Book"` (DU case names) and **omits
  the key when `None`**; decoder reads it with `Optional.Field`. JSON key `movieSlug` unchanged.
- `Add_entry` duplicate rule: both typed → duplicate iff `(MediaType, slug)` match; **either side
  `None` → slug equality alone rejects**, with the message
  `Entry '{slug}' is already in this catalog (recorded before entries were typed)`. Rationale in
  ADR-0079 §3 (the projection's `UNIQUE(catalog_slug, movie_slug)` stays; see below).
- `handledEventTypes` unchanged (no new event type).

**Projection (`src/Server/CatalogProjection.fs`)**
- `catalog_entries.media_type TEXT NULL`, added in `createTables` via the idempotent
  `try … ALTER TABLE catalog_entries ADD COLUMN media_type TEXT … with _ -> ()` guard
  (`GameProjection.fs` precedent, ADR-0055). **Do not widen the UNIQUE constraint** — NULLs are
  distinct in a SQLite UNIQUE index, so legacy rows would lose duplicate protection (ADR-0079 §5).
- `Entry_added` handler writes `media_type` (NULL for legacy replays).
- `getEntries`: when `media_type` is set, join exactly that type's list table
  (`movie_list` name/year/poster_ref, `series_list` + season/episode caches as today,
  `game_list` name/year/cover_ref, `book_list` title/year(nullable → 0)/cover_ref) and emit the
  DTO's `MediaType` from the column. When `media_type IS NULL`, keep today's COALESCE join-order
  inference verbatim (movies, then series, default movies) so legacy rows render unchanged.
  `enhanceDisplayName` (season/episode suffix) applies only to `Series` rows.
- `getCatalogsForMovie` becomes `getCatalogsForMedia (mediaType) (slug)` filtering
  `media_type = @type OR media_type IS NULL`; `getCatalogsForSeriesWithChildren` gets the same
  filter on its `LIKE @prefix` branch.
- `getEntriesByMediaSlug` (the removal cascade) takes a `MediaType` and applies the same filter —
  removing a movie must never delete a same-slugged book's or game's entry.

**Shared (`src/Shared/Shared.fs`)**
- `CatalogEntryDto`: `MediaSlug`, `Title`, `Year`, `PosterRef`, `Note`, `Position`, `MediaType`
  (no `RoutePrefix`). `CatalogRef`: `Slug`, `Name`, `EntryId`, `MediaSlug`, `MediaType`.
  `AddCatalogEntryRequest`: `MediaSlug`, `MediaType`, `Note`.
- `MediaType.routePrefix : MediaType -> string` (`movies` / `series` / `games` / `books`) next to
  the `MediaType` DU — the one place the route string is derived.
- `IMediathecaApi.getCatalogsForBook: string -> Async<CatalogRef list>`, alongside the existing
  `…Movie` / `…Series` / `…Game` methods (each now a typed call into `getCatalogsForMedia`;
  `getCatalogsForGame` stops being an alias of the movie lookup).

**Server API (`src/Server/Api.fs`)**
- `addCatalogEntry` passes the request's `MediaType` into `EntryAddedData` as `Some`.
- Movie / series / game removal cascades call the typed `getEntriesByMediaSlug`.
  (Book removal's cascade is `books-f3sb2`'s.)

**Client (existing pages only — the book detail page is `books-f3sb2`)**
- The three `Add_to_catalog` / `Create_catalog_and_add` handlers (MovieDetail, SeriesDetail,
  GameDetail `State.fs`) send `MediaType` (`Movie` / `Series` — season and episode children are
  `Series` with the `slug:s01e05` suffix unchanged / `Game`). **Not a mechanical rename.**
- `Pages/CatalogDetail/Views.fs` and `Components/EntryList` consumers: field renames, and
  `RoutePrefix = MediaType.routePrefix e.MediaType` where `EntryList.EntryItem` /
  `PosterCard.viewForRoute` still take the string. The empty-state copy "Add movies or series to
  build your catalog." becomes media-neutral.
- `EventFormatting.formatCatalogEvent`'s `Entry_added` label may append the media type when
  present; `crossLinkFields` is **out of scope** (see Notes).

**Tests**
- `tests/Server.Tests/CatalogsTests.fs`: round-trip with and without `mediaType`; legacy JSON
  `{entryId, movieSlug, note}` decodes to `MediaType = None`; the duplicate rule's three cases
  (typed same pair rejects, typed different type same slug accepts, legacy vs typed same slug
  rejects with the "before entries were typed" message).
- New projection query tests (in-memory SQLite, the `ProjectionRebuildTests` fixture style): one
  entry per media type resolves title / year / cover / `MediaType`; a legacy NULL row falls back to
  the join-order inference; a movie and a book sharing a slug in one catalog resolve to their own
  rows; `getEntriesByMediaSlug Movie` does not return the book's entry; the typed
  `getCatalogsForGame` / `getCatalogsForBook` return the catalog.

**README delta (reported, conductor applies):** the "Catalog entry" bullet's second sentence
becomes: *References a media item by `(MediaType, slug)` — that pair is the entry's identity within
a catalog, so a media item appears at most once per catalog; entries recorded before entries were
typed carry no MediaType and are treated as matching any type with the same slug (ADR-0079).*
Purpose line: "ordered lists of movies / series / games / books". Relationships: conformist to
Movies, Series, Games, **Books**.

## Acceptance criteria

- [ ] `EntryAddedData` / `EntryState` carry `MediaType: MediaType option`; the encoder omits
      `mediaType` when `None`; the stored key `movieSlug` and column `movie_slug` are unchanged.
- [ ] Expecto: legacy JSON `{"entryId","movieSlug","note"}` deserializes to an `Entry_added` with
      `MediaType = None`; a typed event round-trips with its type.
- [ ] Expecto: `Add_entry` rejects a same-slug add when the existing entry is legacy-untyped (any
      type) or when both are typed with the same `(MediaType, slug)`; accepts a same-slug add of a
      different type when both are typed; every existing `CatalogsTests` case stays green.
- [ ] `catalog_entries` gains `media_type TEXT NULL` through the idempotent `ALTER TABLE` guard;
      `createTables` is safe to run repeatedly against an existing database and on a fresh one.
      `UNIQUE(catalog_slug, movie_slug)` is unchanged.
- [ ] Projection test: a game entry resolves its real name, year and cover and
      `MediaType = Game`; a book entry resolves title, year (0 when the book's year is null), cover
      and `MediaType = Book`; a movie and a book sharing a slug in the same catalog each resolve to
      their own row.
- [ ] Projection test: a `media_type IS NULL` row for a movie slug resolves exactly as before
      (movie name / year / poster, `MediaType = Movie`); a NULL row for a series season slug still
      resolves the season poster and " - Season N" display name.
- [ ] `getEntriesByMediaSlug` is type-filtered: removing a movie whose slug equals a book's slug
      leaves the book's catalog entry in place (Expecto through the API's `removeMovie`).
- [ ] `getCatalogsForBook` exists; `getCatalogsForMovie` / `…Series` / `…Game` / `…Book` all pass
      their type and `getCatalogsForGame` is no longer the movie lookup; the series-children lookup
      keeps resolving season and episode entries.
- [ ] Shared: `CatalogEntryDto` has `MediaSlug` / `Title` / `Year` / `PosterRef` / `MediaType` and
      no `RoutePrefix`; `CatalogRef` and `AddCatalogEntryRequest` carry `MediaSlug` + `MediaType`;
      `MediaType.routePrefix` exists and is the only route-string derivation on the client.
- [ ] The three existing add-to-catalog flows (MovieDetail, SeriesDetail incl. season/episode
      children, GameDetail) send the correct `MediaType`; `npm run build`, `npm test` and
      `npm run test:client` are green.
- [ ] In the running app: a game added to a catalog shows its title, year and cover in the catalog
      detail list and its link opens `/games/{slug}`; a season entry still opens `/series/{slug}`.

## Notes

- Refined 2026-09-16 from the original "Books in catalogs" capture. Builder chose **typed entries**
  over keeping the untyped slug or renaming DTO fields only ("I am all about specific ubiquitous
  language in this bounded context"). The tactical pass (orchestrator → tactical-modeler) settled
  the legacy handling, the conservative duplicate rule, the UNIQUE constraint and the cascade
  filter; all recorded in ADR-0079.
- **Split:** the book detail page's catalog pills + picker (extracting the thrice-copied
  `CatalogManager` into `Components/`) and `removeBook`'s catalog cascade are `books-f3sb2`,
  which `depends_on` this task.
- **Follow-up, not here:** widening the projection's UNIQUE to `(catalog_slug, media_type,
  movie_slug)` and relaxing the duplicate rule to strict pair identity once legacy rows have a
  backfilled `media_type` (ADR-0079 §5). Also pre-existing and separate:
  `EventFormatting.crossLinkFields` maps `movieSlug` → `Movie-` for every event, so non-movie
  catalog entries already mislink in the event browser.
- `media_type` values are the DU case names (`Movie` / `Series` / `Game` / `Book`), matching how
  other projections store DU-valued columns (e.g. `status`).
- Frontend gate: `design-system-001-formalize-styleguide` is done; the client edits here are
  renames plus copy, no new surface.
