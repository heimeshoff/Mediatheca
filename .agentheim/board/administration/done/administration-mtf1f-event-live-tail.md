---
id: administration-mtf1f
title: Event explorer live tail — follow mode for incoming events
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-20
depends_on: [administration-g5dfy, design-system-001]
blocks: []
tags: [admin-console, event-store, live]
related_adrs: [0002, 0020, 0023]
related_research: []
prior_art: []
---

## Why
Watching a Steam sync, Jellyfin import, or nightly refresh write events in real time is the fastest way to see what an integration actually does — and to catch it misbehaving. Today the only option is refreshing the browser page.

## What
- A "Follow" toggle on the Events tab. While on, the client polls `IAdminApi` for events with `global_position` greater than the last seen position (reuse `EventStore.readAllForward`), every ~2s, and prepends new rows with a subtle highlight animation.
- Active filters (stream, type, BC, search) apply to tailed events too.
- Toggling off stops polling; navigating away stops polling (Elmish subscription or interval cmd with proper disposal).

## Acceptance criteria
- [x] With Follow on, an event appended by another action (e.g. rating a movie in a second tab) appears in the list within a few seconds without page reload.
- [x] New rows respect the active filters.
- [x] Polling stops when Follow is off or the page is left (no orphan intervals — verify no requests in devtools network log after leaving).

## Notes
Polling is fine for a single-user app — don't build SSE/WebSocket infrastructure for this; the SSE pattern stays reserved for rebuild progress (administration-qjcp4). Keep the poll interval a client-side constant.

See ADR-0023 for the full design rationale (query reuse, epoch-guarded Cmd loop, first-page-only Follow).

## Outcome

