---
id: books-y9kxy
title: Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared
status: todo
type: feature
context: books
created: 2026-09-16
completed:
depends_on: []
blocks: [integration-c8d4x, integration-dhctm, integration-wmqn3, books-f33e2, books-g7g1j, intelligence-dnv2y]
tags: [books, aggregate, projection, event-sourcing, metadata-cache, api]
related_adrs: [0076, 0043, 0045, 0044, 0050]
related_research: [audible-api-surface-and-listening-progress-2026-09-16, goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
prior_art: []
---

## Why

Books is the fourth media type (movies, series, games, **books**) and the vision's "read" verb has
had no home. Audible and Goodreads integrations, a book detail page and a dashboard Books tab all
need one thing first: a Book aggregate with its event stream, a projection, a cache slice and the
Remoting surface — built the way Movies/Games are built, so every later task is a mirror of an
existing shape. ADR-0076 fixes the model; this task materializes it. No UI, no adapters.

## What

Server-side core of the Books BC, mirroring `Games.fs` / `GameProjection.fs` / `MetadataCache.fs`:

**`src/Server/Books.fs`** (after `Games.fs` in `Server.fsproj`)
- `BookFormat = Audiobook | Print | Ebook | Unknown`; `BookStatus = Backlog | InFocus | Finished |
  Abandoned`; `ProgressSource = Audible | Goodreads | Manual`; `ReadingPosition = Page of page: int *
  total: int option | Minutes of minutes: int * total: int option`; `BookExternalId = Isbn13 of string
  | OpenLibraryWork of string | OpenLibraryEdition of string | AudibleAsin of string | GoodreadsBookId
  of string`.
- `BookAddedData = { Title; Authors: string list; Year: int option; CoverRef: string option;
  Subjects: string list; Format: BookFormat; ExternalIds: BookExternalId list }`.
- `ReadingProgressObservedData = { Percent: int; Position: ReadingPosition option; Source;
  ObservedOn: string (yyyy-MM-dd); Finished: bool }`.
- `BookEvent`: `Book_added_to_library`, `Book_removed_from_library`, `Book_cover_replaced`,
  `Book_external_id_linked`, `Book_format_set`, `Book_status_changed`, `Reading_progress_observed`,
  `Reading_progress_observation_removed of observedOn * source`, `Book_personal_rating_set`,
  `Book_recommended_by`, `Book_recommendation_removed`.
- `BookCommand`: `Add_book_to_library`, `Remove_book_from_library`, `Replace_cover`,
  `Link_external_id`, `Set_format`, `Change_status`, `Observe_reading_progress`,
  `Remove_reading_progress_observation`, `Set_personal_rating`, `Recommend_by`, `Remove_recommendation`.
- `evolve` / `reconstitute` / `decide` with the ADR-0076 rules: commands other than add refused on
  an unadded or removed book; percent clamped to 0–100 else refused; **same-percent observation →
  no events**; higher observation on a non-InFocus, non-Finished book → observation + `Book_status_changed
  InFocus`; percent = 100 or `Finished = true` → observation + `Book_status_changed Finished` (unless
  already Finished); lower observation → observation only; re-link of a different value for an
  already-linked id kind → refused, same value → no events; `Change_status` to the current status → no
  events.
- `streamId slug = "Book-" + slug`; `Serialization` module (Thoth encode/decode for every event,
  `handledEventTypes`, `toEventData`, `fromStoredEvent`) following `Games.Serialization` exactly.
- `Slug.bookSlug` in `src/Shared/Shared.fs`'s `Slug` module (title + year, same normalization as
  `movieSlug`).

**`src/Server/BookProjection.fs`** (after `GameProjection.fs`) — `handler: Projection.ProjectionHandler`
with `Init`/`Drop`/`Handle`:
- `book_list (slug PK, title, authors JSON, year, cover_ref, subjects JSON, format, status, progress_percent
  INTEGER NOT NULL DEFAULT 0, progress_source, progress_observed_on, personal_rating, isbn13,
  openlibrary_work_key, openlibrary_edition_key, audible_asin, goodreads_book_id, finished_at, added_at)`
