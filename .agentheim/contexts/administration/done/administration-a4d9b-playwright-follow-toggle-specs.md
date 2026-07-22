---
id: administration-a4d9b
title: Assert the Events-tab Follow toggle's three live-tail behaviors via committed Playwright specs
status: done
type: feature
context: administration
created: 2026-07-21
completed: 2026-07-22
depends_on: [administration-da908, administration-h4br2]
blocks: []
tags: [admin-console, event-store, live, testing, e2e]
related_adrs: [0023, 0027]
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
Write committed Playwright specs in `tests/e2e/` — alongside the proven
`event-tail-follow.smoke.spec.ts` — that drive the running stack (via the
now-shipped ADR-0027 harness) and assert ADR-0023's three behaviors 1:1,
triggering "event appended elsewhere" via a direct Fable.Remoting API call.
The smoke spec is the template: reuse its `page.on('request')` accumulator,
its `POST /api/IMediathecaApi/addFriend` (body `["<name>"]` → `{Ok: slug}`)
hermetic trigger, its `/#/admin/events` hash route, and its arrived-row
locator shape (`Friend-${slug}` + "Friend added").

## Acceptance criteria
- [ ] **Arrival:** with Follow on, an event appended via a direct API call becomes visible within a realistic bounded timeout (e.g. `toBeVisible({ timeout: 4000 })` — not a generous sleep that masks the real ~2s poll cadence, `EventBrowser/State.fs pollIntervalMs`), and the arrived row carries the arrival-highlight class **`animate-highlight`** (`DesignSystem.animateHighlight`; keyed off `NewlyArrived` in `State.fs`, so it stays on the row until the *next* batch arrives — reliably assertable in a quiet test).
- [ ] **Filter-respecting live rows:** with a filter active (at least one of stream / type / BC select, or the `"Search event payloads..."` box), an event matching the filter arrives live and one that does NOT match is confirmed absent after the same window.
- [ ] **No orphan polling — all three sub-cases assert zero further `getEventsAfter` requests over a ~10s window** (accumulate via `page.on('request')` on `/api/admin/getEventsAfter`, observable on the `:5173` baseURL per ADR-0027; `waitForTimeout(10000)`; assert count unchanged since the action):
  - [ ] (a) Follow toggled off (the `/^Following$/` button clicked back to `/^Follow$/`).
  - [ ] (b) User paginates away from page 1 (the `"Prev"`/`"Next"` buttons).
  - [ ] (c) **Load-bearing:** user navigates away from `/admin` to another page via **real client-side navigation** — click an in-app nav/sidebar link (or otherwise trigger Feliz.Router's `Url_changed`), **not** `page.reload()` / a full document load — the path fixed only by static review at administration-mtf1f iteration 2. This is the single most important assertion in the task.
- [ ] **Specs stay additive/read-only** — trigger events only via `addFriend` (the smoke spec's hermetic, non-destructive command), never a destructive command, because `reuseExistingServer: !CI` can run these against the real dev `DATA_DIR` (ADR-0027's isolation caveat).
- [ ] All specs pass locally on Windows dev via `npm run test:e2e`; `npm run build` and the Expecto suite remain green.
- [ ] Any discrepancy the specs uncover against ADR-0023's design is filed as a new backlog item against administration-mtf1f rather than silently patched here (mirrors administration-h4br2's rule).

## Notes
Dependencies are **both `done/`** — the readiness bar is met: administration-da908
shipped the harness (`playwright.config.ts`, `tests/e2e/`, `npm run test:e2e`,
ADR-0027) and administration-h4br2 confirmed the flows/selectors live. This task
consumes that harness rather than re-deciding it.

**Empirically-resolved conventions carried forward from ADR-0027** (do not
re-derive — they were proven, not guessed, in the spike):
- **Trigger:** `POST /api/IMediathecaApi/addFriend`, body `["<name>"]` (Fable.Remoting
  wraps even a single arg in a JSON array), response `{"Ok": "<slug>"}`. Hermetic
  (no seeding, no TMDB), which is also why it satisfies the additive/read-only AC.
- **Network observability:** `getEventsAfter` is visible on the vite-proxied `:5173`
  origin (the config's `baseURL`) — no need to watch `:5000` separately.
- **Selectors** (from `EventBrowser/Views.fs`, cross-checked with the smoke spec):
  Follow toggle button text `Follow` ⇄ `Following`; arrival highlight class
  `animate-highlight`; hash route `/#/admin/events`; search placeholder
  `"Search event payloads..."`; pagination buttons `"Prev"` / `"Next"`.
- **Isolation caveat:** with `npm start` already running, `reuseExistingServer`
  points the specs at the real dev DB — hence the additive/read-only constraint.

All acceptance criteria are machine-checkable (ADR-0061) — the arrival-highlight
check asserts the real `animate-highlight` class toggled by `NewlyArrived`, the
actual mechanism, not an invented perceptual proxy. No `[human-eye]` criteria.

Shaped via the orchestrator (architect) during the administration-h4br2
refinement, 2026-07-21; enriched at refine-time (2026-07-22) once da908 shipped
the harness and resolved the open selector/observability unknowns.

## Outcome

Added `tests/e2e/event-tail-follow.spec.ts` alongside the harness-proving
smoke spec, with five committed specs covering ADR-0023's behaviors 1:1:

- **Arrival** — Follow on, an `addFriend` call lands live within a bounded
  4s window, and the arrived row (scoped via `div.border-b`, the actual
  `eventRow` wrapper, not a bare `div` text-match that could resolve to an
  outer ancestor) carries `animate-highlight`.
- **Filter-respecting live rows** — with a unique search term active, a
  matching live friend arrives; a simultaneously-created non-matching
  friend's name never appears anywhere on the page.
- **No orphan polling**, all three sub-cases, each asserting zero further
  `getEventsAfter` requests over a 10s window after the action: (a) Follow
  toggled off, (b) pagination away from page 1 (forcing `HasMore` via 26
  same-filter events, independent of ambient DB size), and (c)
  **[load-bearing]** real client-side navigation away from `/admin` via the
  sidebar's Dashboard link (Feliz.Router `Url_changed`), not a reload —
  the path fixed only by static review at administration-mtf1f iteration 2.

All 6 e2e specs pass (`npm run test:e2e`), `npm run build` is clean, and the
Expecto suite passes (358/358).

Two discrepancies surfaced and were **not** patched here, per the task's own
rule:
- Concurrent `addFriend` calls (tried via `Promise.all`) intermittently crash
  the server (`SqliteConnection does not support nested transactions` /
  "cannot start a transaction within a transaction") — empirical
  confirmation of the already-tracked **administration-cx92m** (shared
  connection request-concurrency audit). Specs were rewritten to trigger
  events strictly sequentially to route around it.
- The event explorer's zero-filter-match state renders `"No events found."`,
  never `paginationBar`'s own `"No matches"` string — the latter is
  unreachable dead code given `view`'s branch order (`List.isEmpty
  model.Events` always wins first when `TotalMatches = 0`). Filed as new
  backlog item **administration-nf3wk**.

Key files:
- `tests/e2e/event-tail-follow.spec.ts` (new)
- `.agentheim/contexts/administration/backlog/administration-nf3wk-dead-no-matches-branch.md` (new)
- `.agentheim/contexts/administration/README.md` (updated: Playwright
  harness bullet)