Added a Follow toggle to the Events tab's Event Store view. While on, the
client polls `IAdminApi.getEventsAfter` (a new ascending "everything after
global position N matching Filter" query, reusing `EventFilter` verbatim per
ADR-0020's design intent) every 2s and prepends new matching events with a
brief highlight, capped at 200 rows per poll and 200 rows total in the model.

The polling loop is a self-rescheduling Elmish `Cmd` (`Async.Sleep` +
`Cmd.OfAsync`, the same primitives already used elsewhere in this module)
guarded by an epoch counter carried in every `Poll_tail`/`Tail_loaded`
message — not `Elmish.Sub` (no subscription wiring exists anywhere in this
codebase's Elmish setup yet) and not a raw JS timer (exactly the leak the
acceptance criteria exist to catch). Toggling Follow off, or navigating to an
older page (which force-stops Follow), bumps the epoch; any scheduled poll or
in-flight response tagged with a stale epoch is dropped in `update` and, for
`Tail_loaded`, is specifically what stops the loop from rescheduling.

Follow is only available on the newest page — pagination in either direction
always stops it — so live rows are never prepended onto a page of history
someone is reading. Filter changes reload the first page but leave Follow
running, since the tail poll reads filters fresh from the model at fetch
time.

Server side: `EventStore.queryEventPage`'s inline filter-condition building
was extracted into `EventStore.buildFilterConditions`, now shared by both
`queryEventPage` and the new `queryEventsAfter` — the one refactor to
existing code, done specifically so the two queries can't drift out of sync.

Key files:
- `src/Shared/Shared.fs` — `EventTailQuery`, `IAdminApi.getEventsAfter`.
- `src/Server/EventStore.fs` — `buildFilterConditions`, `queryEventsAfter`.
- `src/Server/Administration.fs` — `getEventsAfter`.
- `src/Client/Pages/EventBrowser/Types.fs` / `State.fs` / `Views.fs` — Follow
  toggle, epoch-guarded poll loop, arrival highlight.
- `src/Client/index.css`, `src/Client/DesignSystem.fs` — `highlight-flash`
  keyframe / `animateHighlight`.
- `tests/Server.Tests/EventStoreTests.fs`, `AdministrationTests.fs` — new
  coverage for `queryEventsAfter`/`getEventsAfter` (ascending bound, filter
  reuse, limit cap).
- `.agentheim/knowledge/decisions/0023-event-explorer-live-tail-polling-with-epoch-guarded-cmd.md`

Full suite: 309 tests passing (up from 304). `npm run build` clean.

Verification note: acceptance criteria 1 and 3 (event shows up live; polling
truly stops with no orphan network requests) are asserted by the epoch-guard
design and code review rather than a browser/devtools smoke test — this
worker did not drive the app in a browser. A future task or manual check
could add a Playwright/Chrome-DevTools smoke test if stronger assurance is
wanted; flagged as a backlog candidate below.

### Iteration 2 — navigation-teardown fix

Iteration 1's claim that "navigating to an older page ... force-stops Follow"
covered the page-left half of acceptance criterion 3 was **wrong** — that
mechanism is pagination-away (`Next_page`/`Prev_page` calling
`stopFollowing`), not page navigation. Verification correctly caught that
leaving `/admin` for another page entirely did not stop Follow: `AdminModel`
(and its `Following`/`FollowEpoch`) is retained verbatim by root
`Url_changed`, which only replaces the destination page's own child model,
and `Admin_msg` is routed to `Pages.Admin.State.update` unconditionally with
no `CurrentPage` guard — so a `Poll_tail` scheduled before navigation kept
firing `getEventsAfter` and rescheduling indefinitely after the user left.

Fix: made `EventBrowser.State.stopFollowing` public (was `private`), added a
one-line `Admin.State.stopFollowing` wrapper, and had root `Url_changed`
(`src/Client/State.fs`) call it whenever `prevPage` matches `Admin _` and the
destination `page` does not — reusing the exact epoch-bump mechanism already
proven correct for toggle-off and pagination, rather than inventing a second
teardown path. Bumping the epoch immediately on navigation also closes the
in-flight-request case for free: a `getEventsAfter` response already in
flight when the user leaves arrives later carrying the stale epoch and is
dropped by the existing `Tail_loaded` guard instead of rescheduling. See
ADR-0023's "Navigation teardown" section for the full rationale, including
why a coarser "drop all `Admin_msg` off-page" gate was considered and
rejected.

No client-side test asserts this directly: the project has no client-side
(Elmish `update`) test harness today — `tests/Server.Tests` only references
`Server.fsproj`/`Shared.fsproj`, and a standalone `dotnet build` of
`Client.fsproj` fails on pre-existing `Feliz`/`Feliz.DaisyUI` version-
resolution mismatches unrelated to this fix (confirmed by trying it), so
wiring one up is out of scope for this defect fix. Coverage for this path is
`npm run build` (compiles) + code review; `administration-h4br2` (browser
smoke test, already backlogged) has been updated to exercise the
navigate-away path first specifically because it's the one this iteration
fixed by static review rather than empirical observation.

Additional key files (iteration 2):
- `src/Client/Pages/EventBrowser/State.fs` — `stopFollowing` made public.
- `src/Client/Pages/Admin/State.fs` — `stopFollowing` wrapper.
- `src/Client/State.fs` — `Url_changed` navigation-teardown check.
- `.agentheim/knowledge/decisions/0023-...md` — Decision/Consequences amended
  to correct the false "component unmounts" claim and document the actual
  navigation-teardown mechanism.
- `.agentheim/contexts/administration/README.md` — Follow bullet extended to
  mention navigation-away teardown.
- `.agentheim/contexts/administration/backlog/administration-h4br2-...md` —
  noted the navigation bug found/fixed and told the future smoke test to
  exercise it first.

Full suite re-run at iteration 2: still 309 passing, no regression.
`npm run build` clean.

## Verifier note (iteration 1)

**VERDICT: FAIL** — acceptance criterion 3 is only half satisfied.

**Reasons:**
- "Polling stops when Follow is off or the page is left — no orphan intervals": the
  toggle-off half is correct; the **page-left half is not**. `src/Client/State.fs:385-450`
  (`Url_changed`) replaces only the *destination* page's child model and leaves
  `AdminModel` — and therefore `Following = true` / `FollowEpoch = N` — untouched. No
  navigation path invokes `EventBrowser.State.stopFollowing`, which is reachable only from
  `Toggle_follow`, `Next_page`, `Prev_page`.
- `src/Client/State.fs:578-580` dispatches `Admin_msg` to the Admin page's update with no
  `model.CurrentPage` guard, and `src/Client/Pages/Admin/State.fs:16-18` forwards to
  `EventBrowser.update` unconditionally. A `Poll_tail N` scheduled before navigation still
  matches the retained `FollowEpoch`, passes the guard, issues `api.getEventsAfter`, and
  reschedules via `delayedPoll` — an orphan 2s poll that survives navigation indefinitely.
  It dies only if the user later returns to `/admin` (`Admin.State.init` resets the epoch),
  which is exactly the window the criterion asks about.
- The `## Outcome` claimed navigation-away was covered, but the mechanism it cited
  ("navigating to an older page ... force-stops Follow") is **pagination**, not page
  navigation. Do not conflate the two again.

**Suggested fix:** stop Follow when the Event Browser is no longer the active page — either
bump the epoch on page exit (have root `Url_changed` dispatch a stop/teardown message into
`AdminModel` when navigating away from `Admin`, or re-init `AdminModel` on exit as other
pages are re-inited on entry), or gate the `Admin_msg` branch so tail messages are dropped
when `model.CurrentPage` is not `Admin _`.

**Iteration hint:** likely-fixable.

**Verified sound at iteration 1 (do not redo):** the epoch guard is correct for toggle-off
and for rapid toggle-off-then-on (exactly one live loop, no double-insert); there is no raw
`setTimeout`/`setInterval` anywhere — the loop is pure `Cmd.OfAsync`, correct per ADR-0005;
and the `buildFilterConditions` extraction is genuinely behavior-preserving for
`queryEventPage` (identical condition and param lists, only hoisted) without reformatting
surrounding code.
