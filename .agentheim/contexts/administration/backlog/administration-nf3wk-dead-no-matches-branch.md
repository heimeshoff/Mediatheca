---
id: administration-nf3wk
title: "Event Browser's \"No matches\" pagination-bar text is dead code — the empty-events branch always wins first"
status: backlog
type: bugfix
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [event-store, admin-console, ui, bug]
related_adrs: [0020, 0023]
related_research: []
prior_art: [administration-mtf1f]
---

## Why
Discovered while writing administration-a4d9b's committed Playwright specs
for ADR-0023's filter-respecting live-tail behavior. Applying a filter that
matches zero events was expected (and asserted, per the task's acceptance
criteria referencing the paginationBar's `"No matches"` string) to render
`"No matches"`, but the real rendered DOM shows `"No events found."`
instead. Confirmed via the browser (Playwright DOM snapshot), not by
reading the source in isolation.

## What
`src/Client/Pages/EventBrowser/Views.fs`'s `view` function:

```fsharp
if model.IsLoading then <spinner>
else if List.isEmpty model.Events then <"No events found.">
else
    <event rows>
    paginationBar model dispatch   // contains "No matches" | "Showing X-Y of Z"
```

`paginationBar` (which is where the `"No matches"` vs. `"Showing {firstShown}-
{lastShown} of {model.TotalMatches}"` branch lives) is only ever reached in
the `else` arm, i.e. only when `model.Events` is *not* empty. But
`model.TotalMatches = 0` (the condition `paginationBar` checks for
`"No matches"`) can only happen when there are zero matching events, which
also means `model.Events` is empty — so the `List.isEmpty model.Events`
branch above always wins first and renders `"No events found."` instead.
`paginationBar`'s `"No matches"` string is therefore unreachable dead code
today.

This likely predates ADR-0023 — the empty-state check looks like it was
added for the general zero-results case (administration-g5dfy /
ADR-0020's pagination work), and the live-tail Follow feature
(administration-mtf1f) inherited the same view structure without touching
it, so it isn't this task's live-tail logic that's wrong; it's the
zero-results empty-state precedence.

## Acceptance criteria
- [ ] Decide and implement a single consistent zero-results empty state (either
      remove the dead `"No matches"` string from `paginationBar` and keep
      `"No events found."` as the one true empty-state message, or restructure
      `view` so `paginationBar` — and its `"No matches"` text — is reachable
      when filters yield zero results).
- [ ] A test (Expecto `update`-level or a Playwright DOM assertion) asserts the
      chosen behavior so this can't silently regress again.
- [ ] `npm run build` and the Expecto suite stay green.

## Notes
Filed per administration-a4d9b's rule to file discrepancies against
administration-mtf1f rather than silently patch them in a testing-only task.
The a4d9b Playwright spec was adjusted to assert the actual current text
(`"No events found."`) rather than the aspirational-but-unreachable
`"No matches"`, so it stays green regardless of when/whether this gets fixed.
