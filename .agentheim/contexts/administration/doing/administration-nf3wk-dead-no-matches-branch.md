---
id: administration-nf3wk
title: "Event Browser's \"No matches\" pagination-bar text is dead code — give the filter-empty state its own message instead"
status: doing
type: bugfix
context: administration
created: 2026-07-22
completed:
depends_on: [design-system-001]
blocks: []
tags: [event-store, admin-console, ui, bug]
related_adrs: [0020, 0023]
related_research: []
prior_art: [administration-mtf1f, administration-g5dfy, administration-a4d9b]
---

## Why
Discovered while writing administration-a4d9b's committed Playwright specs
for ADR-0023's filter-respecting live-tail behavior. Applying a filter that
matches zero events was expected (and asserted, per that task's acceptance
criteria referencing the paginationBar's `"No matches"` string) to render
`"No matches"`, but the real rendered DOM shows `"No events found."`
instead. Confirmed via the browser (Playwright DOM snapshot), not by
reading the source in isolation.

The dead `"No matches"` string exists because a *distinct filter-empty
state was intended but never wired*. Rather than just delete the dead code,
we resolve it in the intended direction (builder decision, this refinement):
distinguish "a filter is active and matched nothing" from "the store is
genuinely empty". Today both render `"No events found."`, which is a small
UX lie — an active filter that excludes everything currently reads as an
empty store.

## What
`src/Client/Pages/EventBrowser/Views.fs`'s `view` function:

```fsharp
if model.IsLoading then <spinner>
else if List.isEmpty model.Events then <"No events found.">
else
    <event rows>
    paginationBar model dispatch   // contains "No matches" | "Showing X-Y of Z"
```

`paginationBar` (Views.fs:209) is only ever reached in the `else` arm, i.e.
only when `model.Events` is *not* empty. But `model.TotalMatches = 0` (the
condition `paginationBar` checks for `"No matches"`, Views.fs:226) can only
happen when there are zero matching events, which also means `model.Events`
is empty — so the `List.isEmpty model.Events` branch above always wins
first. `paginationBar`'s `"No matches"` string is therefore unreachable dead
code today.

This predates ADR-0023 — the empty-state check was added for the general
zero-results case (administration-g5dfy / ADR-0020's pagination work), and
the live-tail Follow feature (administration-mtf1f) inherited the same view
structure without touching it. So it isn't this task's live-tail logic
that's wrong; it's the zero-results empty-state precedence.

**The fix (chosen direction — distinct filter-empty state):**
1. Remove the unreachable `"No matches"` branch from `paginationBar` — it
   now always renders `"Showing {firstShown}-{lastShown} of {model.TotalMatches}"`,
   since it only ever renders when `model.Events` is non-empty (so
   `TotalMatches > 0` there). The `if model.TotalMatches = 0 then 0`
   guard in `firstShown` becomes moot in the same way — the worker may drop
   it or leave it as harmless defense; not load-bearing either way.
2. Make the `view` empty arm (`List.isEmpty model.Events`) pick its message
   by whether any filter is active. All filter fields on `Model` are strings
   defaulting to `""` — a filter is active iff any of `Search`,
   `StreamFilter`, `EventTypeFilter`, `BoundedContextFilter`,
   `TimestampFrom`, `TimestampTo` is non-empty. Active → render
   `"No matches for the current filters."`; otherwise → `"No events found."`.

Suggested shape (worker's discretion): extract the decision into a single
pure function in `EventBrowser/State.fs` so the copy lives in one place and
the intent is explicit, e.g.

```fsharp
let anyFilterActive (m: Model) =
    [ m.Search; m.StreamFilter; m.EventTypeFilter
      m.BoundedContextFilter; m.TimestampFrom; m.TimestampTo ]
    |> List.exists (System.String.IsNullOrEmpty >> not)

let emptyStateMessage (m: Model) =
    if anyFilterActive m then "No matches for the current filters."
    else "No events found."
```

## Acceptance criteria
- [ ] `paginationBar`'s unreachable `"No matches"` branch is removed; it
      renders only `"Showing {firstShown}-{lastShown} of {model.TotalMatches}"`.
- [ ] The `view` empty arm renders `"No matches for the current filters."`
      when any filter (`Search` / `StreamFilter` / `EventTypeFilter` /
      `BoundedContextFilter` / `TimestampFrom` / `TimestampTo`) is non-empty,
      and `"No events found."` when none is — i.e. an empty store still reads
      `"No events found."`, a zero-match filter now reads the filter message.
- [ ] The existing Playwright spec `tests/e2e/event-tail-follow.spec.ts`
      (administration-a4d9b) is updated: its zero-match-filter scenario
      (~line 123, where a search term *is* active) asserts
      `"No matches for the current filters."` instead of `"No events found."`,
      and the now-stale "unreachable dead code" comment (~lines 118-122) is
      corrected. This edit is the machine-checkable regression guard *and*
      keeps a4d9b green under the new behavior.
- [ ] `npm run build` (Fable compile) and the Expecto suite (`npm test`) stay
      green; `npm run test:e2e` passes.

## Notes
- **Copy is pinned for the spec's sake.** The filter-empty string is
  `"No matches for the current filters."` (exactly, matching the acceptance
  criteria and the Playwright assertion). A worker may refine the wording,
  but must keep the spec assertion in lock-step — the whole point of this
  task is that the asserted string and the rendered string agree.
- **Why Playwright, not a unit test:** there is no client-side (Vitest/Fable)
  test harness in this repo — only Expecto (server) and the Playwright e2e
  harness (ADR-0027). The empty-state branch is pure *view* logic, so an
  Expecto `update`-level test can't reach it, and standing up a whole
  Vitest+Fable harness for one message function is overkill. The a4d9b spec
  already exercises exactly this scenario, so updating it is the
  lowest-friction, highest-fidelity, machine-checkable guard. (If a worker
  extracts `emptyStateMessage`/`anyFilterActive` as pure functions and *wants*
  to bootstrap the fable-frontend-tests harness to unit-test them, that's
  welcome but explicitly not required.)
- **Cross-task coupling (do not skip):** `event-tail-follow.spec.ts:114-123`
  fills a search term and then asserts `"No events found."` — with this
  change that assertion goes red unless flipped to the filter message. The
  worker must update it in the same task, or a4d9b's committed spec breaks.
- All acceptance criteria are machine-checkable (ADR-0061) — exact rendered
  strings asserted via Playwright; no `[human-eye]` criteria.
- Filed per administration-a4d9b's rule to file discrepancies against
  administration-mtf1f rather than silently patch them in a testing-only task.
- `depends_on: [design-system-001]` per the BC's frontend styleguide gate
  (README "Frontend gate"); that task is **done**, so the dependency is met
  and non-blocking.
