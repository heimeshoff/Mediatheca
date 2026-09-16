---
id: integration-wmqn3
title: Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN
status: done
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, integration-dhctm, design-system-001-formalize-styleguide]
blocks: [integration-y2ak4, integration-jjvg2]
tags: [books, goodreads, adapter, settings, rss, sync, scheduled-job]
related_adrs: [0075, 0070, 0076, 0043, 0026, 0010, 0078]
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

## Outcome

Built the Goodreads adapter and daily shelf sync per ADR-0075, plus a Settings card, on top of the
already-merged Books core (`books-y9kxy`), Open Library adapter (`integration-c8d4x`) and Audible
adapter (`integration-dhctm`).

**`src/Server/Goodreads.fs`** — `parseUserId` (bare numeric id, `user/show/{id}-slug` URL,
`review/list/{id}?shelf=...` URL, rejects non-numeric input); a 1-request/2s adapter-owned throttle
(`Goodreads.throttleCall`, ADR-0066's shape); `getShelf`/`getProfileName`, both backed by one private
`fetchFeed` that GETs `review/list_rss/{userId}?shelf={shelf}` with a browser-like User-Agent and
parses the response with `System.Xml.Linq` into `GoodreadsShelfItem` (`BookId`, `Title`, `Author`,
`Isbn`/`Isbn13`, `ImageUrl`/`LargeImageUrl`, `NumPages`, `AverageRating`, `UserRating` — `0` decodes to
`None` — `Published`, `DateAdded`, `ReadAt`, `Shelves`); `GoodreadsError = ProfilePrivateOrUnknown |
FeedUnavailable of string | ParseFailed of string`, with `describeError` mapping
`ProfilePrivateOrUnknown` to the fixed `"profile private or user id unknown"` text.

**`src/Server/GoodreadsSync.fs`** — `runSync` (compiled before `Api.fs`, so it carries its own local
`executeBookCommand`/`generateUniqueSlug`, mirroring `PlaytimeTracker.fs`/`GameFacetBackfill.fs`'s own
precedent for the same reason): always fetches all three shelves (`currently-reading`, `read`,
`to-read` — a design clarification recorded in the module's own doc comment: `goodreads_import_shelves`
gates only whether an UNMATCHED item on a shelf is imported, never whether the shelf is fetched at all,
so an existing library book's status/rating/links stay current from `read`/`to-read` even when the user
hasn't opted those shelves into importing NEW books — this is what makes the task's own "shelf not
configured for import -> Skipped" acceptance criterion meaningful rather than dead code). Matching is
`GoodreadsBookId` → `Isbn13` → `Isbn` (ISBN-10, converted to ISBN-13 via a standard check-digit
recompute, `isbn10ToIsbn13`, for the lookup only). An unmatched, import-eligible item resolves through
`OpenLibrary.getEditionByIsbn` (isbn13 then isbn10) and `OpenLibrary.getWork`/`downloadCover` when
found, else falls back to the feed's own title/author/`book_large_image_url` cover — always
`Format = Print`. Status mapping is adapter-owned (ADR-0075 §3's "never demote" rule — the aggregate's
`Change_status` is a manual override that obeys whatever it's told): `to-read` only issues
`Change_status (Backlog, None)` when the book is already `Backlog`; `currently-reading` issues
`Change_status (InFocus, None)` unless the book is already `Finished`; `read` unconditionally issues
`Change_status (Finished, Some readAtDate)` and lets `Books.decide`'s own ADR-0077 no-op/re-date rule
decide idempotence versus a legitimate correction. `user_read_at`'s RFC-822 day-of-week token is
stripped before parsing (`parseReadAtDate`) — `DateTimeOffset.TryParse` rejects the whole string
outright when that token doesn't match the date it precedes (verified against .NET's own parser during
this task), and only day/month/year/offset matter for `finished_at`. `user_rating` seeds
`Set_personal_rating` only on an unrated book. Per-item failures are caught and folded into the
result's `Errors` list without aborting the run (ADR-0010); a shelf-fetch failure (403/unknown feed)
ends the whole run as `Error`, persisting `goodreads_last_error` and appending no events. `jobLock` is
acquired only around brief DB moments, never across an awaited HTTP call (ADR-0028's discipline).

**Shared surface** (`src/Shared/Shared.fs`) — `GoodreadsSettings`, `GoodreadsShelfSyncSummary`,
`GoodreadsSyncResult`, and five new `IMediathecaApi` members appended after `addBookFromAudible`:
`getGoodreadsSettings`, `setGoodreadsUserId`, `setGoodreadsImportShelves`, `testGoodreadsConnection`,
`runGoodreadsShelfSync`.

**`src/Server/Api.fs`** — `getGoodreadsConfig` and `runGoodreadsShelfSyncNow` added to `Api.create`'s
signature after `getAudibleConfig` (two new parameters, not one — see ADR-0078 for why the second one
exists); the five new API members; `goodreads_import_shelves` stored as a JSON string list
(`decodeGoodreadsImportShelves`/`encodeGoodreadsImportShelves`, always including `currently-reading`).
Every `Api.create` call site (12 test files + `Composition.fs`) updated with the two new arguments.

**`src/Server/Composition.fs`** — `getGoodreadsConfig` (reads `goodreads_user_id`/
`goodreads_import_shelves`); the "Goodreads shelf sync" `JobSpec` (default 05:00 local, an hour clear
of the Steam playtime sync) appended to `scheduledJobs`; `runGoodreadsShelfSyncNow` — the ADR-0078
wrapper-`JobSpec` pattern that shares the real spec's `tryStartJob`/`JobRunRecorder` guard so the
Settings card's own "Sync now" is recorded as a `job_runs` row (`trigger = "manual"`) and refused if
the nightly fire is already in flight, while still handing the card a typed, synchronous
`GoodreadsSyncResult`.

**Settings card** (`src/Client/Pages/Settings/{Types,State,Views}.fs`) — a **Goodreads**
`integrationCard` (Icons.star) positioned after Audible in the Integrations grid: profile-URL-or-id
input with Save/Test, `currently-reading` shown always-on-and-disabled plus two opt-in checkboxes for
`read`/`to-read`, a standing "profile private or user id unknown" notice, last-sync/last-result display,
and a "Sync now" button showing the typed result inline.

**Tests** — `tests/Server.Tests/GoodreadsTests.fs` (7 cases: `parseUserId`'s three accepted shapes plus
rejection; the RSS parser against a pinned two-item fixture asserting every field including
`user_rating=0 -> None` and missing-ISBN `-> None`; `getProfileName`; a 403 -> `ProfilePrivateOrUnknown`
mapping). `tests/Server.Tests/GoodreadsSyncTests.fs` (5 cases): the three-item currently-reading
scenario (link-by-ISBN13, Open-Library-resolved import, feed-only import, all promoted to `InFocus`,
idempotent re-run); the to-read-never-demotes / read-finishes-a-Backlog-book /
read-re-dates-an-Audible-finished-book scenario (idempotent re-run); `UserRating` seeding; the 403
end-the-run-failed case; and a job-registration test exercising the REAL `Administration.create`
surface (`getJobStatuses`/`runJobNow`) to prove "Goodreads shelf sync" is listed and a manual run is
recorded with `Trigger = "manual"`. `src/Client/Pages/Settings/GoodreadsCard.test.fs` (4 cases): the
user-id save/reject reducer paths and the sync-completed notice-setting/clearing-deferred-to-reload
paths (the two shelf-checkbox toggles were deliberately not unit-tested here, for the identical reason
`AudibleAuthFileTests.fs` never dispatches `Save_audible_auth_file`/`Test_audible_connection` against a
`fakeApi` -- `Unchecked.defaultof<IMediathecaApi>` compiles to a bare `null` in Fable, and even a field
READ on it throws before the message handler's own logic runs; the card's rendering itself is this
task's own `[human-eye]` acceptance criterion).

**Verification**: `npm test` -> 871 Expecto tests, all green (16 new). `npm run test:client` -> 74
Vitest/Fable.Mocha tests, all green (4 new). `npm run build` -> clean, 189 modules.

**Notes / known limitations** (per the task's own text): the shelf feed's 100-item-per-shelf cap is a
real Goodreads limitation, not worked around. A book removed from Mediatheca will reappear under a new
slug on the next sync that still sees it on a configured shelf (the same limitation `books-y9kxy`
already documents for Steam imports). `goodreads_import_shelves` is stored as a JSON string list rather
than the task text's literal wording being tested for exact on-disk shape — no acceptance criterion
depends on the storage format itself.
