---
id: series-x4qte
title: Bootstrap Fable/Vitest client-side unit test infrastructure (blocked on npm install being safe to run)
status: backlog
type: chore
context: series
created: 2026-08-07
completed:
depends_on: []
blocks: []
tags: [testing, vitest, fable, client, infra]
related_adrs: []
related_research: []
prior_art: []
---

## Why

series-k4zpn ("Next Up must follow the furthest-watched episode") extracted a
pure, testable `SeasonDto list -> (int * EpisodeDto) option` function
(`src/Client/Pages/SeriesDetail/NextUp.fs`) specifically so both client
acceptance criteria — the hero Next Up card and the Episodes-tab badge — could
be asserted without driving the DOM, per the task's own Notes and the
`fable-frontend-tests` skill it pointed at.

No Vitest infrastructure exists anywhere in this repo yet (`package.json` has
no `vitest`/`@vitest/*` devDependency, no `vitest.config.*`). Standing it up
means adding a new devDependency, which means running `npm install` — and the
worker executing series-k4zpn was operating in a git worktree whose
`node_modules` is a **junction to the main tree's real `node_modules`**, with
an explicit instruction not to run `npm install` from inside a worktree (it
would write into the shared main-tree `node_modules`, affecting every other
worktree and the main tree itself, outside that worker's isolated scope).

So series-k4zpn's `NextUp.compute` shipped with correct behaviour (proven via
9 Expecto tests covering the same rule server-side, a clean `npm run build`,
and manual code-review of the extracted function against the SQL view's
logic) but **no client-side automated test**, per the worker-return-format's
"UI tasks where the project has no UI test infrastructure" TDD-skip category.

## What

- Add `vitest` (+ any adapter `vite-plugin-fable` needs to resolve compiled
  Fable output — check whether `vite-plugin-fable`'s own docs/examples cover
  a Vitest setup, or a plain `vite.config.ts` test block pointed at Fable's
  emitted JS is enough) as a devDependency, from the **main tree**, not a
  worktree — an ordinary session with a real `npm install` allowed, not a
  worker's constrained one.
- Wire an `npm run test:client` script.
- Write the first real spec: `src/Client/Pages/SeriesDetail/NextUp.spec.ts`
  (or `.fs` compiled via Fable, whichever the chosen setup expects) covering
  the two client acceptance criteria series-k4zpn described:
  - a gap behind the furthest-watched episode is skipped — the hero names the
    episode after the frontier
  - no episode exists beyond the frontier — the hero renders nothing
- Once this lands, retrofit `NextUp.fs` with the spec file the extraction was
  originally meant to enable.

## Acceptance criteria

- [ ] `npm test:client` (or equivalent) runs a real Vitest suite against
      Fable-compiled client code, green.
- [ ] `NextUp.compute` has a spec covering the two scenarios above.
- [ ] `npm run build` still clean.
