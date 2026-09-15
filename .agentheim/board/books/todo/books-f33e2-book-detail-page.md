---
id: books-f33e2
title: Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove
status: todo
type: feature
context: books
created: 2026-09-16
completed:
depends_on: [books-y9kxy, integration-c8d4x, design-system-001-formalize-styleguide]
blocks: [intelligence-dnv2y]
tags: [books, detail-page, frontend, reading-progress]
related_adrs: [0076, 0016, 0015]
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
`isDashboardSection` — add if books-g7g1j hasn't), root `Types/State/Views.fs` wiring (`Cmd.map`,
the per-page delegation pattern every other detail page follows).

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
  (`ContentBlockEditor.view` with the book owner key), the Journal editor **only if** `JournalEditor`
  is media-agnostic (check GameJournal's coupling; if it is game-specific, leave it out and note it).
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
- [ ] `Route.test.fs` covers `["books"; slug]` (if not already from books-g7g1j).
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
  status unchanged) and same-day overwrites (Manual wins a same-day tie in the projection).
- ADR-0016 paper overlay for the popover; ADR-0015: review against the live StyleGuide page.
- `intelligence-dnv2y` (dashboard) navigates here; `books-g7g1j` (search) navigates here.