- `book_detail` = `book_list` columns + `recommended_by JSON`
- `book_progress (book_slug, observed_on, source, percent, position_json, PRIMARY KEY (book_slug,
  observed_on, source))` — `Reading_progress_observed` does `INSERT OR REPLACE` (same-day same-source
  replaces); the `_removed` event deletes the row; after either, `book_list.progress_*` is recomputed
  from the latest row by `observed_on` (ties: prefer Manual > Audible > Goodreads).
- `finished_at` = the `Book_status_changed Finished` event's timestamp (date part); cleared when status
  leaves Finished. `added_at` = the add event's timestamp.
- UNIQUE indexes on `book_detail(audible_asin)` and `book_detail(goodreads_book_id)` (partial, WHERE
  NOT NULL) as duplicate-add backstops, like `movie_detail(tmdb_id)`.
- Queries: `getAll`, `getBySlug` (joins `book_metadata_cache`), `findByExternalId`, `findByTitle`
  (case-insensitive), `getProgressHistory slug`, `getCurrentlyReading`, `getRecentlyFinished days`,
  `getRecentlyAdded n`, `getDailyReadingActivity` (per `observed_on` count of `book_progress` rows —
  the Journal/heatmap source).

**`src/Server/MetadataCache.fs`** — `book_metadata_cache (book_slug PK, description, page_count,
runtime_minutes, narrators JSON, series_name, series_position, publisher, published_date, average_rating,
language, source, fetched_at)` + `upsertBookMetadata` / `tryGetBookMetadata`. Registered in
`Administration.tableRegistry` as `Cache "Open Library / Audible / Audnexus adapters"`.

**Registries** (`src/Server/Administration.fs`, `EventFormatting.fs`, `Composition.fs`):
`boundedContextPrefixes` (`Books` / `Book-`), `eventCodecs`, `tableRegistry` (`book_list`,
`book_detail`, `book_progress` → `Projected "BookProjection"`), `imageRefColumns`
(`book_list/cover_ref`, `book_detail/cover_ref`), `EventFormatting.formatBookEvent` + the `Book-`
prefix dispatch, `BookProjection.handler` in `Composition`'s projection list.

**Shared + API** (`src/Shared/Shared.fs`, `src/Server/Api.fs`):
- `MediaType` gains `Book`; every exhaustive match that now warns (client `State.fs` ×3,
  `SearchModal.fs`, `Tmdb.fs`) is completed — client navigation maps `Book` → `("books", slug)`;
  `SearchModal.filterLibrary` includes books once `getBooks` exists (the Library tab shows them;
  a dedicated Books search tab is `books-g7g1j`).
- DTOs: `BookListItem`, `BookDetail` (identity card + external ids + status + progress + rating +
  `RecommendedBy: FriendRef list` + cache fields as options + `ProgressHistory: ReadingProgressDto list`
  + `ContentBlocks`), `ReadingProgressDto`, `AddBookRequest = { Title; Authors; Year; CoverUrl: string
  option; Subjects; Format; ExternalIds; SkipDuplicateCheck }`, `AddBookOutcome = Book_added of slug |
  Duplicate_found of existingSlug * existingTitle` (the `AddGameOutcome` shape), `SetReadingProgressRequest
  = { Slug; Percent: int option; Page: int option; TotalPages: int option; ObservedOn: string option }`.
