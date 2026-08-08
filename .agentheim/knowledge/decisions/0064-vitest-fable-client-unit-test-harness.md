---
id: 0064
title: Vitest-through-vite-plugin-fable client unit-test harness
scope: global
status: accepted
date: 2026-08-08
supersedes: []
superseded_by: []
related_tasks: [infrastructure-j7v3c]
related_research: []
---

# ADR 0064: Vitest-through-vite-plugin-fable client unit-test harness

## Context
There was no way to unit-test a pure client-side F# function in this repo. `npm test` is the
Expecto server suite (`tests/Server.Tests`); `npm run test:e2e` is Playwright (ADR-0027) —
nothing covered pure client F# short of a full browser round-trip through the e2e suite.

ADR-0027 named this gap explicitly and left the boundary open:

> There is no Vitest or client-side unit-test harness in this repo — Elmish `update` functions
> are pure and could in principle be unit-tested without a browser, but nothing does that today.
> [...] Keep that boundary if such a harness appears later — e2e specs stay reserved for paths
> that need the real network/DOM/timing story, not general Elmish logic coverage.

Two unrelated bounded contexts (`design-system`, `series`) independently captured near-identical
prose for this harness on the same day, which is itself the evidence it belongs to neither: the
harness would still be wanted even if either requesting BC did not exist (the infrastructure
routing test). It is closer in kind to `infrastructure-p1h9a` / ADR-0037 (the client *build*
gate) than to ADR-0027 — both are standing, always-on quality gates over `src/Client/` with no
single motivating BC, changing on toolchain cadence rather than domain cadence.

The concrete blocked consumer is `NextUp.compute` (`src/Client/Pages/SeriesDetail/NextUp.fs`),
extracted specifically so it could be tested without driving the DOM, and shipped with no
client-side test — see ADR-0063's "Client test coverage deferred" section, which backlogged
exactly this bootstrap as `series-x4qte`.

## Decision
Client-side F# is unit-tested by **Vitest 3** (pinned `^3.2.4`, resolved `3.2.7`) running
`*.test.fs` files through the app's existing `vite.config.mts` Fable plugin instance, with
**Fable.Mocha** as the Expecto-shaped DSL — no second config, no separate Fable compile step.
Test files live under the vite root and must be explicit `<Compile>` items in `Client.fsproj`
after the modules they test, so they are typechecked by `npm run build` while staying out of the
shipped bundle. This harness owns pure client logic (Elmish `update`/`init`, pure helpers) and
complements rather than replaces ADR-0027's Playwright e2e harness, whose specs stay reserved for
paths needing the real network/DOM/timing story.

