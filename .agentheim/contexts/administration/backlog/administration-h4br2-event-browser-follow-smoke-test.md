---
id: administration-h4br2
title: Browser smoke-test the Events tab Follow toggle end-to-end
status: backlog
type: chore
context: administration
created: 2026-07-20
completed:
depends_on: [administration-mtf1f]
blocks: []
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
- Start the dev server, open `/admin/events`, turn Follow on.
- In a second tab/action, append an event (e.g. rate a movie) and confirm the
  row appears within ~2-3s without a page reload, with the arrival highlight.
- Change a filter while following; confirm subsequent live rows respect it.
- Turn Follow off (or navigate to Projections tab, or paginate to page 2) and
  watch the Network panel for ~10s to confirm no further `getEventsAfter`
  requests fire.

## Acceptance criteria
- [ ] All three of administration-mtf1f's acceptance criteria are confirmed via live browser interaction, not just code review.
- [ ] Any discrepancy found is filed as a new backlog/todo item against administration-mtf1f's design (do not silently patch around it here).

## Notes
Use `chrome-devtools-mcp` per CLAUDE.md's MCP Servers section. This is a
verification-only task — no production code changes expected unless the smoke
test uncovers a real bug.

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
