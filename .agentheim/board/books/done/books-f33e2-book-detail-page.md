---
id: books-f33e2
title: Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove
status: done
type: feature
context: books
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, design-system-001-formalize-styleguide]
blocks: [intelligence-dnv2y, books-g7g1j]
tags: [books, detail-page, frontend, reading-progress]
related_adrs: [0076, 0077, 0016, 0015]
related_research: []
prior_art: []
---

## Why

"Have a details page that is similar to a movie's detail page." The detail page is where a book's
progress, status and rating live for the user, and — since Goodreads' feed and Audible's sync can
both be absent for a given book — where the user can set progress by hand (the always-available
fallback ADR-0075/0076 assume).

## What

`src/Client/Pages/BookDetail/{Types,State,Views}.fs` (after `Pages/GameDetail/Views.fs` in
`Client.fsproj`), `Router.fs` (`Book_detail of slug`, `["books"; slug]`, `toUrl`,
`isDashboardSection`), root `Types/State/Views.fs` wiring (`Cmd.map`, the per-page delegation
pattern every other detail page follows). **This task owns the `Router.fs` edit** — `books-g7g1j`
now `depends_on` this task specifically to avoid both tasks independently adding the identical new
DU case and match arms (refined 2026-09-16; the two tasks originally specified the same ~10-line
edit with no ordering between them, which would have produced a duplicate-union-case compile error
at squash time if dispatched in the same batch).

Layout mirrors `Pages/MovieDetail/Views.fs` (hero + two-column content grid, `detailCard`,
`personalRatingCard`, `friendsCard`, `EventHistoryModal`, `ContentBlockEditor`):
- **Hero**: portrait cover (`CoverRef`, `PosterCard`-sized, placeholder when `None`), title,
  authors, year, a **format badge** (Audiobook / Print / Ebook), and the length line — "9 h 12 min ·
  narrated by X" for audiobooks (from `RuntimeMinutes` / `Narrators`), "384 pages" for print (from
  `PageCount`); series line "Book 2 of *Series*" when the cache has it.
- **Progress card** (left column, first): a `DesignSystem` progress bar (reuse the season/episode
  progress primitive from design-system-mz9v7 if it fits; else a plain gold-on-brown bar) showing
  `ProgressPercent`, the **source badge** (Audible / Goodreads / Manual), "observed {date}", and an
  **Update progress** control: a paper-overlay popover (ADR-0016) with two inputs — percent, or
  page + total pages (total prefilled from `PageCount`) — and an optional date (`EditableDateInput`,
  default today); submits `api.setBookProgress`. Below: a **progress history** list
  (`ProgressHistory`, newest first: date · percent · source · position), each row with a remove
  action (`api.removeBookProgressObservation`) behind a confirm — the watch-sessions list pattern.
- **Status control** in the hero (the GameDetail status segmented control shape): Backlog / In Focus
  / Finished / Abandoned → `api.setBookStatus`. Finished shows "finished {finished_at}".
- **Personal rating** card → `api.setBookPersonalRating` (reuse `personalRatingCard` / `HeroRating`).
- **Details** card: description (cache), publisher · published date · language, subjects as chips,
  average rating from the source when present.
- **Links** card: Audible (`https://www.audible.{tld}/pd/{asin}` using the marketplace from
  `getAudibleStatus`), Goodreads (`https://www.goodreads.com/book/show/{id}`), Open Library
  (`https://openlibrary.org{workKey}`) — only the ids the book carries.
- **Right column**: `friendsCard` with recommended-by (`api.recommendBookBy` /
  `removeBookRecommendation`, the movie `FriendManager` modal reused), content blocks
  (`ContentBlockEditor.view` wired to the new `getBookContentBlocks`/`addBookContentBlock`/… family
  `books-y9kxy` adds — there is no "book owner key" to pass; it's a distinct method family per BC,
  mirroring how `ContentBlockEditor` is already wired for Series/Games). **Skip the Journal editor —
  confirmed game-specific, not media-agnostic** (verified during refinement, 2026-09-16):
  `JournalEditor.view` hardcodes `api.saveGameJournal`/`api.getGameJournal` at two call sites, backed
  by `GameJournal.fs`'s own `game_journal_blocks` table; there is no generic journal storage and no
  load/save-as-parameters seam to plug a book into. Reusing it would require a full parallel
  `book_journal_blocks` table + `BookJournal.fs` module (out of scope for this task and `books-y9kxy`
  as written) or a `JournalEditor` refactor (also out of scope). Leave it out; note this finding in
  your RESULT rather than attempting a workaround.
