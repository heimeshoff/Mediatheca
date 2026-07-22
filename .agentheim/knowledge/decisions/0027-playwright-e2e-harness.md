---
id: 0027
title: Playwright e2e harness
scope: global
status: accepted
date: 2026-07-22
related_tasks: [administration-da908, administration-a4d9b]
---

# ADR 0027: Playwright e2e harness

## Context
The Events-tab Follow (live-tail) toggle's most behavior-sensitive paths —
live-append arrival, filter-respecting live rows, and *no orphan
`getEventsAfter` polling after teardown* (ADR-0023) — were, until now, only
asserted by code review and one-time agent-driven smoke passes. The repo has
no client-side (Elmish `update`) test harness and no e2e harness at all;
`tests/Server.Tests` (Expecto) only exercises `Server.fsproj`/`Shared.fsproj`.
administration-da908 stands up the project's first browser/e2e harness so a
committed spec can drive the real running stack and observe its network
traffic — the infrastructure risk this ADR resolves — before
administration-a4d9b codifies the actual ADR-0023 assertions on top of it.

## Decision — the harness
**Playwright Test (`@playwright/test`)**, chosen over:
- **Scripting `chrome-devtools-mcp`** — agent-driven, not committed or
  repeatable; it's what produced the one-time smoke passes this harness
  replaces with a durable, re-runnable spec.
- **Cypress / WebdriverIO** — weaker fit for the negative, time-windowed
  network assertions administration-a4d9b's orphan-poll check needs (assert
  *zero* further requests over a ~10s window after navigating away) —
  Playwright's `page.on('request')` plus its own auto-waiting model composes
  more naturally with that than either alternative's network-interception
  story.

### Files
- `playwright.config.ts` (repo root) — config, `webServer` (two entries: server + client), temp-`DATA_DIR` isolation, `globalTeardown`.
- `tests/e2e/global-teardown.ts` — best-effort temp-`DATA_DIR` cleanup.
- `tests/e2e/event-tail-follow.smoke.spec.ts` — the one smoke spec this spike required.
- `package.json` — `@playwright/test` devDependency, `test:e2e` script.
- Setup step (not automated): `npx playwright install chromium` once per machine.

### `webServer` dev-stack lifecycle + the `dotnet watch` teardown caveat
**Empirically confirmed** (not assumed): spawning `dotnet watch run` and
killing only the direct child process (exactly what Playwright's `webServer`
teardown does) leaves the `dotnet watch` CLI process and its inner
`dotnet-watch.dll` host **running as orphans** on Windows, even though the
Kestrel server itself dies and its port is freed. Repro: spawn, wait for
"Now listening", kill the spawned child, then `Get-CimInstance
Win32_Process` still shows `dotnet watch run ...` and `dotnet-watch.dll run
...` alive. The same test against plain **`dotnet run` (non-watch)** left no
process behind and freed the port immediately.

