---
id: integration-c8d4x
title: Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled
status: todo
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