- **Action menu** (`ActionMenu`): Refresh metadata (`api.refreshBookFromOpenLibrary` when an OL key
  or ISBN exists), Change format, Event history (`EventHistoryModal.view $"Book-{slug}"`), Remove
  book (confirm → `api.removeBook` → navigate to Dashboard).
- Every command re-fetches `getBook` (the movie page's pattern); the first load fans out to
  `getBook`, `getFriends`, `getAudibleStatus` (for the marketplace link).

## Acceptance criteria

- [ ] `Pages/BookDetail/Progress.test.fs` (Fable.Mocha): a pure `buildProgressRequest` seam turns
      (page 120, total 300) into `{ Percent = None; Page = Some 120; TotalPages = Some 300 }` and a
      bare 45 into `{ Percent = Some 45; … }`; rejects percent > 100 and page > total with a message
      shown in the popover (model state, no `Cmd`).
- [ ] `Route.test.fs` covers `["books"; slug]` — this task owns the `Router.fs` edit (see What
      section); `books-g7g1j` confirms rather than duplicates it.
- [ ] Loading a book whose cache has `RuntimeMinutes = 552` and `Narrators = ["X"]` renders
      "9 h 12 min · narrated by X" (a pure `lengthLine` function, unit-tested; print variant "384 pages").
- [ ] Setting progress to 100 via the popover results in the page showing status Finished after the
      refetch (server-side aggregate rule; a Playwright e2e in `tests/e2e/book-detail-progress.spec.ts`
      drives it against the dev stack per ADR-0027, using `addBook` directly to seed).
- [ ] Removing a progress observation re-renders the bar from the remaining latest observation.
- [ ] The page passes `/design-check` — paper overlay for the popover and modals, Instrument Serif
      headings, `font-mono` for the percent/date/page figures, no translucency.
- [ ] Side by side with a movie page, the book page reads as the same family: same hero rhythm,
      card chrome, action menu placement; the progress bar is the one visual element the movie page
      lacks and it sits naturally as the first card. [human-eye]
- [ ] `npm run build`, `npm run test:client`, `npm test`, `npm run test:e2e` (the new spec) green.

## Notes

- Copy `Pages/MovieDetail` file by file and prune — do not start from GameDetail (its tabs and play
  facets are noise here); take only GameDetail's status segmented control.
- ADR-0076 for what the aggregate does with a manual 100 % (Finished), a lower percent (recorded,
  status unchanged) and same-day overwrites (Manual wins a same-day tie in the projection). ADR-0077
  (drafted by `books-y9kxy`) for `Change_status`'s `effectiveOn` — `book_list.finished_at` is a plain
  `yyyy-MM-dd` date string, not a timestamp; render "finished {finished_at}" as a date, no time part.
- ADR-0016 paper overlay for the popover; ADR-0015: review against the live StyleGuide page.
- `intelligence-dnv2y` (dashboard) navigates here; `books-g7g1j` (search) navigates here and now
  `depends_on` this task (Router.fs ownership, see What section).

## Outcome

Built `src/Client/Pages/BookDetail/{Progress,Format,Types,State,Views}.fs`, mirroring `Pages/MovieDetail`'s
structure (hero + two-column content grid, `panelCard`, `personalRatingCard`, `FriendManager`,
`EventHistoryModal`, `ContentBlockEditor`) pruned to what the task's What section asked for, plus
GameDetail's dropdown-based status-control shape (`HeroStatus`) adapted to `BookStatus`'s four cases.

**Hero**: portrait cover (`CoverRef`, placeholder `Icons.book`), title, authors, year, a format badge,
the length line ("9 h 12 min · narrated by X" / "384 pages", `Format.lengthLine`), a series line
("Book N of *Series*", `Format.seriesLine`), and the status control. `Format.fs` and `Progress.fs` are
pure, Feliz-free modules (the `SeriesDetail.NextUp` split) unit-tested in `Format.test.fs`/`Progress.test.fs`.

**Progress card** (left column, first): `DesignSystem.progressContinuous`, the percent/source/observed-date
line, a "History" list (newest first, each row behind a per-row confirm before
`api.removeBookProgressObservation`), and an "Update progress" button opening a `ModalPanel`-backed
popover (paper overlay, ADR-0016) with Percent/Page tabs, an inline validation message from
`Progress.buildProgressRequest`, and a native date input defaulting to today.

**Status control**: `bookStatusBadge`/`bookStatusLabel` are Books' own — `DesignSystem.statusBadge` was
*not* reused directly because its label is hardcoded per `LifecycleStatus` case ("Retired"), and the
task's acceptance criteria require the control to literally read "Finished" (a book is finished, not
retired). The CSS color vocabulary (`status-badge-retired` etc.) is still reused for Finished's "done"
hue, keeping the visual family consistent with Games/Movies while getting Books' own words right — this
is documented inline in `Views.fs` rather than as a separate ADR (a page-level label choice, not a
cross-cutting decision).

