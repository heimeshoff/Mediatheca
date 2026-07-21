---
id: administration-h4br2
title: Browser smoke-test the Events tab Follow toggle end-to-end
status: done
type: chore
context: administration
created: 2026-07-20
completed: 2026-07-21
depends_on: [administration-mtf1f]
blocks: [administration-da908]
tags: [admin-console, event-store, live, testing]
related_adrs: [0023]
related_research: []
prior_art: []
---

## Why
administration-mtf1f (event explorer live tail / Follow toggle) landed with
server-side test coverage for the new `getEventsAfter` query and a clean
`npm run build`, but its two most behavior-sensitive acceptance criteria — "a
live event appears without reload" and "polling truly stops, no orphan
requests" — were verified by design/code review (the epoch-guard mechanism)
rather than by driving the app in a real browser. The codebase has Chrome
DevTools MCP tooling available for exactly this kind of check (see
CLAUDE.md's MCP Servers section) but this worker didn't have a running server
to point it at.

## What
A `work` worker drives this hands-off via the `chrome-devtools-mcp` tools
(CLAUDE.md's MCP Servers section) against a stack the worker starts itself.
Exercise the **navigate-away teardown case first and explicitly** — it's the one
path never empirically verified (fixed by static review at mtf1f iteration 2) —
then the rest:
- Start the dev server (`npm start`), open `/admin/events`, turn Follow on.
- **First:** with Follow on, navigate to another page entirely (Dashboard /
  Movies) via client-side nav and watch the Network panel for ~10s — confirm no
  further `getEventsAfter` requests fire.
- Turn Follow on again; append an event via a direct API call / second action
  (e.g. rate a movie) and confirm the row appears within ~2-3s without reload,
  with the arrival highlight.
- Change a filter while following; confirm subsequent live rows respect it.
- Toggle Follow off, and separately paginate to page 2 — after each, watch the
  Network panel for ~10s to confirm no further `getEventsAfter` requests fire.

## Acceptance criteria
mtf1f's three behaviors (ADR-0023), each confirmed via live browser interaction
in a real running app, not code review:
- [x] **Teardown — no orphan polling (all three sub-cases):** after navigating away from `/admin`, after toggling Follow off, and after paginating away from page 1, no further `getEventsAfter` requests fire over a ~10s window. The **navigate-away** sub-case is exercised first and is the load-bearing one.
- [x] **Live arrival:** with Follow on, an event appended by another action appears within ~2-3s without page reload, with the arrival highlight.
- [x] **Filter-respecting live rows:** with a filter active, subsequent live rows respect it (a matching event arrives; a non-matching one does not).
- [x] Any discrepancy found is filed as a new backlog/todo item against administration-mtf1f's design (do not silently patch around it here). — No discrepancies found; nothing to file.

## Notes
Executed by a `work` worker via `chrome-devtools-mcp` (requires Chrome running
and the MCP server reachable). This is a verification-only task — no production
code changes expected unless the smoke test uncovers a real bug.

**Durable follow-on (this is the one-shot pass; the repeatable coverage is
separate):** administration-da908 (spike — prove a committed Playwright e2e
harness can drive the stack) and administration-a4d9b (feature — codify these
three behaviors as committed Playwright specs) both depend on this task, so the
exact flows/selectors are confirmed known-good here before they're written into
repeatable specs.

**Update (administration-mtf1f iteration 2):** a static-review pass at
verification caught a real bug on the navigation-away path — toggling Follow
off worked, but leaving `/admin` for another page did not stop the poll loop
(`AdminModel` was retained verbatim across `Url_changed`, so a scheduled
`Poll_tail`/in-flight `getEventsAfter` response kept rescheduling
indefinitely after the user left). Fixed at iteration 2 by bumping the Follow
epoch on `AdminModel` from root `State.Url_changed` when navigating away from
`Admin _` (see ADR-0023's "Navigation teardown" section). This was caught by
code review, not by driving the app — when this smoke test is eventually run,
exercise the navigate-away case *first and explicitly* (turn Follow on, click
to Dashboard or Movies, watch the Network panel for ~10s) before the other
scenarios, since it's the one path that was empirically unverified until now.

## Outcome (2026-07-21 — conductor-run browser smoke test)

Executed directly by the `work` conductor session via `chrome-devtools-mcp` (the
`agentheim:worker` subagent type has no chrome-devtools MCP tools, so this could
not be dispatched to a worker — builder chose conductor-run). Stack started with
`npm start` against an **isolated temp `DATA_DIR`** — a copy of the production DB
— so the live-arrival event appends never touched the real library (prod
`mediatheca.db` mtime confirmed unchanged afterward). All measurements used
precise client-side timing (`performance.getEntriesByType('resource')` filtered to
`getEventsAfter`, with `clearResourceTimings()` between tests to avoid the 250-entry
buffer cap).

**All three ADR-0023 behaviors confirmed in a real running browser — PASS:**

- **Teardown — no orphan polling (all three sub-cases):**
  - **Navigate away (load-bearing, previously unverified):** with Follow on,
    client-side nav (`Url_changed`) to Dashboard → **0** `getEventsAfter` requests
    started after the navigation across a ~12s window; last poll fired 568 ms
    *before* the nav. The epoch-bump fix from mtf1f iteration 2 works.
  - **Toggle Follow off:** **0** polls after toggle-off over ~12s; button reverted
    `Following → Follow`.
  - **Paginate away from page 1:** clicking **Next** force-stopped Follow (button
    reverted to `Follow`) and produced **0** polls after pagination over ~12s.
- **Live arrival:** appending a `Personal_rating_set` event (via a direct
  `setPersonalRating` API call, so the tab never left `/admin/events`) surfaced the
  new row at the top in **~1–2 s** with no page reload. The `animate-highlight`
  class (`DesignSystem.animateHighlight`, on the outer `EventBrowser` row element)
  was confirmed applied to the arrived row (`.animate-highlight` element present,
  exactly one at a time).
- **Filter-respecting live rows:** with the stream filter set to
  `Movie-10-cloverfield-lane-2016`, a **matching** rating event (same stream)
  arrived at the top; a **non-matching** rating event on
  `Movie-28-years-later-2025` — verified appended to the event store as global
  position 17599 — did **not** leak into the filtered live tail across ~5 s of
  polling. Genuine server-side filter application on the `getEventsAfter` query,
  not a dropped write.

**Discrepancies against mtf1f's design:** none. All three behaviors match ADR-0023.

**Incidental (out of scope — not an mtf1f/Follow issue, not filed against this
task):** the browser console logs a React `validateDOMNesting` warning (`<a>`
cannot be a descendant of `<a>`) originating on the **Dashboard** nav, unrelated to
the event browser. Also, 404s for images were expected — the isolated `DATA_DIR`
copy deliberately omitted the `images/` cache. Neither affects the Follow toggle.

**Durable follow-ons (already in backlog, unchanged):** administration-da908
(Playwright harness spike) and administration-a4d9b (commit these three behaviors as
Playwright specs) — the exact flows and selectors are now confirmed known-good here.
