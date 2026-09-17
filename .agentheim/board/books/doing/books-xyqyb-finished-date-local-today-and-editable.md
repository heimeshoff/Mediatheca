---
id: books-xyqyb
title: Manual finish stamps today's local date and the hero's finished-date line is click-to-edit — `Set_book_status Finished` sends `Some localToday` instead of `None` (no more UTC-midnight drift), and the `finished {date}` line opens an `EditableDateInput` whose commit re-dates the finish via `setBookStatus slug Finished (Some picked)`
status: doing
type: feature
context: books
created: 2026-09-18
completed:
depends_on: [design-system-001-formalize-styleguide]
blocks: []
tags: [books, frontend, finished-date, date-picker, book-detail]
related_adrs: [0082, 0077]
related_research: []
prior_art: [books-jm7aa, books-f33e2]
---

## Why
`Set_book_status Finished` (`src/Client/Pages/BookDetail/State.fs`) sends `effectiveOn = None`,
and the projection defaults that to the event's append timestamp, which the event store writes as
`DateTimeOffset.UtcNow` — so a finish clicked after roughly 22:00 local (CEST) is dated
*yesterday*. And the hero's `finished {date}` line is plain text: a wrong date (a late click, a
prior dated to the import day) cannot be corrected without the event browser. The builder wants a
manual finish to mean "today" and the displayed date to be editable with a date picker (ADR-0082).

## What
1. `src/Client/Pages/BookDetail/State.fs`: in `Set_book_status status`, when `status =
   BookStatus.Finished` send `Some (today ())` (the module's existing local-date helper) instead of
   `None`; other statuses keep `None`.
2. `src/Client/Pages/BookDetail/Views.fs` (hero, the `finished {finishedAt}` paragraph): make the
   date click-to-edit with `Components/EditableDateInput.fs`, following the pattern
   `MovieDetail/Views.fs` already uses for the watch-session date (`EditableDateInput.EditableDateInput
   initial extraClasses onCommit onCancel`). Keep the `font-mono` finished line as the resting
   state; the input replaces it while editing.
3. `Types.fs` / `State.fs`: add `IsEditingFinishedDate: bool`, and messages `Edit_finished_date`,
   `Cancel_edit_finished_date`, `Commit_finished_date of string`; commit dispatches
   `api.setBookStatus model.Slug BookStatus.Finished (Some picked)` (ADR-0077 §4: re-dating an
   already-Finished book is a legitimate event) and reloads the book on success; Escape / unchanged
   blur cancels with no API call.
4. Edge validation: `Api.fs`'s `setBookStatus` rejects a future `effectiveOn` with a clear message
   (ADR-0077 §6 puts date-range validation at the edge, not in `decide`); the client shows the error
   through the page's existing `Error` slot.
5. Client unit tests (Vitest, ADR-0064) for the `update` transitions; an Expecto test for the
   future-date rejection.

## Acceptance criteria
- [ ] `update (Set_book_status Finished)` issues `setBookStatus slug Finished (Some today)` where `today` is the local `yyyy-MM-dd` date (Vitest, stubbed api).
- [ ] `update (Set_book_status Backlog | InFocus | Abandoned)` still issues `effectiveOn = None`.
- [ ] `Commit_finished_date "2026-03-10"` issues `setBookStatus slug Finished (Some "2026-03-10")` and, on `Ok`, reloads the book; `Cancel_edit_finished_date` clears `IsEditingFinishedDate` with no api call.
- [ ] `setBookStatus` with a future `effectiveOn` returns `Error` naming the reason; today and past dates are accepted (Expecto).
- [ ] Re-committing the currently displayed date appends zero events (ADR-0077 §4 no-op).
- [ ] `npm run build`, Expecto and Vitest are green.
- [ ] Clicking the finished date opens a native date picker in place; Enter or blur commits, Escape reverts, and the hero shows the new date after commit. [human-eye]

## Notes
- Independent of `books-d4wtc` / `integration-dtdbb`: it fixes the manual path and the edit
  affordance whatever produced the current date.
- Frontend gate: `design-system-001-formalize-styleguide` (done). Paper-overlay rules don't apply
  (inline input, no floating surface); reuse `EditableDateInput`'s `input-xs`/`input-sm` sizing.
- ADR-0082 records the "manual finish = today's local date" decision alongside the prior event.
