---
id: integration-c8d4x
title: Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled
status: done
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [books-y9kxy]
blocks: [books-g7g1j, integration-wmqn3, integration-dhctm]
tags: [books, open-library, adapter, search, metadata-cache, throttle]
related_adrs: [0075, 0066, 0043, 0045, 0076]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
prior_art: [integration-w7ktb]
---

## Why

Goodreads has no search endpoint and never will again (ADR-0075); Audible's catalog covers only
audiobooks. Books need one key-less, ISBN-addressable metadata source for the search modal and for
enriching books that arrive from a Goodreads shelf. Open Library is that source. This is the
`Tmdb.fs`-equivalent for books: the anticorruption layer that keeps `Books.fs` free of Open
Library's JSON.

## What

**`src/Server/OpenLibrary.fs`** (in the adapter block of `Server.fsproj`, before `ImageStore.fs`):
- `OpenLibraryConfig = { UserAgent: string }` — `"Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)"`,
  sent on every request (Open Library's identified-client policy).
- `throttleCall` — one process-wide gate (`SemaphoreSlim(1,1)` + minimum interval, default 1000 ms,
  interval injectable for tests) fronting every `openlibrary.org` call, and a second independent gate
  for `covers.openlibrary.org` (default 3000 ms — the covers host allows 100 requests per 5 minutes by
  ISBN/OLID key). The `Steam.throttleStorefrontCall` shape (ADR-0066), copied, not shared.
- `searchBooks httpClient config (query: string) : Async<OpenLibrarySearchResult list>` —
  `GET https://openlibrary.org/search.json?q={query}&fields=key,title,author_name,first_publish_year,
  cover_i,isbn,edition_key,subject,number_of_pages_median&limit=20`; result = `{ WorkKey: string
  ("/works/OL…W"); Title; Authors: string list; Year: int option; CoverId: int option; Isbn13: string
  option (first 13-digit isbn); EditionKey: string option; Subjects: string list (first 8);
  PageCount: int option }`.
- `getWork httpClient config workKey` → `{ Description: string option; Subjects }` from
  `GET https://openlibrary.org{workKey}.json` (`description` is a string or `{type,value}` object —
  decode both).
- `getEditionByIsbn httpClient config isbn` → `{ EditionKey; WorkKey option; Title; Authors (resolved
  via `/authors/{key}.json` at most 3); PublishDate; Publishers; PageCount; CoverId; GoodreadsIds:
  string list (from `identifiers.goodreads`) }` from `GET https://openlibrary.org/isbn/{isbn}.json`;
  `None` on 404.
- `coverUrl coverId size` = `https://covers.openlibrary.org/b/id/{coverId}-{S|M|L}.jpg`;
  `downloadCover httpClient config coverId slug` → saves `posters/book-{slug}.jpg` via
  `ImageStore.saveImage`, returns the ref; a 404 or a tiny placeholder response (< 1 KB) yields `None`.
- In-process 1 h search cache keyed by query (the `Tmdb.SearchCache` shape).

**Shared + API**
- `OpenLibrarySearchResult` DTO in `Shared.fs` (fields above, `CoverUrl: string option` pre-built at
  size M for the grid).
- `IMediathecaApi.searchOpenLibraryBooks: string -> Async<OpenLibrarySearchResult list>`.
- `IMediathecaApi.addBookFromOpenLibrary: AddBookFromOpenLibraryRequest -> Async<Result<AddBookOutcome,
  string>>` with `{ WorkKey; EditionKey: string option; SkipDuplicateCheck }`: fetches the work (and
  the edition when given) → `AddBookRequest` (Title, Authors, Year, CoverUrl at size L, Subjects,
  `Format = Unknown`, ExternalIds = `OpenLibraryWork` + `OpenLibraryEdition`? + `Isbn13`? +
  `GoodreadsBookId`? when the edition carries one) → `addBook` (books-y9kxy) → on `Book_added`,
  `MetadataCache.upsertBookMetadata` with description, page_count, publisher, published_date,
  `source = "openlibrary"`.
- `IMediathecaApi.refreshBookFromOpenLibrary: slug -> Async<Result<unit,string>>` — re-fetches the
  work/edition for a book that has an Open Library key or ISBN and rewrites the cache slice only
  (never the identity card — ADR-0043's identity-card clause).
- `Composition.fs`: `getOpenLibraryConfig` (constant; no setting needed).

## Acceptance criteria

- [ ] `OpenLibraryTests.fs`: decoders handle a captured `search.json` fixture (string `description`
      and `{type,value}` object both decode; missing `cover_i` → `None`; `isbn` list picks the first
      13-digit value).
- [ ] Throttle test (the `SteamStorefrontThrottleTests` shape, interval injected to ~50 ms): three
      concurrent `searchBooks` calls complete in ≥ 2 × interval; covers gate and API gate are
      independent (a covers call does not wait on an API call).
- [ ] Every outbound request carries the configured `User-Agent` (assert on a recording
      `HttpMessageHandler`).
- [ ] `addBookFromOpenLibrary` on a stubbed handler creates the book with `OpenLibraryWork` + `Isbn13`
      linked, downloads the cover to `posters/book-{slug}.jpg`, and writes `book_metadata_cache` with
      `page_count` and `description`; calling it again returns `Duplicate_found`.
- [ ] `refreshBookFromOpenLibrary` changes `book_metadata_cache.description` and leaves
      `book_list.title/cover_ref` untouched (asserted).
- [ ] A 404 from `/isbn/{isbn}.json` yields `Ok None`, not an exception.
- [ ] `npm test` green; `npm run build` succeeds (Shared changes compile for Fable).

## Notes

- ADR-0075 (Open Library as the metadata source, User-Agent + rate policy), ADR-0066 (adapter-owned
  throttle), ADR-0043/0045 (cache slice vs identity card), ADR-0076 (external-id kinds).
- Mirror `Tmdb.fs` for structure (config record, `fetchJson`, `Decoder<_>`s, `SearchCache`,
  `downloadImage`) and `Steam.fs` lines ~240–288 for the throttle.
- Open Library docs: `openlibrary.org/dev/docs/api/search`, `/dev/docs/api/books`,
  `/dev/docs/api/covers`; rate policy in internetarchive/openlibrary issue #8534 (1 req/s anonymous,
  3 req/s identified). Research report §3 has the details.
- Author names in `search.json` come pre-resolved (`author_name`); in `/isbn/{isbn}.json` they are
  `{key}` references — resolve at most three, best-effort.
- No settings card: Open Library needs no key. The Books Settings surface is integration-dhctm
  (Audible) and integration-wmqn3 (Goodreads).
- **Scheduling note:** `integration-dhctm` now `depends_on` this task (added during refinement,
  2026-09-16) — both tasks otherwise add new `IMediathecaApi` members to the same tail of the
  interface (right after this task's new "Books" section) and matching fields to `Api.fs`'s `create`
  record plus a new `getXConfig` function in `Composition.fs`'s adapter-config cluster (~line
  188–237); dispatching them in the same parallel batch would produce a near-guaranteed manual-merge
  conflict at squash time. This task should merge to `main` before `integration-dhctm` starts — no
  action needed in this file beyond landing your own additions cleanly at the tail so the next
  worker's diff applies without rebasing surprises.

## Verifier note (iteration 1)

VERDICT: FAIL (iteration 1 of 3). Runner green (build ok, Expecto 832/0, Vitest 62) — not the reason.

REASONS:
- "Every outbound request carries the configured User-Agent" is not satisfied by production code: the only cover request this feature issues comes from `addBookFromOpenLibraryImpl` → `addBookToLibraryImpl`, which fetches `OpenLibrary.coverUrl id Large` (`Api.fs:1608`) with a bare `httpClient.GetAsync(url)` (`Api.fs:1517`) — no User-Agent, no throttle. The covering test (`OpenLibraryTests.fs:168`) only drives `searchBooks`/`getWork`/`getEditionByIsbn`, never the cover path.
- `OpenLibrary.downloadCover` (`OpenLibrary.fs:317`) — the one function that sends the User-Agent and wraps `throttleCoverCall` — has zero call sites in `src/` or `tests/`; dead code. The covers gate governs nothing at runtime. This bypasses ADR-0075 §5.
- "`addBookFromOpenLibrary` creates the book with `OpenLibraryWork` + `Isbn13` linked" holds only for a fixture that feeds an ISBN into `EditionKey`. `OpenLibrarySearchResult.EditionKey` is decoded from `edition_key` (an OLID like `OL33246498M`, never an ISBN), yet `addBookFromOpenLibraryImpl` feeds `request.EditionKey` to `getEditionByIsbn` (`Api.fs:1570`) and derives `Isbn13` by 13-digit-filtering that value (`Api.fs:1596`). With a real OLID: `/isbn/OL…M.json` 404s → no Isbn13, no edition id, no cover, and the title falls back to the work key — a book literally titled `/works/OL893415W`. The test's `sampleRequest.EditionKey = Some "9780593135204"` is written to the implementation's assumption.

SUGGESTED_FIX: Route the import's cover download through `OpenLibrary.downloadCover` (inject a cover-fetch into `addBookToLibraryImpl`, or call `downloadCover` in `addBookFromOpenLibraryImpl` and pass the ref into the add path) so the covers request carries the User-Agent and passes the covers gate; extend the recording-handler test to assert the UA on the cover request. Then make the edition lookup honour the field's real contract — `/isbn/{value}.json` only when ISBN-shaped, `/books/{OLID}.json` otherwise (or add an explicit `Isbn13` field to `AddBookFromOpenLibraryRequest` and have the search modal pass the search result's own `Isbn13`) — with a test that passes a genuine `edition_key` OLID from a `search.json` fixture and still asserts Isbn13/title/cover are linked.

ITERATION_HINT: likely-fixable

## Outcome

Built `src/Server/OpenLibrary.fs` — the Open Library anticorruption layer, mirroring `Tmdb.fs`'s
structure (config record, `fetchJson`, `Decoder<_>`s, a 1h `SearchCache`) and `Steam.fs`'s
`throttleStorefrontCall` shape (ADR-0066, copied not shared) for two independent gates:
`throttleApiCall` (1000ms, `openlibrary.org` — search/work/isbn/olid/author lookups) and
`throttleCoverCall` (3000ms, `covers.openlibrary.org`). Every outbound request, including the cover
download, carries the configured `User-Agent` via a manually-built `HttpRequestMessage`. `searchBooks`
decodes `search.json`'s `docs` into `Mediatheca.Shared.OpenLibrarySearchResult` (picking the first
13-digit ISBN from a mixed ISBN-10/13 list, defaulting a missing `cover_i` to `None`). `getWork`
decodes `/works/{key}.json`'s `description` field (plain string or `{type,value}` object, both decode
via `Decode.oneOf`). `getEditionByIsbn` (`/isbn/{isbn}.json`) and `getEditionByOlid`
(`/books/{OLID}.json`) share one `fetchEdition` helper and decoder, resolving at most 3
`/authors/{key}.json` references best-effort and returning `None` (not an exception) on a 404.
`downloadCover` treats both a 404 and a sub-1KB response (Open Library's placeholder image) as "no
real cover", saving `posters/book-{slug}.jpg` via direct `System.IO` writes (this module compiles
immediately before `ImageStore.fs` in `Server.fsproj`'s adapter block, per the task's own instruction).

