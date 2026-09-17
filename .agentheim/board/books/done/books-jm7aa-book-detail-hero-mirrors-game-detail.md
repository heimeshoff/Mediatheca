---
id: books-jm7aa
title: Book detail hero mirrors the game detail hero — the personal rating sits right of the status badge as a hero control, the reading state closes the hero meta block (finished date when Finished, otherwise a percent progress line), Details print on the page background like the movie synopsis, and Links move to the right column above Recommended By
status: done
type: feature
context: books
created: 2026-09-17
completed:
depends_on: [design-system-001-formalize-styleguide, books-nvnyk, books-depwh]
blocks: []
tags: [books, detail-page, frontend, hero, layout]
related_adrs: [0076]
related_research: []
prior_art: [books-f33e2]
---

## Why

The book detail page (`/books/{slug}`, shipped in `books-f33e2`) was built as a stack of
`panelCard`s under the hero — Reading Progress, My Rating, Details, Links — while the game
detail page puts the same facts *in the hero* (status badge + rating control on one row) and
keeps its Links in the right column. The builder wants the book page to read like the game
page: the state of the book (format, status, rating, how far along) belongs in the hero
block at the top, the prose belongs on the page background the way the movie synopsis does,
and the outbound links belong in the right column. The extra cards are noise.

## What

All changes are in `src/Client/Pages/BookDetail/Views.fs` (plus `Types.fs`/`State.fs` only
if a message is needed). No server, Shared, or projection change.

**1. Badge row — same size as the game hero.** The format badge (`Audiobook` / `Print` /
`Ebook`) and the status badge must render at exactly the size the game hero's genre badges
and status badge render at. Today both pages already use the identical class strings
(`bg-primary/80 px-3 py-1 rounded text-xs font-bold tracking-wider uppercase text-primary-content`
and the `status-badge status-badge-*` CSS vocabulary) and measure the same in the DOM at
desktop width — see Notes. The worker's job here is to *verify* the parity in the rendered
DOM at desktop **and** at a phone-width viewport, fix whatever differs if anything does, and
leave the two heroes sharing the same badge markup so they cannot drift again.

**2. Rating in the hero, right of the status badge.** Add a Books `HeroRating` component on
the same row as `HeroStatus`, after it, following `GameDetail.Views.HeroRating` exactly:
when a personal rating is set, an icon + name button in the rating's colour class; when not,
the source rating as `starRating` (Books: `book.AverageRating`, the cache-tier average) or a
quiet `Rate` link when there is none. Clicking opens the existing `rating-dropdown`
(paper overlay, `fixed z-[201]`) with the same options and the `Clear rating` item, dispatching
the existing `Toggle_rating_dropdown` / `Set_personal_rating` messages. Delete
`personalRatingCard` and its call.

**3. Reading state closes the hero meta block.** The hero's text column is, top to bottom:
badge row (format · status · rating) → `h1` title → the meta line (authors · year · length /
narrators) → the series line when present → **one reading-state line**:
- **Finished:** `finished {FinishedAt}` (the line that already exists), mono, muted.
- **Not finished:** a progress line — `DesignSystem.progressContinuous` (the same bar the
  progress card uses) with the percent, the source badge (`AUDIBLE` / `GOODREADS` / `MANUAL`)
  and `observed {date}` next to it, i.e. the progress card's summary row moved into the hero.
  A book with no observation yet shows the bar at 0 % and no source/observed text.

The **Reading Progress card is dissolved**: the summary row moves to the hero as above; the
`Update progress` control moves into the hero `ActionMenu` (next to `Change format`) and still
opens the existing `progressPopover`; the observation **History** list (with its remove
affordance) is kept but rendered on the page background under the details as a plain section
(see 4), not a card. If the builder later wants the history gone entirely that is a separate
capture — this task keeps every existing affordance reachable.