**Decision: the harness's server `webServer` entry uses `dotnet run
--project src/Server/Server.fsproj`, not `npm run dev:server` (`dotnet
watch`).** Hot reload has no value for a one-shot test run anyway — this
isn't a loss, just a different command for a different purpose than local
dev. `npm start` / `dotnet watch` remain unchanged for interactive dev.

### New finding: scheduled-job catch-up race crashes a cold-started server
While reproducing the above, running a freshly cold-started server (`dotnet
run`, empty `DATA_DIR`) past ~5s reliably crashed the whole process with an
unhandled `SqliteConnection` thread-safety exception — both scheduled jobs'
5-second "catch-up" timers (`ScheduledJobs.fs`) fire near-simultaneously on
separate ThreadPool threads and both call
`Administration.insertRunningRow` on the *same* shared `SqliteConnection`,
which is not safe for concurrent command creation/disposal. An unhandled
exception on a background thread is process-fatal in .NET Core, so this
kills the whole server, not just one job run.

This is a **pre-existing production bug**, unrelated to the harness itself,
filed as **administration-tj8n2** for a real fix (give the job-run recorder
its own connection, or a lock, or similar). The harness works around it via
an opt-in `MEDIATHECA_DISABLE_SCHEDULED_JOBS=1` env var
(`Composition.fs`), set only in `playwright.config.ts`'s server `webServer`
entry — unset (every normal dev/Docker run) is untouched. e2e runs don't
exercise scheduled jobs, so skipping their startup entirely is a clean,
harness-scoped sidestep rather than a fix.

### Per-run temp `DATA_DIR` isolation
`playwright.config.ts` computes a fresh `fs.mkdtempSync` temp directory once
per config load (before any `webServer` starts) and injects it via the
server `webServer` entry's `env.DATA_DIR` — the same mechanism
`Composition.fs`'s `DataDir.resolveDefault` already honors, so no server
code needed to change. `tests/e2e/global-teardown.ts` removes it after the
run, **best-effort**: on Windows the just-exited process's SQLite file (plus
WAL/SHM sidecars) stayed transiently locked for up to ~10-20s during this
spike's dry runs (plausibly Defender/Search Indexer scanning newly-touched
temp files, not the already-exited .NET process) — a bare `fs.rmSync` raced
that and threw `EPERM`. The teardown retries for a bounded budget, then
**warns and moves on** rather than failing the whole run: the leftover
directory lives under the OS temp dir, never shadows the real dev DB, and
the OS reclaims it on its own schedule regardless.

**Caveat — `reuseExistingServer` trades isolation for convenience.** Per the
"no CI today" stance below, `reuseExistingServer: !process.env.CI` means: if
a developer already has `npm start` running locally when they run `npm run
test:e2e`, Playwright reuses that already-bound port instead of spawning an
isolated instance — so the specs run against the **real dev `DATA_DIR`**,
not a temp one. This is why the smoke spec's only side effect (`addFriend`)
is deliberately additive and harmless rather than destructive: it's a
reasonable one-off write even against real personal data, given the
convenience win. A guaranteed-isolated run means stopping `npm start` first
(or setting `CI=1` to force a cold start). Confirmed empirically: the real
dev DB's `mediatheca.db` mtime was unchanged (predates the spike) across
every cold-start ( `reuseExistingServer` not in play) run performed here.

### The direct-API-call event-triggering convention
"Event appended elsewhere" is triggered via a **direct HTTP call to the real
Fable.Remoting endpoint**, not a raw event-store write and not UI clicking —
exercising the real server-append → projection → live-tail path.
Empirically confirmed wire protocol: `POST /api/{TypeName}/{Method}` with
the JSON body being **an array of the method's arguments**, even for a
single argument (`["some string"]`, not `"some string"` — the server
rejects the latter with a clear "expected N argument(s)... in the form of a
JSON array" error). Response for a `Result<string, string>`-returning method
is `{"Ok": "..."}` / `{"Error": "..."}`.

**Chose `IMediathecaApi.addFriend` over `addMovie` or seeding a movie.**
`addMovie` calls out to the real TMDB API (network + API key dependency) —
unsuitable for a hermetic harness. `addFriend` needs no pre-existing entity
and no external network, so **no seeding was needed** for this spike's
happy path (the task's acceptance criteria anticipated seeding might be
required; empirically it isn't, for this specific trigger). Route:
`POST /api/IMediathecaApi/addFriend`, body `["<name>"]`.

### `getEventsAfter` traffic: observable via the `:5173` proxy
**Confirmed observable on the vite-proxied `:5173` origin**, not just
`:5000` directly — the client's Fable.Remoting proxies use relative paths
(`Remoting.createApi()` with no `withBaseUrl`), so requests go to the same
origin the page was loaded from, and vite's `/api` proxy (`vite.config.mts`)
forwards them to `:5000`. `page.on('request')` on a page navigated to
`:5173` sees `/api/admin/getEventsAfter` directly — no need to watch `:5000`
separately. **This is the answer administration-a4d9b should carry
forward**: point Playwright at `:5173` (the `baseURL` this config already
sets) for both DOM and network assertions.

### No CI today
This repo has no CI pipeline. `reuseExistingServer: !process.env.CI` is the
standard Playwright idiom for "reuse a dev server if I'm a human running
this locally; always cold-start if I'm CI" — `CI` is simply unset today, so
every run behaves like local dev (see the isolation caveat above). The
config needs no changes to become CI-ready later; only a CI job that sets
`CI=1` (or Playwright's own auto-detection of common CI env vars, which
already covers most providers).

### Boundary vs. the (nonexistent) Vitest/Fable unit-test skill
There is no Vitest or client-side unit-test harness in this repo — Elmish
`update` functions are pure and could in principle be unit-tested without a
browser, but nothing does that today. This Playwright harness is
**deliberately end-to-end only**: it drives the real compiled app through a
real browser against a real (isolated) server, and is not a substitute for
a future pure-`update`-function unit-test harness, should one be added. Keep
that boundary if such a harness appears later — e2e specs stay reserved for
paths that need the real network/DOM/timing story (like ADR-0023's
navigate-away teardown), not general Elmish logic coverage.

### Precedent: spike-then-feature split for e2e-worthy work
administration-da908 (this ADR) proved the harness in isolation, deferring
the actual ADR-0023 assertions to administration-a4d9b. This split —
retire *infrastructure* risk in a narrowly-scoped spike, then write the
*behavioral* assertions as a separate, normally-scoped feature task once the
harness is known-good — is the intended pattern for any future e2e-worthy
work in this repo: don't let harness uncertainty (webServer lifecycle,
process teardown, network observability) block or bloat the task that
actually wants to assert a behavior.

## Consequences
- First project-wide test-infrastructure dependency beyond Expecto:
  `@playwright/test` (root devDependency) + a one-time `npx playwright
  install chromium` per machine.
- `npm run build` and the Expecto suite (`npm test`) are unaffected —
  confirmed green after this change.
- administration-tj8n2 (scheduled-job connection race) is a real bug found
  as a side effect and must eventually be fixed on its own merits; the
  `MEDIATHECA_DISABLE_SCHEDULED_JOBS` env var is a harness accommodation,
  not a substitute for that fix.
- `reuseExistingServer`'s real-DB-reuse caveat should be re-read by anyone
  writing a *destructive* e2e spec later (administration-a4d9b's specs stay
  additive/read-only for this same reason, per its own acceptance criteria).

## References
- `playwright.config.ts`, `tests/e2e/global-teardown.ts`, `tests/e2e/event-tail-follow.smoke.spec.ts`
- `src/Server/Composition.fs` — `MEDIATHECA_DISABLE_SCHEDULED_JOBS` gate.
- `src/Server/ScheduledJobs.fs`, `src/Server/Administration.fs` (`insertRunningRow`) — the race behind administration-tj8n2.
- ADR-0023 — the behavior this harness exists to eventually protect.
- administration-tj8n2 — the scheduled-job connection race bug, filed as a follow-up.
- administration-a4d9b — the follow-on feature task that consumes this harness.