**Personal rating / Details / Links cards**: `personalRatingCard` (rating dropdown, `api.setBookPersonalRating`);
description/publisher/published-date/language/subjects/average-rating; Audible (built from `AudibleAsin`
against the `audible.de` host — `getAudibleStatus` does not exist yet in this worktree's `Shared.fs`, per
the dispatch's fallback instruction; the sibling `integration-dhctm`/a follow-up task is expected to
thread the marketplace-aware host in later), Goodreads, and Open Library links, shown only for the ids
the book carries.

**Right column**: `friendsCard` (recommended-by only, `api.recommendBookBy`/`removeBookRecommendation`,
the movie `FriendManager` modal pattern reused) and `ContentBlockEditor.view`. Content-block add/update/
remove/get use the book-specific `addBookContentBlock`/`updateBookContentBlock`/`removeBookContentBlock`
family `books-y9kxy` added; change-type/reorder/group/ungroup have no book-specific counterpart in
`Shared.fs`, so they reuse the generic bare-slug-keyed `IMediathecaApi` methods against the same slug —
correct today because the underlying `ContentBlocks` stream is shared bare-slug-keyed across media types
(a known, already-recorded limitation), noted in `State.fs` and filed as a backlog item below in case
that limitation is ever resolved out from under this reliance.

**Journal editor**: confirmed and left out — `JournalEditor.view` hardcodes `api.saveGameJournal`/
`api.getGameJournal` at its two call sites with no generic storage/load-save seam; reusing it for Books
would need a parallel `book_journal_blocks` table and module, out of scope here (matches the task's own
Notes finding).

**Router.fs** (owned by this task): added `Book_detail of slug`, `["books"; slug] -> Book_detail slug`,
`toUrl`/`navigateTo`, and `isDashboardSection`. Root `Types.fs`/`State.fs`/`Views.fs` gained the
`BookDetailModel`/`Book_detail_msg` delegation, an `init`/`Url_changed`/`Go_back` wiring identical in
shape to `Game_detail`'s (no Books dashboard tab exists yet, so `Go_back`'s empty-stack fallback lands on
the Dashboard without pre-selecting a tab). `StreamDetail/Views.fs`'s `pageForDetailLink` also gained a
`"books" -> Book_detail` case — a one-line, directly-enabled follow-through of the same Router.fs change
(without it, a book event stream row in the admin projection panel would render no drill-in link at all).

**Tests**: `Progress.test.fs` (6 cases) proves `buildProgressRequest` shapes `(page, total)` and a bare
percent correctly and rejects percent > 100 / page > total / negative / non-numeric input.
`Format.test.fs` (7 cases) proves `lengthLine`'s audiobook/print/ebook branches (including the exact
"9 h 12 min · narrated by X" string from the acceptance criteria) and `seriesLine`'s position+name rule.
`Route.test.fs` gained the `["books"; slug] -> Book_detail` case and an `isDashboardSection` assertion —
this task owns that edit per the refinement note; `books-g7g1j` confirms rather than duplicates it.
`tests/e2e/book-detail-progress.spec.ts` seeds a book via a direct `addBook` call, drives the popover
twice (a backdated 60% observation, then a 100% observation today), asserts the status control reads
"Finished" and the bar reads "100%", then removes the newest (100%) history row and asserts the bar
re-derives to the remaining 60% observation while status stays Finished (ADR-0076: removal never
un-finishes a book) — covering both the "100% finishes" and "removing an observation re-renders from the
remainder" acceptance criteria in one hermetic run.

Verified: `npm run build` (Fable typecheck + production bundle), `npm run test:client` (12 files / 76
tests), `npm test` (834 Expecto tests), and `npx playwright test tests/e2e/book-detail-progress.spec.ts`
all green.

Not independently verified: the "side by side with a movie page, the book page reads as the same family"
[human-eye] criterion — the page reuses `panelCard`/`velvetCard`/`paper-overlay`/`rating-dropdown` and the
hero rhythm verbatim from `MovieDetail`/`GameDetail`, but a human visual pass is outside a worker's tools.