**4. Details on the background, movie-style.** Replace `detailsCard`'s `panelCard` with the
movie page's pattern: a `Html.section` with the movie `sectionHeader` (`h2`, `text-2xl
font-bold font-display`, gold bar) reading `Details`, then the description at
`text-base-content/70 leading-relaxed text-lg` (rendered through the rich-text component
`books-nvnyk` introduces), then the subject chips, then the `Publisher · Language` line as
`books-depwh` leaves it, then the average rating if the card showed it. Copy `sectionHeader`
into Books' `Views.fs` the way Games did, or lift it into `DesignSystem.fs` if it is identical
in all three pages — the worker's call, but no new visual variant. The History section from
(3) follows as a second `sectionHeader "History"` section in the same column. The left
column's spacing becomes `space-y-10` like the movie page.

**5. Links in the right column.** Move the links out of the left column into the right
column (`lg:col-span-4`) **above** `friendsCard`, as a `panelCard` titled `Links` whose rows
match the game page's link rows: `flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5
… text-sm font-medium`, a leading icon (`Icons.book` for Audible / Open Library, `Icons.globe`
or the closest existing icon for Goodreads — no new icon assets), the label, and a trailing
`Icons.externalLink` at `ml-auto text-base-content/30`. Same three links (Audible,
Goodreads, Open Library), same visibility rule (only present ids), `target="_blank"` +
`rel="noopener noreferrer"`. When the book has no external ids the card is not rendered.

Notes editor stays in the right column under Recommended By, unchanged.

## Acceptance criteria

- [ ] The book hero's badge row renders the format badge, `HeroStatus`, and a new `HeroRating` in that order inside the same `flex flex-wrap items-center gap-3 mb-3` row; `personalRatingCard` no longer exists in `BookDetail/Views.fs`.
- [ ] `HeroRating` (Books) renders the rated icon + name when `PersonalRating` is `Some n` with `n > 0`, `starRating AverageRating` when unrated and `AverageRating` is `Some`, and a `Rate` text button when both are absent; clicking any of them dispatches `Toggle_rating_dropdown`, and the dropdown items dispatch `Set_personal_rating`.
- [ ] Format badge and status badge in the book hero have byte-identical class strings to the game hero's genre badge and `DesignSystem.statusBadge`, and the rendered `getBoundingClientRect().height` and computed `font-size` of each match the game hero's counterpart at 1519 px and at 390 px viewport width (verified with Chrome DevTools; record the numbers in the Outcome).
- [ ] For a book whose `Status` is `Finished`, the hero's last line reads `finished {FinishedAt}` and no progress bar is rendered anywhere on the page.
- [ ] For a book whose `Status` is not `Finished`, the hero's last line is a `progressContinuous` bar at `ProgressPercent / 100` followed by `{ProgressPercent}%`, the source label, and `observed {ProgressObservedOn}` when a source is present; with no observation the bar renders at 0 and no source/observed text appears.
- [ ] The Reading Progress `panelCard` is gone; `Update progress` is an item in the hero `ActionMenu` and dispatches `Open_progress_popover`; the popover's page/percent submission still works unchanged.
- [ ] The observation history rows (date, percent, source, position, remove) still render, under a `History` section header on the page background, with the remove confirmation flow unchanged.
- [ ] `detailsCard` no longer wraps its content in `panelCard`/`velvetCard`; the description, chips and metadata line render inside a `section` headed by the movie-style `sectionHeader "Details"`, and no `velvet-card` class remains in the left column of the book page.
- [ ] The `Links` panel is a child of the right column (`lg:col-span-4`) and precedes `friendsCard`; each link row carries a leading icon, the label, and a trailing `Icons.externalLink`, with `target="_blank"` and `rel="noopener noreferrer"`; the panel is absent when the book has no Audible, Goodreads, or Open Library id.
- [ ] Existing `BookDetail` client tests (`State.test.fs`, `Progress.test.fs`, `Format.test.fs`) still pass under `npm run test:client`, and `npm run build` succeeds.
- [ ] Side by side at desktop width, the book hero and the game hero read as the same composition: same badge weight, same rating control placement, same title/meta rhythm. [human-eye]

## Notes

- **Measured 2026-09-17 on the running dev build (1519 px viewport, DPR 1.25):** book hero
  format badge `12px` / `24px` tall, status badge `10px` / `24px` tall; game hero genre badge
  `12px` / `24px`, status badge `10px` / `24px`. The class strings are identical in source.
  The builder perceives a size difference; it did not reproduce at desktop width. The worker
  should re-measure at phone width and check whether the *composition* (three gold genre
  badges + status + stars on Games vs. one gold badge + status on Books) is what reads as
  "smaller", and say so in the Outcome rather than inventing a fix if nothing differs.
- **Dependencies are real merge dependencies, not sequencing preference.** `books-nvnyk`
  (`doing/`) rewrites the description rendering inside `detailsCard`; `books-depwh` (`todo/`)
  edits `detailsCard`'s metadata line. This task deletes `detailsCard`'s card wrapper and moves
  the function's output into a section — it must land after both so it restructures their
  final code, not a stale copy.
- Patterns to copy, all already in the repo: `GameDetail/Views.fs` `HeroRating` (lines ~172–272)
  and the right-column `Links` panel (~1053–1100); `MovieDetail/Views.fs` `sectionHeader` (~16)
  and the Synopsis section (~960); `BookDetail/Views.fs` `progressCard` summary row (~257–300)
  for the hero progress line.
- Status lifecycle and finished-date semantics: ADR-0076, ADR-0077. `FinishedAt` is a
  `yyyy-MM-dd` string; render it as-is (the existing line already does).
- Paper overlay rule (ADR-0016) is already satisfied by reusing `rating-dropdown` for the new
  hero rating dropdown — no new floating surface is introduced.
- Prior art: `books-f33e2` built the page this task reshapes; its task file lists the cards
  being dissolved here.

## Outcome

`src/Client/Pages/BookDetail/Views.fs` was restructured so the book detail
page reads like the game detail page (games-shape parity), reusing
`RichText.render` (books-nvnyk) and `Format.metaLine` (books-depwh) as-is:

- **Badge row** (`flex flex-wrap items-center gap-3 mb-3`): format badge →
  `HeroStatus` → new `HeroRating`, in that order. `HeroRating` is
  `GameDetail.Views.HeroRating` copied verbatim with `book.AverageRating`
  (already a 0-5 Goodreads scale, `Server/Goodreads.fs` line 36 — NOT
  halved, unlike Games' 0-10 RAWG scale) standing in for `RawgRating`; a
  new local `starRating` renders the 0-5 value. `personalRatingCard` and
  its call are deleted.
- **Hero reading-state line** closes the meta block: `Finished` still
  renders `finished {FinishedAt}`; every other status renders a new
  `heroProgressLine` (the old progress card's summary row — bar +
  `{Percent}%` + source + `observed {date}` — moved verbatim into the
  hero). No observation → bar at 0%, no source/observed text (verified:
  screenshot equivalent state showed `0%` and nothing else).
- **Reading Progress card dissolved**: `progressCard` (the panel) is gone;
  its `Update progress` button is now an `ActionMenu.ActionMenuItem`
  (`Icons.chartBar`) in the hero's `ActionMenu.heroView`, dispatching the
  unchanged `Open_progress_popover`/`progressPopover`. The history list
  survives as `historySection` — a `sectionHeader "History"` + the
  unchanged `progressHistoryRow` list (remove-confirm flow untouched) — on
  the page background, guarded the same way the old card guarded it (no
  section when history is empty).
- **Details section**: `detailsCard` (a `panelCard`) became `detailsSection`
  (a plain `Html.section`) headed by a local `sectionHeader` copied from
  `GameDetail`/`MovieDetail` (both already carry their own private copy of
  the identical composition — Books following suit rather than adding a
  third DesignSystem consumer was the worker's call per the task's
  wording). Order: description (`RichText.render`, wrapped in a `text-lg`
  div so its own `text-base-content/70 leading-relaxed` classes plus the
  wrapper's size class match the movie Synopsis's exact class string) →
  subject chips → `metaLine` (`books-depwh`, unchanged) → average rating.
  No `velvet-card`/`panelCard` wrapper remains anywhere in the left column
  (`grep` confirms the only `velvetCard` reference left in the file is
  `panelCard` itself, used solely by the right-column `linksCard`/
  `friendsCard`).
- **Links panel**: moved into the right column (`lg:col-span-4`), directly
  above `friendsCard`. Each row now matches `GameDetail`'s link-row classes
  exactly (`flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5
  transition-colors text-sm font-medium text-base-content/70
  hover:text-primary`), with a leading icon (`Icons.book` for
  Audible/Open Library, `Icons.globe` for Goodreads — no new icon assets)
  and a trailing `Icons.externalLink` at `ml-auto text-base-content/30`.
  Same three links, same presence rule, `target="_blank"` +
  `rel="noopener noreferrer"` unchanged. Absent (`Html.none`) when the
  book has no external id — confirmed by screenshot before/after linking
  fixture ids via `linkBookExternalId`.
- Left column spacing changed from `space-y-6` to `space-y-10`, matching
  the movie page.

**Badge parity, measured against an isolated dotnet instance** (built
client + `dotnet <worktree>/src/Server/bin/Debug/net9.0/Server.dll` on
`127.0.0.1:5100`, `DATA_DIR` a throwaway temp folder, one fixture game and
one fixture book seeded via direct `addGame`/`addBook` — both left at
their default `Backlog` status so the two hero compositions are compared
like-for-like), via a throwaway Playwright script:

| | 1519 px | | 390 px | |
|---|---|---|---|---|
| | height | font-size | height | font-size |
| Game genre badge | 24px | 12px | 24px | 12px |
| Book format badge | 24px | 12px | 24px | 12px |
| Game status badge (Backlog) | 25px | 10px | 25px | 10px |
| Book status badge (Backlog) | 25px | 10px | 25px | 10px |

Byte-identical at both viewports once both fixtures are compared at the
same status. (An earlier pass compared a `Backlog` game against an
`InFocus` book and saw a spurious 25px-vs-27px gap — that was
`.status-badge-in-focus`'s own, pre-existing, intentional extra padding
(`padding: 6px 13px` vs the default, `index.css` line 289), which the
in-focus gold-sweep variant already carries identically on *both* pages;
it was an artifact of comparing two different statuses, not a hero-parity
bug, and needed no fix.) The class strings were already byte-identical in
source (as the task's Notes anticipated); no change was needed to make
them so. The composition difference the builder perceived (Games' three
gold genre badges + status + stars vs. Books' one gold badge + status)
is real and unchanged by this task — reproduced in side-by-side
screenshots — but is a content-volume fact (Games carries more badges),
not a sizing bug, matching the task Notes' own hypothesis.

Screenshots taken during verification (not carried anywhere, described
here only): book hero with an in-progress read (progress bar + Details/
History sections, no Details card chrome); book hero for a `Finished`
book (`finished {date}`, no bar); book hero with all three external ids
linked (Links panel in the right column, above Recommended By); book hero
with a personal rating set (`HeroRating` shows the rated icon + name,
`rating-dropdown` opens with all six options + "Clear rating").

Verified: `npm run build` succeeds; `npm run test:client` — 17 files, 116
tests, all passing (unchanged — `State.test.fs`/`Progress.test.fs`/
`Format.test.fs` are pure-logic tests untouched by this view-only
restructure).

Not touched, out of scope per the task's own "All changes are in
`Views.fs`" framing: `Types.fs`, `State.fs`, Shared, server, and
`tests/e2e/book-detail-progress.spec.ts` (see `BACKLOG_ITEMS` — that spec
now needs an extra click to open the hero `ActionMenu` before it can find
"Update progress", since the button moved out of the dissolved Reading
Progress card).
