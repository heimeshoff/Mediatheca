---
id: integration-wmqn3
title: Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN
status: doing
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, integration-dhctm, design-system-001-formalize-styleguide]
blocks: [integration-y2ak4, integration-jjvg2]
tags: [books, goodreads, adapter, settings, rss, sync, scheduled-job]
related_adrs: [0075, 0070, 0076, 0043, 0026, 0010]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
prior_art: [integration-qb7tk, integration-n3vqa]
---

## Why

The builder tracks print reading on Goodreads and wants it reflected in Mediatheca. The Goodreads
API is gone, but the public RSS feeds keyed by user id still work without any credential — the
cleanest possible position under ADR-0070's reasoning. This task brings shelf membership (what am I
reading / have read / want to read), ratings and read dates across; numeric progress from the
status feed is `integration-y2ak4`.

## What

**`src/Server/Goodreads.fs`** (adapter block):
- `parseUserId (input: string) : Result<string, string>` — accepts a bare numeric id, or any
  `goodreads.com/user/show/{id}[-slug]` / `goodreads.com/review/list/{id}…` URL; returns the digits.
- `getShelf httpClient userId (shelf: string) : Async<Result<GoodreadsShelfItem list, GoodreadsError>>`
  — `GET https://www.goodreads.com/review/list_rss/{userId}?shelf={shelf}` with a browser-like
  `User-Agent` and `Accept: application/rss+xml, application/xml`; parse with `System.Xml.Linq`
  into `{ BookId: string; Title; Author; Isbn: string option; Isbn13: string option; ImageUrl;
  LargeImageUrl: string option; NumPages: int option; AverageRating: float option; UserRating: int
  option (0 → None); Published: int option; DateAdded: string option; ReadAt: string option;
  Shelves: string list }` (element names per the research report §2a — verify against the live feed
  the builder's id returns and pin a fixture). `GoodreadsError = ProfilePrivateOrUnknown |
  FeedUnavailable of string | ParseFailed of string`. A 403/404, an empty channel with no title, or
  a redirect to a sign-in page → `ProfilePrivateOrUnknown`. Adapter-owned throttle at 1 request per
  2 seconds across all Goodreads calls.
- `getProfileName` — the channel `<title>` of the feed ("Marco's bookshelf: currently-reading").

**Settings** (keys `goodreads_user_id`, `goodreads_import_shelves` (JSON list, default
`["currently-reading"]`), `goodreads_sync_hour` (default 05), `goodreads_last_sync`,
`goodreads_last_sync_result`, `goodreads_last_error`):
- `IMediathecaApi`: `getGoodreadsSettings: unit -> Async<GoodreadsSettings>` (`{ UserId: string
  option; ImportShelves: string list; LastSync; LastResult; LastError }`), `setGoodreadsUserId:
  string -> Async<Result<string, string>>` (parses URL or id, stores the id), `setGoodreadsImportShelves`,
  `testGoodreadsConnection: unit -> Async<Result<string, string>>` (fetches `currently-reading`,
  returns `"{profile name}: N currently reading"`), `runGoodreadsShelfSync: unit -> Async<Result<
  GoodreadsSyncResult, string>>` (through `ScheduledJobs.tryStartJob`, recorded as a `manual` run).
- Settings page: a **Goodreads** `integrationCard` (Integrations grid, after Audible) with the
  id/URL input, a "profile must be public" hint, Save / Test, a status badge, three shelf checkboxes
  (currently-reading always on and disabled; read and to-read opt-in), last sync + result, "Sync
  now", and the standing `goodreads_last_error` notice.

**Shelf sync** (`GoodreadsSync.fs` or inside `Goodreads.fs`; `JobSpec` "Goodreads shelf sync" in
`Composition.fs`): for each configured shelf (`currently-reading` always; `read` / `to-read` when
opted in):
1. Fetch the feed; on `ProfilePrivateOrUnknown` persist `goodreads_last_error` and end the run failed.
2. For each item, resolve the book: `BookProjection.findByExternalId (GoodreadsBookId id)`, else by
   `Isbn13`, else by `Isbn`(10 → convert to 13). Found → `Link_external_id (GoodreadsBookId id)` if
   not yet linked (and `Isbn13` if known and unlinked). Not found → **import** when the shelf is
   configured for import: try `OpenLibrary.getEditionByIsbn` (isbn13, then isbn10) → `AddBookRequest`
   from the edition (Title/Authors/Year/CoverUrl L/Subjects, `Format = Print`, ExternalIds =
   Goodreads id + Isbn13 + OL keys) → else fall back to the feed's own title/author/published year and
   `LargeImageUrl` as the cover; `addBook` (duplicate check on) → `Duplicate_found` links the Goodreads
   id to the existing slug instead of creating. Then `upsertBookMetadata` with `page_count`
   (`NumPages`), `average_rating`, `source = "goodreads"` (+ description from Open Library when found).
3. Status mapping via `Change_status (status, effectiveOn)` — `books-y9kxy` already defines this
   signature (`BookStatus * effectiveOn: string option`, ADR-0077); this task is its first real
   caller and does not need to extend anything. **The adapter, not the aggregate, enforces
   "never demote"** — the aggregate's `Change_status` is a manual override that obeys whatever it's
   told, so this sync must check current status itself before calling it: `to-read → Change_status
   (Backlog, None)` only when the book is currently `Backlog` or newly created; `currently-reading →
   Change_status (InFocus, None)` unless already `InFocus` or `Finished`; `read → Change_status
   (Finished, Some readAtDate)` unless already `Finished` **with the same date** — ADR-0077's
   narrowed no-op rule means calling this again with a *different* `ReadAt` (a corrected Goodreads
   date, or a book Audible finished first at today's date) legitimately re-dates `finished_at`, so
   don't guard this call with "unless already Finished" alone or a genuine correction will be
   silently dropped. `readAtDate` is the feed's `ReadAt` (RFC-822, e.g.
   `"Tue, 02 Sep 2026 00:00:00 -0800"`) parsed and reformatted to `yyyy-MM-dd` before the command is
   issued — `decide` refuses anything else.
4. `UserRating` seeds `Set_personal_rating` only when the book has no rating yet.
5. Items on the feed but absent from the library after step 2 (shelf not configured for import) are
   counted as `Skipped`.
Result `{ Shelves: (shelf * fetched * created * linked * statusChanged * skipped) list; Errors }`
persisted as JSON; per-item failures never abort the run (ADR-0010).

## Acceptance criteria

- [ ] `GoodreadsTests.fs`: `parseUserId` accepts `12345678`, `https://www.goodreads.com/user/show/
      12345678-marco`, `goodreads.com/review/list/12345678?shelf=read`; rejects non-numeric input.
- [ ] The RSS parser decodes a pinned fixture (captured from a live public feed) into every field
      above; `user_rating` of `0` → `None`; missing ISBN elements → `None`.
- [ ] Sync over a fixture with one item already in the library (by ISBN-13, unlinked), one unknown
      item with an ISBN that Open Library resolves (stubbed), and one unknown item with no ISBN:
      links the first (`Book_external_id_linked GoodreadsBookId`), creates the second from Open
      Library data with `Format = Print`, creates the third from feed data with the feed cover, and
      moves all three to `InFocus`; running the same sync again appends zero events.
- [ ] `read` shelf item with `ReadAt = "Tue, 02 Sep 2026 …"` on a `Backlog` book → `Finished` with
      `book_list.finished_at = 2026-09-02`; a `to-read` item never demotes an `InFocus` book (no event);
      running the sync again with the identical `ReadAt` appends zero events; a book already `Finished`
      via an Audible import (`finished_at = today`) that this sync later finds on the `read` shelf with
      a real `ReadAt` gets exactly one re-dating event, converging `finished_at` to the true date.
- [ ] `UserRating = 4` sets the rating on an unrated book and does not touch a book already rated 5.
- [ ] A 403 feed response ends the run failed with `goodreads_last_error` = "profile private or user
      id unknown" and no events appended; the Settings badge shows it after reload.
- [ ] Job "Goodreads shelf sync" is listed in the Jobs section; "Sync now" records a `manual` run.
- [ ] Settings card reads as one of the existing integration cards. [human-eye]
- [ ] `npm test`, `npm run test:client` green; `npm run build` succeeds.

## Notes

- ADR-0075 is the contract: user id not key, no cookie ever, shelf feed for membership/ratings,
  status feed for progress (next task), Open Library for enrichment. ADR-0076 for the status rules
  (`Change_status` semantics; the sync never sends progress). ADR-0043: `average_rating`/`page_count`
  are cache; the Goodreads id is an event-carried external id.
- Research §2a has the shelf feed URL and fields; the 100-item-per-shelf cap is real — report it,
  don't work around it.
- The feed's `book_id` is the Goodreads *book* (edition) id. Open Library's `identifiers.goodreads`
  may carry it for the same edition — when `getEditionByIsbn` returns `GoodreadsIds` containing the
  feed's id, that is a confirmed match; otherwise ISBN equality is the match.
- Prior art: `integration-qb7tk` (card), `integration-n3vqa` (diff-before-enrich, per-item fault
  isolation, persisted last-result).
- **Scheduling notes (added during refinement, 2026-09-16):** this task now `depends_on`
  `integration-dhctm` because this task's own spec positions the Goodreads card "after Audible" in
  the Settings Integrations grid — dispatching both in the same parallel batch would leave this
  task's worker with no Audible card to anchor after. It also now `blocks` `integration-jjvg2`
  (Audible's own scheduled-job task): both this task's "Goodreads shelf sync" job and `jjvg2`'s
  "Audible progress sync" job append an entry to the same `Composition.fs` `scheduledJobs` list
  literal (~line 395) — the two tasks don't otherwise interact; this ordering exists purely to avoid
  a same-line-region merge conflict at squash time.
- A removed book cannot be re-added under the same slug (see `books-y9kxy`'s Notes); a shelf title
  the user has removed from Mediatheca will reappear under a new slug on the next sync that still
  sees it on the configured shelf. Existing Steam-import behavior — note it as a known limitation,
  not a bug to fix here.
