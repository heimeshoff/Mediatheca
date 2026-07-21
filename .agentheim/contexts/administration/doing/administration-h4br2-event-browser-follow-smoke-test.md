---
id: administration-h4br2
title: Browser smoke-test the Events tab Follow toggle end-to-end
status: doing
type: chore
context: administration
created: 2026-07-20
completed:
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
- [ ] **Teardown — no orphan polling (all three sub-cases):** after navigating away from `/admin`, after toggling Follow off, and after paginating away from page 1, no further `getEventsAfter` requests fire over a ~10s window. The **navigate-away** sub-case is exercised first and is the load-bearing one.
- [ ] **Live arrival:** with Follow on, an event appended by another action appears within ~2-3s without page reload, with the arrival highlight.
- [ ] **Filter-respecting live rows:** with a filter active, subsequent live rows respect it (a matching event arrives; a non-matching one does not).
- [ ] Any discrepancy found is filed as a new backlog/todo item against administration-mtf1f's design (do not silently patch around it here).

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