- `IMediathecaApi`: `getBooks`, `getBook: slug`, `addBook: AddBookRequest` (generic add used by the
  adapters and by a manual add; downloads `CoverUrl` to `posters/book-{slug}.jpg`), `removeBook`,
  `setBookStatus`, `setBookFormat`, `setBookPersonalRating`, `setBookProgress` (Manual source,
  today's date unless given; percent from page/total when percent absent), `removeBookProgressObservation`,
  `linkBookExternalId`, `recommendBookBy`, `removeBookRecommendation`, `getCatalogsForBook` is **not**
  in scope (curation-cyxbc). Content blocks: reuse the existing `ContentBlocks` attachment by a
  `"book:{slug}"` owner key exactly as movies do, so the detail page can show them.
- Duplicate detection in `addBook`: any external id already linked → `Duplicate_found`; else
  case-insensitive title + first author → `Duplicate_found`; `SkipDuplicateCheck` bypasses both.

## Acceptance criteria

- [ ] `Books.decide` unit tests (Expecto, `tests/Server.Tests/BooksTests.fs`) cover: add; observation
      with same percent emits nothing; higher observation from Backlog emits observation +
      `Book_status_changed InFocus`; 100 % emits observation + `Finished`; `Finished = true` at 60 %
      emits `Finished`; lower observation on Finished emits observation only and status stays
      Finished; re-link different ASIN refused, same ASIN no-op; commands on removed book refused.
- [ ] `BookProjection` tests (`BookProjectionTests.fs`, `TestDb.withTempDbFactory`): two observations
      on one day from one source leave one `book_progress` row with the later percent; observations
      from two sources on one day leave two rows and `book_list.progress_percent` reflects the latest
      `observed_on` (Manual wins a same-day tie); `finished_at` set on Finished and cleared on
      `Change_status Backlog`; `getDailyReadingActivity` counts distinct days.
- [ ] Round-trip serialization test for every `BookEvent` case; `handledEventTypes` lists every case
      (the `EventStore` unknown-event report stays empty after appending one of each).
- [ ] `TableClassificationTests` passes with `book_list`, `book_detail`, `book_progress` classified
      Projected and `book_metadata_cache` classified Cache; `ProjectionRebuildTests`/`ProjectionDriftTests`
      style rebuild of `BookProjection` from a stream with add + 3 observations + finish yields identical
      rows (drift zero).
- [ ] `addBook` twice with the same ASIN returns `Duplicate_found`; with `SkipDuplicateCheck = true`
      creates a second slug (`generateUniqueSlug`).
- [ ] `setBookProgress { Page = Some 120; TotalPages = Some 300 }` yields percent 40 with
      `Position = Page (120, Some 300)`, source Manual.
- [ ] `MediaType.Book` compiles with zero incomplete-match warnings in `npm run build` and `dotnet build`.
- [ ] `npm test` and `npm run test:client` green; `npm run build` succeeds.

## Notes

- Model of record: ADR-0076. Cache/event doctrine: ADR-0043, ADR-0045 (a `ProjectionHandler` must
  never read `MetadataCache`). Table classification: ADR-0044. Games' play-session precedent: ADR-0050.
- Mirror, don't invent: `Games.fs` (DU + decide + Serialization), `GameProjection.fs` (Init/Drop/Handle,
  `dropDeprecatedColumns` pattern not needed), `Api.fs`'s `addGame` (duplicate → outcome → slug →
  image download → command), `MovieProjection`'s UNIQUE-index backstop.
- Cover download: reuse `Tmdb.downloadImage`'s shape (a plain `httpClient.GetByteArrayAsync` into
  `ImageStore.saveImage` at `posters/book-{slug}.jpg`); no throttle here — the adapters throttle their
  own hosts (integration-c8d4x).
- `Composition.fs`: register the projection handler; no scheduled job in this task.
- Content blocks: check how `ContentBlocks` keys movies (`getContentBlocks "movie" slug` or similar)
  and add the `book` owner kind the same way; if the owner kind is a DU, extend it.
- The Journal/Intelligence consumers come later (`intelligence-dnv2y`, `journal-k52j1`); this task
  only exposes `getDailyReadingActivity` and `getRecentlyFinished`.
- Research: `knowledge/research/audible-api-surface-and-listening-progress-2026-09-16.md`,
  `knowledge/research/goodreads-reading-progress-and-book-metadata-sources-2026-09-16.md` (for the
  external-id kinds the adapters will link).
