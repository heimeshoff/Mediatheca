---
id: integration-y2ak4
title: Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs
status: todo
type: feature
context: integration
created: 2026-09-16
completed:
depends_on: [integration-wmqn3]
blocks: []
tags: [books, goodreads, rss, reading-progress, sync]
related_adrs: [0075, 0076, 0077, 0043]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
prior_art: []
---

## Why

"Progress should be based on the Goodreads percentage." Goodreads exposes numeric progress only in
the user's status updates. The research review gate verified live that
`https://www.goodreads.com/user_status/list/{user_id}?format=rss` is public, key-free and
cookie-free and carries items like "X is on page 137 of 248 of *Title*" and "X is 81% done with
*Title*". This task turns those items into progress observations.

## What

- `Goodreads.getStatusUpdates httpClient userId page` — `GET https://www.goodreads.com/user_status/
  list/{userId}?format=rss&page={n}`; parse each `<item>` into `{ StatusId: string (from link/guid);
  Text: string (title element, entity-decoded); PublishedAt: DateTimeOffset; Link }`.
- `Goodreads.parseProgress (text: string) : ProgressUpdate option` — a pure function over the item
  text, recognizing (case-insensitive, whitespace-normalized, the user's display name prefix
  stripped by taking everything after the first " is " / " finished "):
  - `is on page {N} of {M} of {Title}` → `PageProgress (N, M, Title)`
  - `is {N}% done with {Title}` → `PercentProgress (N, Title)`
  - `finished reading {Title}` / `is finished with {Title}` → `Finished Title`
  - `is starting {Title}` / `started reading {Title}` → `Started Title` (percent 0, no event unless
    the aggregate says so)
  Anything else → `None` (quotes, shelvings, reviews are skipped silently). Title normalization:
  strip surrounding quotes, trailing author parentheticals, subtitle after `:`; lower-case; collapse
  whitespace.
- **Join**: the status item carries no book id. Match the normalized title against the normalized
  titles of (a) the `currently-reading` shelf items fetched by the same sync run (which carry
  `BookId` → library slug via `GoodreadsBookId`), then (b) every library book whose `goodreads_book_id`
  is set, then (c) every library book by title. Exact normalized equality first; if none, a
  prefix match (status titles are sometimes truncated) that is unambiguous. No match → counted
  `Unmatched`, never an error.
- **Emit**: per matched `ProgressUpdate` case —
  - `PageProgress (n, m, _)` → `Percent = floor(n/m × 100)` (**floor, never round** — a
    round-half-up on 299/300 would hit 100 and auto-Finish a book Goodreads hasn't itself marked
    finished; `Books.decide` accepts a source's 100 at face value), `Position = Some (Page (n, Some m))`,
    `Finished = false`.
  - `PercentProgress (n, _)` → `Percent = n` (already an integer, no rounding needed), `Position = None`,
    `Finished = false`.
  - `Finished _` → `Percent = 100`, `Position = None`, `Finished = true` (the explicit signal, not a
    rounding artifact — this is the one case allowed to actually reach 100).
  - `Started _` → **do not emit** `Observe_reading_progress` at all (there is no percent to report);
    log/count it as `Started` in the sync result and move on. (The parenthetical "percent 0, no event
    unless the aggregate says so" in the pattern list above was ambiguous — this resolves it: a
    `Started` item never calls `Observe_reading_progress`, it is purely informational.)
  Every emitted command carries `Source = Goodreads`, `ObservedOn = PublishedAt` local date. Items are
  processed oldest-first so the aggregate's **per-source** same-percent no-op (compared against this
  book's latest *Goodreads* observation, not its global current percent — see `books-y9kxy`) and
  promotion rules see them in order.
- **Idempotency**: persist `goodreads_last_status_id` / `goodreads_last_status_at`; only items newer
  than the persisted marker are processed; on the first run, walk back at most 3 pages (or until an
  item older than 90 days). Re-running with no new items appends zero events.
- Wire into the "Goodreads shelf sync" job body (after the shelf step, in the same run) and into
  its result (`ProgressObserved`, `Unmatched`, `Started`, `Ignored` counts).

## Acceptance criteria

- [ ] `parseProgress` table-driven tests (`GoodreadsProgressTests.fs`): the four patterns above
      with real-shaped inputs (including a title containing " of " and a title with a colon), a
      quote item and a "added to shelf" item → `None`; HTML entities (`&amp;`, `&#39;`) decoded.
- [ ] Join test: a status "is on page 120 of 300 of Dune" with a currently-reading shelf item
      "Dune" (book id linked to slug `dune-1965`) emits `Reading_progress_observed { 40; Page (120,
      Some 300); Goodreads; 2026-09-15 }` on `Book-dune-1965`; an ambiguous prefix match emits nothing
      and counts `Unmatched`.
- [ ] Order and idempotency: three items for one book (10 %, 40 %, finished) on three dates, processed
      oldest-first, yield observations on the first two dates and a `Finished` status (Percent = 100,
      Position = None) on the third; re-running with the same feed appends zero events; a new 4th item
      (a different book) is the only thing processed on the next run.
- [ ] A `Started` item never calls `Observe_reading_progress` (assert zero commands issued for it) and
      is counted separately from `Unmatched`/`ProgressObserved` in the sync result.
- [ ] A `PageProgress (299, 300, _)` item yields `Percent = 99` (floored), not 100.
- [ ] First run on a fixture of two pages stops after page 2 and records the marker; a page request
      failure after page 1 still processes page 1's items and reports the error.
- [ ] `npm test` green.

## Notes

- ADR-0075 §4 (this task's contract), ADR-0076 (the aggregate owns promotion/finish; regression
  is recorded, never refused — a re-read shows up as a lower percent and that is correct).
- Research §2b (corrected) documents the feed's item shape and the title-only join problem; the
  page-2 depth and the exact "finished reading" wording are listed as open questions — pin fixtures
  from the builder's own feed once the user id is configured, and keep `parseProgress` tolerant
  (unknown → `None`).
- The builder's feed language may be German ("ist auf Seite N von M von …") if the Goodreads UI
  locale is German — add the German patterns if the pinned fixture shows them; otherwise English
  only. Do not attempt full i18n.
- Goodreads percent for audiobooks tracked there is also fine — the source badge says Goodreads,
  the position is a bare percent.
