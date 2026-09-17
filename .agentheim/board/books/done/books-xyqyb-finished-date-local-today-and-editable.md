---
id: books-xyqyb
title: Manual finish stamps today's local date and the hero's finished-date line is click-to-edit — `Set_book_status Finished` sends `Some localToday` instead of `None` (no more UTC-midnight drift), and the `finished {date}` line opens an `EditableDateInput` whose commit re-dates the finish via `setBookStatus slug Finished (Some picked)`
status: done
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

## Outcome

`Set_book_status Finished` (`src/Client/Pages/BookDetail/State.fs`) now sends `effectiveOn = Some (today ())` (the module's existing local-date helper) instead of `None`, so a manual finish is stamped with today's local date rather than drifting to the event store's UTC append day for late-evening clicks (ADR-0082 §8). Other statuses (`Backlog`/`InFocus`/`Abandoned`) are unchanged and still send `None`.

The hero's `finished {date}` paragraph (`src/Client/Pages/BookDetail/Views.fs`) is now click-to-edit: clicking it dispatches `Edit_finished_date`, which swaps the `font-mono` paragraph for `Components/EditableDateInput.fs` (the same pattern `MovieDetail/Views.fs` uses for watch-session dates, `input-xs w-36` sizing). Committing dispatches `Commit_finished_date picked`, which calls `api.setBookStatus model.Slug BookStatus.Finished (Some picked)` (ADR-0077 §4: re-dating an already-Finished book is a legitimate event) and reuses the existing `Status_result` handling, which reloads the book on `Ok`. Escape/unchanged blur (handled entirely inside `EditableDateInput`) dispatches `Cancel_edit_finished_date`, which only clears `IsEditingFinishedDate` — no API call. `Model.IsEditingFinishedDate: bool` and the three new messages (`Edit_finished_date`, `Cancel_edit_finished_date`, `Commit_finished_date of string`) were added to `Types.fs`.

`src/Server/Api.fs`'s `setBookStatus` rejects a future `effectiveOn` (string-compared against today's local `yyyy-MM-dd`, same edge-validation posture ADR-0077 §6 describes) before the command ever reaches `Books.decide`, returning `Error "effectiveOn cannot be in the future"`; today and past dates pass through to the domain unchanged. No codebase-wide future-date helper existed prior to this task — the conductor's note that `observeReadingProgress` already had one did not hold on inspection (`grep -n future` found nothing anywhere in `Api.fs`/`Books.fs`); this is the first instance of that validation, added narrowly to `setBookStatus` only, per the task's explicit scope.

The no-op acceptance criterion ("re-committing the currently displayed date appends zero events") is already covered by `Books.decide`'s existing no-op logic and pre-existing domain tests in `tests/Server.Tests/BooksTests.fs` (e.g. "Repeating the identical `Change_status` command appends zero events") — no new test was needed for that criterion since ADR-0077's original task already exercises it at the domain level.

Tests added:
- `src/Client/Pages/BookDetail/State.test.fs` (Vitest/Fable.Mocha, 5 new tests in a `finishedDateTests` list, wired into the module's `Mocha.runTests`): `Set_book_status Finished` sends `Some <today>`; `Backlog`/`InFocus`/`Abandoned` still send `None`; `Commit_finished_date` issues `setBookStatus ... Finished (Some picked)` and reloads via `getBook` on `Ok`; `Cancel_edit_finished_date` and `Edit_finished_date` toggle `IsEditingFinishedDate` with no api call (`Unchecked.defaultof<IMediathecaApi>` stand-in, per the sibling-worker pattern already in this file).
- `tests/Server.Tests/BooksApiTests.fs` (Expecto, 1 new test): `setBookStatus` rejects tomorrow's date, accepts today's, accepts yesterday's, via a real `Api.create` round trip.

Left to the builder (per the conductor's note, cannot be verified by a worker): the `[human-eye]` acceptance criterion — that clicking the finished date opens a native date picker in place, Enter/blur commits, Escape reverts, and the hero visually shows the new date after commit. This is covered mechanically by the Vitest reducer tests above and by `EditableDateInput`'s own established behaviour (already relied on by `MovieDetail`), but the actual DOM/picker interaction was not exercised in a browser.

Gates run from the worktree, all green: `npm run build` (Fable compile), `npm run test:client` (118 passed, up from the 113-test baseline — +5 here), `npm test` (911 passed, up from the 910-test baseline — +1 here).

No new ADR: ADR-0082 §8 already records the "manual finish = today's local date, hero date click-to-edit" decision, per the conductor's note.
