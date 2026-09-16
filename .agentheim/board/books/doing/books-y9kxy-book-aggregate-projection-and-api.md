---
id: books-y9kxy
title: Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared
status: doing
type: feature
context: books
created: 2026-09-16
completed:
depends_on: []
blocks: [integration-c8d4x, integration-dhctm, integration-wmqn3, books-f33e2, books-g7g1j, intelligence-dnv2y]
tags: [books, aggregate, projection, event-sourcing, metadata-cache, api]
related_adrs: [0076, 0077, 0043, 0045, 0044, 0050]
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
  `Book_external_id_linked`, `Book_format_set`, `Book_status_changed of status: BookStatus *
  effectiveOn: string option`, `Reading_progress_observed`,
  `Reading_progress_observation_removed of observedOn * source`, `Book_personal_rating_set`,
  `Book_recommended_by`, `Book_recommendation_removed`.
- `BookCommand`: `Add_book_to_library`, `Remove_book_from_library`, `Replace_cover`,
  `Link_external_id`, `Set_format`, `Change_status of status: BookStatus * effectiveOn: string
  option`, `Observe_reading_progress`, `Remove_reading_progress_observation`, `Set_personal_rating`,
  `Recommend_by`, `Remove_recommendation`.
  **`effectiveOn` (ADR-0077, amends ADR-0076 §5 — already written, pre-loaded for you; do not draft it again):**
  the `yyyy-MM-dd` day a status became true according to a source that knows it (Goodreads'
  `user_read_at`, an observation's own `ObservedOn`); `None` means "the day this event was appended".
  It rides the **event**, not just the command — a command-only date would not survive a projection
  rebuild. Accepted for every status but only read by the projection for `Finished`; there is no
  `abandoned_at`/`backlogged_at` column and this task must not add one.
- Aggregate state (the `Active` case) carries `FinishedOn: string option` (the last `Finished`
  event's `effectiveOn`, cleared whenever status leaves `Finished`) and
  `Observations: Map<string * ProgressSource, int>` keyed `(observedOn, source) -> percent` — needed
  for the per-source no-op/promotion rules below and for exact `Reading_progress_observation_removed`
  handling.
- `evolve` / `reconstitute` / `decide` with the ADR-0076/ADR-0077 rules: commands other than add
  refused on an unadded or removed book; a percent outside 0–100 is **refused** (never clamped —
  clamping to 100 would silently auto-Finish a book on a bad input); **an observation whose percent
  equals the latest percent *previously observed from that same source* (default 0 when the source
  has no prior observation) emits nothing** — the comparison is per-source, not against the book's
  global current percent, so two sources sitting at different percents (e.g. Audible 42 %, Goodreads
  40 %) don't ping-pong an event on every sync; an observation whose percent is *higher* than that
  same per-source baseline, on a book that is `Backlog` or `Abandoned`, emits the observation +
  `Book_status_changed (InFocus, Some data.ObservedOn)`; percent = 100 or `Finished = true` emits the
  observation + `Book_status_changed (Finished, Some data.ObservedOn)` unless the book is already
  `Finished` (a same-day duplicate is still caught by the same-percent-per-source no-op above); a
  lower observation is recorded (subject to the same per-source no-op test) but never changes status;
  **removing an observation never reverts status** — a book finished by an observation that is later
  removed stays `Finished`; `Change_status` is the only way back. Re-link of a different value for an
  already-linked external-id kind → refused, same value → no-op; an `Add_book_to_library` whose
  `ExternalIds` carries two values of the same kind is refused (mirrors `Link_external_id`'s
  per-kind-uniqueness invariant). `Change_status (status, effectiveOn)` is a no-op only when the
  status is unchanged **and** the effective date would not change — i.e. `status = current &&
  (status <> Finished || effectiveOn = None || effectiveOn = currentFinishedOn)`; a
  `Change_status (Finished, Some d)` on an already-`Finished` book with a *different* date is
  therefore a legitimate re-dating event (this is how a corrected Goodreads `user_read_at` or a
  closed re-read gets recorded — see `integration-wmqn3`). `decide` validates only the date's format;
  future-date rejection is an edge concern (`Api.fs`, the adapters), same as for `ObservedOn`.
- `streamId slug = "Book-" + slug`; `Serialization` module (Thoth encode/decode for every event,
  `handledEventTypes`, `toEventData`, `fromStoredEvent`) following `Games.Serialization` exactly.
- `Slug.bookSlug` — `Shared.fs`'s `Slug` module already has `movieSlug`/`gameSlug (name: string)
  (year: int) = sprintf "%s-%d" (slugify name) year`-shaped functions (~lines 5–27); add `bookSlug`
  alongside `gameSlug` in the identical shape. Don't reinvent normalization.

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
- `finished_at` = `effectiveOn |> Option.defaultValue (event's local-date timestamp)` on a
  `Book_status_changed (Finished, effectiveOn)` event — a **date string** (`yyyy-MM-dd`), not a
  timestamp (unlike `game_list.retired_at`), because `integration-wmqn3` needs to backdate it to a
  Goodreads read date; `getRecentlyFinished`/the dashboard linger compare dates, not timestamps.
  Cleared (`NULL`) when status leaves Finished. `added_at` = the add event's timestamp.
- UNIQUE indexes on `book_detail(audible_asin)` and `book_detail(goodreads_book_id)` (partial, WHERE
  NOT NULL) as duplicate-add backstops, like `movie_detail(tmdb_id)`.
- Queries: `getAll`, `getBySlug` (joins `book_metadata_cache`), `findByExternalId`, `findByTitle`
  (case-insensitive), `getProgressHistory slug`, `getCurrentlyReading`, `getRecentlyFinished days`,
  `getRecentlyAdded n`, `getDailyReadingActivity` (per `observed_on` count of `book_progress` rows —
  the Journal/heatmap source).

**`src/Server/MetadataCache.fs`** — `book_metadata_cache (book_slug PK, description, page_count,
runtime_minutes, narrators JSON, series_name, series_position, publisher, published_date, average_rating,
language, source, fetched_at)` + `upsertBookMetadata` / `tryGetBookMetadata`. `MetadataCache.initialize`
is one function with all cache tables' DDL in a single `Db.newCommand` string (`game_metadata_cache`/
`movie_metadata_cache`/`series_metadata_cache` today) — `book_metadata_cache`'s `CREATE TABLE` goes
inside that same string, not a new function. Registered in `Administration.tableRegistry` as
`Cache "Open Library / Audible / Audnexus adapters"`.

**Registries** (`src/Server/Administration.fs`, `EventFormatting.fs`, `Composition.fs`):
`boundedContextPrefixes` (`Books` / `Book-`, ~line 24–31), `eventCodecs` (~line 62–69), `tableRegistry`
(`book_list`, `book_detail`, `book_progress` → `Projected "BookProjection"`), `imageRefColumns`
(`book_list/cover_ref`, `book_detail/cover_ref`), `EventFormatting.formatBookEvent` + a one-line
`elif` in the existing `if/elif` `Book-` prefix dispatch (`formatEvent`, ~line 438–442),
`BookProjection.handler` appended to `Composition.fs`'s `projectionHandlers` list (~line 261–269 —
the only list of its kind; mirror `GameProjection.handler`'s entry exactly).
**A fourth, easily-missed registration site:** `tests/Server.Tests/TableClassificationTests.fs`'s
private `bootstrapEverything` helper (~line 18–32) explicitly calls `XProjection.handler.Init conn`
for each of the six existing handlers to build the in-memory schema that test's sqlite_master scan
checks against `tableRegistry`. It needs a `BookProjection.handler.Init conn` call added too —
without it, `book_list`/`book_detail`/`book_progress` never exist in that test's schema and the
"TableClassificationTests passes" acceptance criterion below fails for the wrong reason (table
absent, not un-classified).

**Shared + API** (`src/Shared/Shared.fs`, `src/Server/Api.fs`):
- `MediaType` gains `Book`; every exhaustive match that now warns (client `State.fs` ×3,
  `SearchModal.fs`, `Tmdb.fs`) is completed — client navigation maps `Book` → `("books", slug)`;
  `SearchModal.filterLibrary` includes books once `getBooks` exists (the Library tab shows them;
  a dedicated Books search tab is `books-g7g1j`).
- DTOs: `BookListItem`, `BookDetail` (identity card + external ids + status + progress + rating +
  `RecommendedBy: FriendRef list` + cache fields as options + `ProgressHistory: ReadingProgressDto list`
  + `ContentBlocks`), `ReadingProgressDto`, `AddBookRequest = { Title; Authors; Year; CoverUrl: string
  option; Subjects; Format; ExternalIds; SkipDuplicateCheck }`, `AddBookOutcome = Book_added of slug |
  Duplicate_found of existingSlug * existingTitle` (the shape of `AddGameOutcome` — note
  `AddGameOutcome`'s own success case is actually named `Created of slug`, not `Game_added`; copy the
  *shape*, keep this task's own case names as specified), `SetReadingProgressRequest
  = { Slug; Percent: int option; Page: int option; TotalPages: int option; ObservedOn: string option }`.
- `IMediathecaApi`: `getBooks`, `getBook: slug`, `addBook: AddBookRequest` (generic add used by the
  adapters and by a manual add; downloads `CoverUrl` to `posters/book-{slug}.jpg` via
  `generateUniqueSlug` (`Api.fs:108`, already the shared BC-agnostic helper Movies/Series/Games/
  Catalogs all reuse) called with `Slug.bookSlug`/`Books.streamId`, mirroring `Api.fs:1392`'s
  `Slug.gameSlug`/`Games.streamId` call exactly), `removeBook`,
  `setBookStatus`, `setBookFormat`, `setBookPersonalRating`, `setBookProgress` (Manual source,
  today's date unless given; percent from page/total when percent absent), `removeBookProgressObservation`,
  `linkBookExternalId`, `recommendBookBy`, `removeBookRecommendation`, `getCatalogsForBook` is **not**
  in scope (curation-cyxbc). Content blocks: **`ContentBlocks` has no owner-kind key to reuse** —
  `ContentBlocks.streamId` is bare-slug (`"ContentBlocks-" + slug`, no `"movie:"`/`"book:"` prefix
  ever), and Series/Games already share the same bare-slug stream/table (`content_blocks`, column
  literally `movie_slug`) through their own parallel `IMediathecaApi` method families
  (`getSeriesContentBlocks`/`addSeriesContentBlock`/…, `getGameContentBlocks`/`addGameContentBlock`/…
  — there is no `owner_kind` DU anywhere to extend). Add a **fourth parallel family** —
  `getBookContentBlocks`/`addBookContentBlock`/`updateBookContentBlock`/`removeBookContentBlock` —
  each calling `ContentBlocks.streamId slug` (bare slug, unchanged) and
  `ContentBlockProjection.getForMovieDetail conn slug` (the same shared, oddly-named function every
  other BC already calls), mirroring the Game/Series precedent exactly — do not invent an owner-kind
  concept. (Latent, pre-existing, out-of-scope-to-fix note: because the stream/table is bare-slug-keyed
  across every media type, a movie and a book that ever produced an identical slug would silently
  share one content-blocks stream — inherited, not caused, by this task.)
- Duplicate detection in `addBook`: any external id already linked → `Duplicate_found`; else
  case-insensitive title + first author → `Duplicate_found`; `SkipDuplicateCheck` bypasses both.

## Acceptance criteria

- [ ] `Books.decide` unit tests (Expecto, `tests/Server.Tests/BooksTests.fs`) cover: add; observation
      with same percent (from the same source) emits nothing; higher observation from Backlog emits
      observation + `Book_status_changed (InFocus, Some observedOn)`; 100 % emits observation +
      `Book_status_changed (Finished, Some observedOn)`; `Finished = true` at 60 % emits `Finished`;
      lower observation on Finished emits observation only and status stays Finished; removing the
      observation that finished a book leaves it Finished (no auto-revert); re-link different ASIN
      refused, same ASIN no-op; commands on removed book refused; a percent of 101 is refused (not
      clamped); two sources at different percents on the same day (Audible 42, Goodreads 40) each
      record independently without ping-ponging a status event.
- [ ] `Change_status` backdating tests: `Change_status (Finished, Some "2026-09-02")` on a Backlog
      book sets `finished_at = 2026-09-02`; repeating the identical command appends zero events;
      repeating it with a different date appends exactly one event and updates the date;
      `Change_status (Finished, None)` sets `finished_at` to the event's own local date; a book
      Finished by an observation today (`effectiveOn = Some today`) and later re-dated by a manual
      `Change_status (Finished, Some pastDate)` converges to the past date after exactly one more
      event.
- [ ] `BookProjection` tests (`BookProjectionTests.fs`, `TestDb.withTempDbFactory`): two observations
      on one day from one source leave one `book_progress` row with the later percent; observations
      from two sources on one day leave two rows and `book_list.progress_percent` reflects the latest
      `observed_on` (Manual wins a same-day tie); `finished_at` is a date string, set on Finished
      (from `effectiveOn` when given) and cleared on `Change_status (Backlog, _)`;
      `getDailyReadingActivity` counts distinct days.
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

- Model of record: ADR-0076, amended by **ADR-0077** (`.agentheim/knowledge/decisions/0077-book-status-change-carries-effective-on-date.md`, already on disk and pre-loaded — read it; do NOT draft a second ADR-0077 in your ADRS block). Cache/event doctrine: ADR-0043, ADR-0045 (a `ProjectionHandler` must never read `MetadataCache`). Table classification: ADR-0044. Games' play-session precedent: ADR-0050.
- Cache/event doctrine: ADR-0043, ADR-0045 (a `ProjectionHandler` must never read `MetadataCache`).
  Table classification: ADR-0044. Games' play-session precedent: ADR-0050.
- Mirror, don't invent: `Games.fs` (DU + decide + Serialization), `GameProjection.fs` (Init/Drop/Handle,
  `dropDeprecatedColumns` pattern not needed), `Api.fs`'s `addGame` (duplicate → outcome → slug →
  image download → command), `MovieProjection`'s UNIQUE-index backstop.
- Cover download: reuse `Tmdb.downloadImage`'s shape (a plain `httpClient.GetByteArrayAsync` into
  `ImageStore.saveImage` at `posters/book-{slug}.jpg`); no throttle here — the adapters throttle their
  own hosts (integration-c8d4x).
- `Composition.fs`: register the projection handler (`projectionHandlers` list, ~line 261–269); no
  scheduled job in this task.
- Content blocks: see the corrected "Shared + API" section above — there is no owner-kind key to
  extend; add a fourth parallel `getBookContentBlocks`/… method family mirroring Series/Games.
- Derived-percent rounding is an **adapter** concern, not this aggregate's: `decide` refuses any
  percent outside 0–100 but does not otherwise second-guess a source's number. `integration-jjvg2`
  and `integration-y2ak4` must floor (never round-half-up) a derived percent so e.g. Audible's 99.6 %
  or "page 299 of 300" doesn't compute to 100 and auto-finish a book the source hasn't actually
  marked finished — flag this to whichever worker or specialist builds those tasks if it isn't already
  in their file.
- The Journal/Intelligence consumers come later (`intelligence-dnv2y`, `journal-k52j1`); this task
  only exposes `getDailyReadingActivity` and `getRecentlyFinished`.
- A removed book cannot be re-added under the same slug (`Removed, _ -> Error` in `decide`, matching
  `Games.fs`) — a later Audible/Goodreads sync that still sees the title will create it again under a
  *new* slug via `generateUniqueSlug`. This is existing Steam-import behavior, not a books-specific
  gap; `integration-wmqn3`/`integration-jjvg2` should carry this as an explicit open question rather
  than a surprise.
- Research: `knowledge/research/audible-api-surface-and-listening-progress-2026-09-16.md`,
  `knowledge/research/goodreads-reading-progress-and-book-metadata-sources-2026-09-16.md` (for the
  external-id kinds the adapters will link).
