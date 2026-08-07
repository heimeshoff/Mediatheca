---
id: design-system-fp2wt
title: Stand up unit-test infrastructure for pure Fable/Feliz functions (Vitest-through-vite-plugin-fable or equivalent)
status: backlog
type: chore
context: design-system
created: 2026-08-07
completed:
depends_on: []
blocks: []
tags: [testing, infra, fable, client]
related_adrs: []
related_research: []
prior_art: []
---

## Why

`design-system-mz9v7` added two pure list->segment mapping functions to `DesignSystem.fs`
(`progressEpisodes`, `progressSeasons`) whose correctness is exactly the kind of thing a small,
fast unit test should pin down — e.g. "a `bool list` with a gap in the middle produces gold at
the true indices and brown at the false indices, not a prefix." The task's own Notes section
pointed at `skills/fable-frontend-tests` (a Vitest-through-vite-plugin-fable path) as the
intended home for this kind of test, but that skill does not exist in this repo, and
`package.json` has no `vitest` devDependency or `test:client`-style script — only
`dotnet run --project tests/Server.Tests` (Expecto, server-side) and Playwright e2e
(`test:e2e`). There is currently no way to unit-test a pure client-side F# function short of a
full e2e browser round-trip.

mz9v7 verified `progressEpisodes`/`progressSeasons` by reading the code, running
`npm run build` (Fable compiles cleanly), and inspecting the StyleGuide specimens' rendered
fixture data (which includes a mid-season gap) — this stood in for a unit test, per the
project's "UI tasks where the project has no UI test infrastructure" TDD-skip category, but is
not a substitute for a real regression test.

## What

Stand up a lightweight test runner for pure functions in `src/Client/` — Vitest driven through
`vite-plugin-fable` (matching the stack already in use for the app build) is the most likely
fit, but the exact tool is this task's call to make. Should be narrow: pure-function unit tests
only, not a replacement for the existing Playwright e2e suite. Wire an `npm run test:client` (or
similar) script.

## Acceptance criteria

- [ ] A test runner exists that can execute a unit test against a pure function in
      `src/Client/DesignSystem.fs` (or equivalent) without a browser.
- [ ] `progressEpisodes`/`progressSeasons`/`seriesSeasonEpisodeProgress` (or their successors)
      get at least one regression test each covering the list->segment mapping.
- [ ] A documented `npm run <script>` entry point, referenced from `CLAUDE.md`'s Build & Run
      section.

## Notes

- Not urgent — the primitives it would cover are already build-verified and StyleGuide-reviewed.
  Refine before pulling into a batch.