### Why infrastructure owns it
Passes the routing test cleanly (`.agentheim/contexts/infrastructure/README.md`'s "if any one BC
didn't exist, would this change still need to happen?"): yes. It is a standing, always-on quality
gate over `src/Client/` as a whole, not scoped to any single BC's domain language.

### Mechanism
- `vite.config.mts` gains one `test` block on the existing config (not a second config file —
  that would instantiate a second Fable compiler instance against the same fsproj):
  ```ts
  test: {
      include: ["**/*.test.fs"],
      exclude: ["**/fable_modules/**", "**/node_modules/**"],
      globals: true,
      environment: "node",
  }
  ```
- `vitest` (`^3.2.7`, devDependency) and `Fable.Mocha` (`2.17.0`, `PackageReference` in
  `Client.fsproj`) were pre-installed on `main` ahead of this task (commit `d881d11`) rather than
  installed from a worker worktree.
- `package.json` gains `"test:client": "vitest run"` — a new script name, not a rename of the
  existing `test` (Expecto server suite).
- `Client.fsproj` gains `Smoke.test.fs` as an explicit `<Compile>` item immediately before
  `App.fs`, proving the pipeline end-to-end with a domain-free, synthetic arithmetic assertion.
- Documented in `CLAUDE.md`'s Build & Run section.

### Environment default
`environment: "node"` is the default — pure MVU/helper tests need no DOM. `jsdom` is a noted
escape hatch (install `jsdom`, switch `environment` to `"jsdom"`) for a future test file that
transitively imports a module reading `window`/`document` at import time, but is **not adopted**
here — nothing in the client currently requires it, and adding an unused dependency is scope
creep.

### The main-tree-only `npm install` policy
A worker's git worktree has `node_modules/` as a **Windows junction** pointing at the main tree's
real `node_modules/` (ADR-0063's "Scheduling constraint" note). Any `npm install`/`ci`/`update`
run from inside a worktree mutates that shared state outside the worker's isolated scope. New
client-test dependencies (`vitest`, `Fable.Mocha`) are therefore installed **only from the main
tree**, by a builder, before the consuming task is dispatched to a worker worktree — exactly what
happened here (`d881d11`, ahead of this task). Workers confirm presence; they never install.

### Dropped-scope rule: branch-free Feliz view functions
`design-system-fp2wt`'s original capture proposed proving the harness against
`DesignSystem.progressEpisodes` — a `ReactElement`-returning, branch-free view function with no
logic to assert beyond "does it return an element", which would only produce a change-detector
test. That target was dropped rather than forced through a testability refactor; the harness's
own proving step uses a synthetic assertion instead (see below), and view functions of that shape
remain out of scope for this harness generally — they belong to visual/e2e verification (the
StyleGuide page, Playwright), not unit assertion.

### Why the smoke spec is synthetic, not a real app function
Two specialists split on what should prove the pipeline: one proposed `progressEpisodes` (least
domain-loaded real function available); the other concluded that function should not be unit-
tested at all (see the dropped-scope rule above). A synthetic arithmetic assertion
(`2 + 2 = 4`, `src/Client/Smoke.test.fs`) satisfies both — zero domain language from either
requesting BC, and no change-detector test against a branch-free view function.

### Convention enforced structurally, not by prose
`*.test.fs` naming, test files as ordered `<Compile>` items placed after the modules they test,
`test:client` as the client-suite entry point — enforced by a build failure in the same task
rather than by prose alone (ADR-0059): a test file in the wrong compile-order position is an F#
compile error, and `npm run build` (ADR-0037's fail-closed gate) catches it.

## Alternatives considered
- **A second, separate Vitest config file.** Rejected — would instantiate a second Fable compiler
  daemon against the same fsproj; reusing `vite.config.mts` is what makes Vitest run `.fs`
  through the already-configured `fable()` plugin.
- **Assuming Vitest's transform path is identical to the dev-server's.** Not assumed — Vitest
  transforms through **vite-node/SSR**, not the dev-server pipeline `vite-plugin-fable` is
  actually proven under elsewhere in this repo (e.g. `npm run dev:client`). This was this task's
  one real technical risk, and the reason the smoke spec exists as a proving step before any real
  test relies on the pipeline.
- **`jsdom` environment by default.** Rejected — adds a dependency and cost with no current
  consumer; `node` covers the pure-function/pure-`update` use case this harness targets.
- **Prove the pipeline against a real app function (`progressEpisodes`).** Rejected — see
  "Dropped-scope rule" and "synthetic spec" sections above.

## Consequences
- `npm run test:client` (`vitest run`) is now the client unit-test entry point; documented in
  `CLAUDE.md`.
- `series-x4qte` (narrowed to `NextUp.compute`'s frontier-rule assertion) is now unblocked.
- Test files are typechecked by `npm run build` (ADR-0037) but excluded from the shipped bundle
  (nothing imports them).
- Adds `vitest` as a project-wide devDependency alongside `@playwright/test`; both must stay
  paired with the installed `vite-plugin-fable` generation (`0.1.x` → Vite 6 → `vitest@^3`) per
  the `fable-frontend-tests` skill's version matrix — upgrading one without the others is the most
  common way this class of setup breaks.
- Known risk not yet observed: Vitest's vite-node/SSR transform path could in principle diverge
  from the dev-server transform path for some future `.fs` construct even though both share the
  same `fable()` plugin instance. Watch for this if a future client test behaves unexpectedly
  compared to running the same code through `npm run dev:client`.

## Note on ADR numbering
Minted provisionally as ADR-0064 in this worker worktree (0063 was the current head on `main` at
task-start); renumbered at squash-merge integration via `lib/adr-allocation.mjs`'s
`finalizeAdrNumbering` (ADR-0058) if a sibling task's ADR lands first with the same number.
