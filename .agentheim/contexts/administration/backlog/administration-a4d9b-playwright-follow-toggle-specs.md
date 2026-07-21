---
id: administration-a4d9b
title: Assert the Events-tab Follow toggle's three live-tail behaviors via committed Playwright specs
status: backlog
type: feature
context: administration
created: 2026-07-21
completed:
depends_on: [administration-da908, administration-h4br2]
blocks: []
tags: [admin-console, event-store, live, testing, e2e]
related_adrs: [0023]
related_research: []
prior_art: [administration-mtf1f]
---

## Why
Once the Playwright harness is proven (administration-da908) and the flows/
selectors are known-good from the one-time smoke pass (administration-h4br2),
codify the Follow toggle's three ADR-0023 behaviors as committed, repeatable
specs — so live-append, filter-respecting rows, and the historically-buggy
navigate-away teardown are asserted by a test on every change, not re-verified
by eye. The navigate-away teardown in particular was fixed only by static review
at administration-mtf1f iteration 2 and has never had an automated guard.

## What
Write committed Playwright specs in `tests/e2e/` that drive the running stack
(via the harness from administration-da908) and assert ADR-0023's three
behaviors 1:1, triggering "event appended elsewhere" via a direct
Fable.Remoting API call.

## Acceptance criteria
- [ ] **Arrival:** with Follow on, an event appended via a direct API call becomes visible within a realistic bounded timeout (e.g. `toBeVisible({ timeout: 4000 })` — not a generous sleep that masks the real ~2-3s cadence), and the arrival-highlight state (confirm the exact class from `EventBrowser/Views.fs` / `index.css`) is present shortly after arrival.
- [ ] **Filter-respecting live rows:** with a filter active (at least one of stream / type / BC / search), an event matching the filter arrives live and one that does NOT match is confirmed absent after the same window.
- [ ] **No orphan polling — all three sub-cases assert zero further `getEventsAfter` requests over a ~10s window** (accumulate via `page.on('request')`, `waitForTimeout(10000)`, assert count unchanged since the action):
  - [ ] (a) Follow toggled off.
  - [ ] (b) User paginates away from page 1.
  - [ ] (c) **Load-bearing:** user navigates away from `/admin` to another page via real client-side navigation (not a full reload) — the path fixed only by static review at administration-mtf1f iteration 2. This is the single most important assertion in the task.
- [ ] All specs pass locally on Windows dev via `npm run test:e2e`; `npm run build` and the Expecto suite remain green.
- [ ] Any discrepancy the specs uncover against ADR-0023's design is filed as a new backlog item against administration-mtf1f rather than silently patched here (mirrors administration-h4br2's rule).

## Notes
Depends on administration-da908 (harness proof — do not start until the harness
can start/stop the stack, isolate the DB, fire an API-triggered event, and see
network traffic) and on administration-h4br2 (the one-time smoke pass that
confirms the exact flows/selectors before they're codified).

ADR-0025 (Playwright e2e harness, `scope: global`) is expected to be authored by
administration-da908; this task consumes that harness rather than re-deciding it.
Shaped via the orchestrator (architect) during the administration-h4br2
refinement, 2026-07-21.
