---
id: administration-da908
title: Prove a Playwright harness can drive the full Mediatheca stack and observe network traffic
status: todo
type: spike
context: administration
created: 2026-07-21
completed:
depends_on: [administration-h4br2]
blocks: [administration-a4d9b]
tags: [admin-console, event-store, live, testing, e2e]
related_adrs: [0023]
related_research: []
prior_art: [administration-mtf1f]
---

## Why
The Events-tab Follow (live-tail) toggle's most behavior-sensitive paths —
live-append arrival, filter-respecting live rows, and *no orphan
`getEventsAfter` polling after teardown* (especially the navigate-away path
fixed only by static review at administration-mtf1f iteration 2) — are asserted
today by code review plus, once administration-h4br2 runs, a one-time
agent-driven smoke pass. Neither is durable: nothing in the repo re-checks these
paths on change.

Standing up durable coverage means introducing the project's **first
browser/e2e test harness**. The repo has no client-side (Elmish `update`) test
harness and no e2e harness at all today: `tests/Server.Tests` (Expecto) only
references `Server.fsproj`/`Shared.fsproj`, and a standalone `dotnet build` of
`Client.fsproj` currently fails on pre-existing Feliz version-resolution
mismatches. Before writing the actual assertions (administration-a4d9b), the
unknowns are all about the *harness*, not the *behavior* — so this spike proves
the harness can drive the running stack and see its traffic, keeping
assertion-writing decoupled from infrastructure risk and giving a clean revert
boundary if Playwright proves flaky here.

## What
Introduce **Playwright Test** (`@playwright/test`) as the project's e2e harness
and prove it can:
- start and reliably tear down the full dev stack (`npm start`, :5000 + :5173)
  via Playwright's `webServer` config, on Windows dev;
- run each test against a fresh, isolated `DATA_DIR` (temp dir, injected via
  `webServer.env`), never touching the real dev DB at `~/app/mediatheca/`;
- trigger an "event appended elsewhere" via a **direct Fable.Remoting API call**
  (e.g. a rating command) — exercising the real server-append → projection →
  live-tail path, not a raw event-store write and not UI clicking;
- observe the resulting HTTP traffic (`page.on('request')`) enough to assert on
  `getEventsAfter` requests.

Playwright chosen over scripting `chrome-devtools-mcp` (agent-driven, not
committed/repeatable) and over Cypress/WebdriverIO (weaker for the negative,
time-windowed network assertions the orphan-poll check needs). See the ADR note
below.

## Acceptance criteria
- [ ] `@playwright/test` installed as a root devDependency; `playwright.config.ts` + `tests/e2e/` created; a `test:e2e` npm script added; `npx playwright install chromium` documented as a required setup step.
- [ ] `webServer` config starts the full dev stack (`npm start`, :5000/:5173) with a readiness wait **and reliably tears it down on Windows** — including verifying `dotnet watch`'s child-process tree actually dies; if it doesn't, switch the test-only server invocation to `dotnet run` (non-watch).
- [ ] Each run uses a fresh temp `DATA_DIR` (injected via `webServer.env`), deleted on teardown; the real dev DB at `~/app/mediatheca/` is provably never touched.
- [ ] One smoke spec: opens `/admin` Events tab, turns Follow on, triggers a single event via a direct API call (seeding a movie first if the empty store requires it), asserts the row appears in the DOM, and asserts the `getEventsAfter` request is visible in the Playwright request log.
- [ ] Documents concretely whether `getEventsAfter` traffic is observable via the `:5173` vite-proxied origin or must be watched on `:5000` directly (carry the answer into administration-a4d9b).
- [ ] `npm run build` and the existing Expecto suite remain green — the harness must not perturb the existing build/test pipeline.

## Notes
**Open unknowns to resolve during the spike (do not guess now):**
- Does `webServer` cleanly kill `dotnet watch`'s process tree on Windows, or is a non-watch `dotnet run` server variant needed for test runs?
- Is there an existing API method to seed a single movie into an empty store, or does a fixture/import path need adding first?
- Does `getEventsAfter` traverse the vite `/api` proxy in a form observable on :5173, or must the test watch :5000 directly?

**Stop-loss (spike):** if, mid-spike, the mitigation is already known and cheap,
record it and stop. Concretely — if the harness proves out trivially, or one of
the open unknowns resolves to an obviously-cheap fix (e.g. `dotnet run` non-watch
is plainly the answer, or `getEventsAfter` is plainly observable on :5173), write
the finding into the ADR and end the spike rather than exhaustively hardening all
six acceptance criteria. The spike exists to retire harness risk, not to gold-
plate the harness.

**Expected ADR output:** working this spike (with the follow-on feature) should
produce a `scope: global` ADR — *Playwright e2e harness* — a first-of-its-kind,
project-wide test-infrastructure choice. Use the **next free ADR number at
authoring time** (as of 2026-07-22 that is **0027** — the originally pre-assigned
0025 was consumed by administration-xx3mw and 0026 by administration-yamm5, so
confirm the max on disk before minting rather than trusting a stale reservation).
The ADR should record: the Playwright choice and why not the
alternatives; the `webServer` dev-stack lifecycle + the `dotnet watch` teardown
caveat; per-run temp `DATA_DIR` isolation; the direct-API-call event-triggering
convention; the "no CI in this repo today, `reuseExistingServer: !CI`, designed
to be CI-addable later" stance; the boundary vs. the Vitest/Fable unit-test
skill; and the spike-then-feature split as the precedent pattern for future
e2e-worthy work.

Decomposition and harness choice shaped via the orchestrator (architect) during
the administration-h4br2 refinement, 2026-07-21.