`Shared.fs` gained `OpenLibrarySearchResult` (`CoverUrl` pre-built at size M) and
`AddBookFromOpenLibraryRequest` (now carrying an explicit `Isbn13: string option` alongside
`WorkKey`/`EditionKey`), plus three `IMediathecaApi` members appended at the tail
(`searchOpenLibraryBooks`, `addBookFromOpenLibrary`, `refreshBookFromOpenLibrary`) — after
`removeBookContentBlock`, per this task's own scheduling note, so integration-dhctm's own tail-append
lands cleanly.

`Api.fs`: extracted `addBook`'s duplicate-check/slug/cover-download/command sequence into
`addBookToLibraryImpl` (mirroring `addMovieToLibraryImpl`), reused by both `addBook` and
`addBookFromOpenLibraryImpl`. `addBookFromOpenLibraryImpl` fetches the work (always) and dispatches
the edition lookup on `EditionKey`'s shape (`isOlidShaped`/`isIsbnShaped`): a genuine `edition_key`
OLID (`OL...M`, the real shape `search.json` always sends) resolves via `getEditionByOlid`, an
ISBN-shaped value via `getEditionByIsbn`. `Isbn13` for the external-id link is read directly from the
request's own explicit field, never parsed out of `EditionKey`.

**Iteration 2** fixed both defects the verifier found in iteration 1:
1. **Cover download now goes through `OpenLibrary.downloadCover`.** `addBookToLibraryImpl` gained an
   optional `coverDownloader: (string -> Async<string option>) option` parameter — when present
   (Open Library's path), it's invoked with the slug `addBookToLibraryImpl` computes internally,
   instead of falling back to its bare, unthrottled, UA-less `httpClient.GetAsync(request.CoverUrl)`
   path (which `addBook`'s plain manual-entry path still uses via `None`).
   `addBookFromOpenLibraryImpl` builds this downloader from `editionOpt.CoverId` and
   `OpenLibrary.downloadCover`, so the cover request now carries the User-Agent and passes the covers
   gate — `downloadCover` is no longer dead code. `OpenLibraryApiTests.fs`'s add test now records every
   request's User-Agent (including the covers-CDN one) and asserts it explicitly; `OpenLibraryTests.fs`'s
   "every outbound request carries the User-Agent" test now also exercises `downloadCover` directly.
2. **`edition_key` is treated as the OLID it actually is.** Added `OpenLibrary.getEditionByOlid`
   (`GET /books/{OLID}.json`, sharing `getEditionByIsbn`'s decoder via a new private `fetchEdition`
   helper) and `AddBookFromOpenLibraryRequest.Isbn13: string option`. `addBookFromOpenLibraryImpl` now
   shape-dispatches `EditionKey` between the OLID and ISBN lookups instead of always calling
   `getEditionByIsbn` on a value that, for a real search hit, is never ISBN-shaped and would 404.
   `OpenLibraryApiTests.fs`'s `sampleRequest.EditionKey` is now a genuine OLID (`OL33246498M`, exactly
   what `search.json` decodes) with `Isbn13` set explicitly; the test asserts the edition actually
   resolved (title, page count, description, `OpenLibraryEditionKey`) rather than degrading to a
   work-key-titled book. `OpenLibraryTests.fs` gained two new unit tests for `getEditionByOlid` (decode
   + 404-degrades-to-`None`), mirroring `getEditionByIsbn`'s existing pair.

New tests (12 total across both iterations): `tests/Server.Tests/OpenLibraryTests.fs` (10 cases —
search/work/edition-by-isbn/edition-by-olid decoding, the 3-concurrent-calls throttle timing test at a
50ms injected interval, the independent-gates test, the User-Agent-on-every-request test including the
cover download, and the ISBN/OLID-404-degrades-to-`None` tests) and
`tests/Server.Tests/OpenLibraryApiTests.fs` (2 cases — `addBookFromOpenLibrary` end-to-end against a
stubbed `HttpMessageHandler` with a genuine OLID `edition_key`, asserting the linked external ids
(including the resolved `OpenLibraryEditionKey` and the explicit `Isbn13`), the User-Agent on the
recorded cover request, the downloaded cover file on disk, the cache-slice writes, and
`Duplicate_found` on a second call; `refreshBookFromOpenLibrary` asserting the cache's `description`
changes while `book_list`/`book_detail`'s `title`/`cover_ref` do not).

`npm test`: 834/834 green. `npm run build`: clean Fable compile (`✓ built in 32.13s`, only the
pre-existing unrelated `AdminProjections/Views.fs` warning). `npm run test:client`: 62/62 green,
unaffected.

Key files: `src/Server/OpenLibrary.fs` (`getEditionByOlid`, shared `fetchEdition`),
`src/Server/Api.fs` (`addBookToLibraryImpl`'s new `coverDownloader` parameter,
`addBookFromOpenLibraryImpl`'s shape-dispatching edition lookup and cover downloader,
`isOlidShaped`/`isIsbnShaped`), `src/Shared/Shared.fs` (`AddBookFromOpenLibraryRequest.Isbn13`),
`tests/Server.Tests/OpenLibraryTests.fs`, `tests/Server.Tests/OpenLibraryApiTests.fs`.
