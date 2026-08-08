---
id: infrastructure-j7v3c
title: Stand up the Vitest-through-vite-plugin-fable client unit-test harness — `vitest@^3.2.4` driven through the app's existing `vite.config.mts` Fable plugin, Fable.Mocha as the DSL, `npm run test:client`, plus the ADR recording the harness and its boundary against ADR-0027's e2e suite
status: done
type: chore
context: infrastructure
created: 2026-08-08
completed: 2026-08-08
depends_on: []
blocks: [series-x4qte]
tags: [testing, infra, fable, client, tooling, build-health]
related_adrs: [0027, 0037, 0063, 0064]
related_research: []
prior_art: [infrastructure-p1h9a]
---

## Why

There is no way to unit-test a pure client-side F# function in this repo today. `test` is the
Expecto server suite, `test:e2e` is Playwright (ADR-0027) — nothing covers pure client F# short
of a full browser round-trip.

ADR-0027 named this gap explicitly and left the boundary open:

> There is no Vitest or client-side unit-test harness in this repo — Elmish `update` functions
> are pure and could in principle be unit-tested without a browser, but nothing does that today.
> [...] **Keep that boundary if such a harness appears later** — e2e specs stay reserved for
> paths that need the real network/DOM/timing story, not general Elmish logic coverage.

That harness is now worth adding, and it belongs to **infrastructure**, not to any single BC.
Two unrelated BCs independently captured near-identical prose for it on the same day
(`design-system-fp2wt` on 2026-08-07, `series-x4qte` on 2026-08-07) — which is itself the
evidence that it belongs to neither. It passes the infra routing test cleanly: if either
requesting BC did not exist, the harness would still be wanted. The closer in-repo sibling is
`infrastructure-p1h9a` / ADR-0037 (the client *build* gate) rather than ADR-0027 — both are
standing, always-on quality gates over `src/Client/` with no single motivating BC, changing on
toolchain cadence rather than domain cadence.

The concrete blocked consumer is `NextUp.compute` (`src/Client/Pages/SeriesDetail/NextUp.fs`),
extracted by `series-k4zpn` specifically so it could be tested without driving the DOM, and
shipped with no client-side test — see ADR-0063's "Client test coverage deferred" section, which
backlogs exactly this bootstrap.

## What

The `fable-frontend-tests` skill (user-level, `~/.claude/skills/fable-frontend-tests/`) prescribes
this setup and its version matrix matches this project exactly. Follow it; the notes below record
the project-specific calls it leaves open.

- ~~Add `vitest` as a devDependency.~~ **Already done — do not run `npm install`.** The builder
  pre-installed `vitest@^3.2.7` in the main tree on 2026-08-08 (see Notes for why, and for the
  verification). It is paired with `vite-plugin-fable` 0.1.1 → Vite 6; vitest declares Vite as a
  peer dependency, so an out-of-band vitest major either fails to resolve or silently runs a
  second Vite. Confirm it is present rather than installing it.
- Add a `test` block to the **existing** `vite.config.mts`. Do not create a second config file —
  that would instantiate a second Fable compiler instance against the same fsproj. Reusing the
  app config is precisely what makes Vitest run `.fs` through the already-configured `fable()`
  plugin:
  ```ts
  test: {
      include: ["**/*.test.fs"],
      exclude: ["**/fable_modules/**", "**/node_modules/**"],
      globals: true,          // Fable.Mocha registers via global describe/it
      environment: "node",
  }
  ```
- ~~`dotnet add src/Client/Client.fsproj package Fable.Mocha`~~ — **already done** (2.17.0,
  pre-installed alongside vitest). The Expecto-shaped DSL (`testList`/`testCase`/`Expect.*`),
  self-registering via `Mocha.runTests` at module level.
- Register test `.fs` files as explicit `<Compile Include>` items in `Client.fsproj`, after
  everything they test, conventionally as a contiguous block immediately before
  `<Compile Include="App.fs" />`. They are typechecked by `npm run build` but stay out of the
  shipped bundle (nothing imports them).
- Wire `"test:client": "vitest run"` in `package.json`. **Do not rename the existing `test`
  script** — it is the Expecto server suite, and both CLAUDE.md and the protocol depend on that
  meaning. Prefer `vitest run` over watch mode; `.fs` invalidation in vitest watch is not
  reliably supported by the plugin.
- Add one **domain-free, synthetic** smoke spec (a trivial arithmetic assertion via Fable.Mocha)
  proving the pipeline end-to-end. This is the task's one real technical risk and the reason it
  is worth a proving step: Vitest transforms through **vite-node/SSR**, not the dev-server
  pipeline `vite-plugin-fable` is actually proven under in this repo. Prove the transform works
  before trusting it. Deliberately do not use a real app function for this — see Notes.
- Document `npm run test:client` in CLAUDE.md's Build & Run section.
- Write the ADR (provisional **0064** — 0063 is the current head on `main`; renumber at
  squash-merge via `lib/adr-allocation.mjs`'s `finalizeAdrNumbering` per ADR-0058, exactly as
  0063 itself went through).

## Acceptance criteria

- [ ] `vitest` is a devDependency at `^3.2.x` — pre-installed at `^3.2.7`, see Notes — resolving
      against the existing Vite 6 with no second `vite` instance in the lockfile, and
      `vite.config.mts` carries a single `test` block of the shape above. No second vitest/vite
      config file exists.
- [ ] `Fable.Mocha` is a `PackageReference` in `Client.fsproj` (pre-installed at 2.17.0), and the
      domain-free smoke spec is an explicit `<Compile>` item; `npx vitest run` executes it green.
- [ ] `npm run test:client` is defined in `package.json`, runs the Vitest suite, and is
      documented in CLAUDE.md's Build & Run section.
- [ ] `npm run build` still passes clean — proving the new fsproj compile items do not break the
      .NET typecheck path (ADR-0037's gate).
- [ ] ADR-0064 (provisional) exists under `.agentheim/knowledge/decisions/` with `scope: global`,
      recording: the harness decision statement (below), why infrastructure owns it, the
      dropped-scope rule for branch-free Feliz view functions, the main-tree-only `npm install`
      policy, the `environment: "node"` default with the `jsdom` escape hatch noted but not
      adopted, and the boundary against ADR-0027's e2e suite.

## Notes

**Convention enforcement (ADR-0059).** This task establishes a durable convention — `*.test.fs`
naming, test files as ordered `<Compile>` items after the modules they test, `test:client` as the
client-suite entry point. It is enforced by a build failure shipped in the same task rather than
by prose: a test file in the wrong compile-order position is an F# compile error, and the
`npm run build` criterion above fails on it (ADR-0037 made that path fail-closed). Not
prose-only.

**Why the smoke spec is synthetic.** Two specialists split on this and the split is worth
recording. One proposed proving the harness against `progressEpisodes` as the least
domain-loaded real function available; the other concluded that function should not be unit-
tested at all (see `design-system-fp2wt`). A synthetic assertion satisfies both — it carries zero
domain language from either requesting BC, and it does not create a change-detector test against
a branch-free view function.

**ADR-0064 decision statement** (hand to the worker verbatim):

> Client-side F# is unit-tested by Vitest 3 (pinned `^3.2.4`) running `*.test.fs` files through
> the app's existing `vite.config.mts` Fable plugin instance, with Fable.Mocha as the
> Expecto-shaped DSL — no second config, no separate Fable compile step. Test files live under
> the vite root and must be explicit `<Compile>` items in `Client.fsproj` after the modules they
> test, so they are typechecked by `npm run build` while staying out of the shipped bundle. This
> harness owns pure client logic (Elmish `update`/`init`, pure helpers) and complements rather
> than replaces ADR-0027's Playwright e2e harness, whose specs stay reserved for paths needing
> the real network/DOM/timing story.

**Scheduling constraint — RESOLVED 2026-08-08.** This task originally could not be dispatched to
a worker worktree at all: a worktree's `node_modules` is a junction to the main tree's real one,
so `npm install` there mutates shared state outside the worker's isolated scope (ADR-0063). The
builder took the pre-install route on 2026-08-08 and committed the result, so the dependencies
are on `main` and every worktree branched from it already has them. **This is now an ordinary
worker-dispatchable task: config block, smoke spec, script, CLAUDE.md line, ADR — zero installs.**

Verified at pre-install time (`npm run build` clean, 39.5s):

| package | resolved | |
|---|---|---|
| `vite` | 6.4.1 | single instance — vitest peered onto the existing Vite 6, no second copy |
| `vitest` | 3.2.7 | satisfies the `^3.2.x` criterion above |
| `vite-plugin-fable` | 0.1.1 | unchanged |
| `ts-lsp-client` | 1.0.4 (nested under the plugin) | the `overrides` pin survived the fresh resolve |

That last row was the live risk: adding any devDependency re-resolves the tree, and `ts-lsp-client`
1.1.0 breaks the plugin's ESM imports. If a future install disturbs it, the pin is in
`package.json`'s `overrides` block.

**Related tasks.** Supersedes the harness scope of `design-system-fp2wt` (recommended for
dismissal) and unblocks the narrowed `series-x4qte`.

**Optional follow-on** (not in scope here): `context-map.md` could gain "build gate, test harness"
in infrastructure's core language and an "Infrastructure → every BC (open host: test harnesses
and build gates)" relationship bullet.

## Outcome

Confirmed `vitest@3.2.7` and `Fable.Mocha@2.17.0` already present (pre-installed on `main`,
d881d11) — no install run from this worktree. Added one `test` block to the existing
`vite.config.mts` (no second config file); wired `"test:client": "vitest run"` in `package.json`
without touching the existing Expecto `test` script; added `src/Client/Smoke.test.fs` — a
domain-free, synthetic `2 + 2 = 4` assertion via Fable.Mocha — as an explicit `<Compile>` item in
`Client.fsproj` immediately before `App.fs`, proving the Vitest-through-vite-node/SSR transform
path works end-to-end (`npx vitest run` → 1 test file, 1 test, green). `npm run build` still
passes clean (39.96s, only the pre-existing `FS0020` warning), proving the new compile item
doesn't break ADR-0037's typecheck gate. Documented `npm run test:client` in CLAUDE.md's Build &
Run section. Wrote ADR-0064 (provisional numbering per ADR-0058) recording the harness decision,
why infrastructure owns it, the dropped-scope rule for branch-free Feliz view functions, the
main-tree-only `npm install` policy, the `environment: "node"` default with the noted-but-
unadopted `jsdom` escape hatch, and the boundary against ADR-0027's e2e suite. Updated the
infrastructure BC README with a new "Client unit-test harness" ubiquitous-language entry.

Key files: `vite.config.mts`, `package.json`, `src/Client/Client.fsproj`,
`src/Client/Smoke.test.fs`, `CLAUDE.md`,
`.agentheim/knowledge/decisions/0064-vitest-fable-client-unit-test-harness.md`,
`.agentheim/contexts/infrastructure/README.md`.

**Iteration 2 fix (2026-08-08):** the verifier failed the task on the ADR file's *form* only —
`0064-vitest-fable-client-unit-test-harness.md` had no YAML frontmatter and encoded
status/scope as prose headings instead of the machine-readable fields every sibling ADR
carries. Added the standard frontmatter block (`id: 0064`, `title`, `scope: global`,
`status: accepted`, `date: 2026-08-08`, `supersedes: []`, `superseded_by: []`,
`related_tasks: [infrastructure-j7v3c]`, `related_research: []`), matching `0063-*.md`'s shape,
and dropped the now-redundant `## Status`/`## Scope` prose headings in favor of the H1 +
`## Context` shape the siblings use. Also repaired the garbled first bullet under
`## Alternatives considered`, which had merged the "second Vitest config file" rejection with an
orphaned sentence about Vitest's vite-node/SSR transform path — split back into two distinct,
correctly-scoped bullets. No code, test, or content changes beyond that structural repair; all
previously-passing checks (harness runners, ADR content requirements) are unaffected.

## Verifier note (iteration 1)

**VERDICT: FAIL** — everything the harness actually does verified green; the ADR file's *form* fails.

REASONS:
- `.agentheim/knowledge/decisions/0064-vitest-fable-client-unit-test-harness.md:1` has **no YAML frontmatter at all**. Check 6 requires well-formed frontmatter (`id`, `title`, `status`, `scope`, `date`); the file instead opens with `# ADR-0064 — ...` and encodes the fields as prose headings (`## Status` → `Accepted, 2026-08-08`, `## Scope` → `global`). 62 of the 64 ADRs in that directory carry the frontmatter block (`0063-*.md`, `0037-*.md`, `0027-*.md`, `0062-*.md` all do — `id`/`title`/`scope`/`status`/`date`/`supersedes`/`superseded_by`/`related_tasks`/`related_research`); the sole other exception, `0052-*.md`, is a pre-existing defect, not a precedent.
- This breaks the acceptance criterion literally as worded: "ADR-0064 (provisional) exists under `.agentheim/knowledge/decisions/` with **`scope: global`**". There is no `scope: global` field in the file — only a `## Scope` heading with `global` under it. The `work` skill's global-ADR index insertion keys on that machine-readable field: `.agentheim/knowledge/index.md`'s `<!-- adr-global:start -->` section renders `**<id>** -- <title> -- <date> -- <path>`, all three of which come from ADR frontmatter and none of which this file exposes.
- Missing `related_tasks: [infrastructure-j7v3c]` and `related_adrs`/`supersedes` linkage that every sibling ADR carries, so the ADR is not discoverable from the task graph.
- Secondary (content integrity, not on its own a blocker): the first bullet of `## Alternatives considered` is garbled — the "A second, separate Vitest config file. Rejected —" bullet runs straight into an orphaned sentence, "Vitest transforms through **vite-node/SSR**, not the dev-server pipeline `vite-plugin-fable` is actually proven under elsewhere in this repo...", which reads as a separate alternative whose bullet header was lost. Worth repairing while the file is open.

SUGGESTED_FIX: Add the standard YAML frontmatter block to `.agentheim/knowledge/decisions/0064-vitest-fable-client-unit-test-harness.md`, matching `0063-*.md`'s shape exactly (`id: 0064`, `title: ...`, `scope: global`, `status: accepted`, `date: 2026-08-08`, `supersedes: []`, `superseded_by: []`, `related_tasks: [infrastructure-j7v3c]`, `related_research: []`), and drop or keep the now-redundant `## Status`/`## Scope` prose headings consistently with the sibling ADRs; while there, repair the merged bullet in `## Alternatives considered`. No code changes are needed — the harness itself verified clean.

ITERATION_HINT: likely-fixable

**Checks that PASSED (do not redo this work):**
- Runners, from the worktree: `npm run test:client` → 1 file / 1 test passed, exit 0. `npm run build` → built in 37.15s, exit 0 (only the expected pre-existing `FS0020` warning at `AdminProjections/Views.fs`). `npm test` → 686 passed, 0 failed, exit 0.
- Criteria 1-4 all verified: one `vite.config.mts` (no second config), lockfile has exactly one `vite` (6.4.1) and one `vitest` (3.2.7), `test` block at `vite.config.mts:30-35`; `Fable.Mocha 2.17.0` at `Client.fsproj:108` with `<Compile Include="Smoke.test.fs" />` at line 98 immediately before `App.fs`; `"test:client": "vitest run"` present with the Expecto `test` script untouched and CLAUDE.md documented.
- Criterion 5's *content* requirements are all present in the ADR body (routing test, dropped-scope rule, main-tree-only install policy, `node` default with unadopted `jsdom` hatch, ADR-0027 boundary). Only the `scope: global` field *form* fails.
- Scope clean (8 files, no INDEX/protocol/other-BC edits, `package-lock.json` untouched); BC README synced; ADR-0059 mechanize-or-drop fires and passes; no protocol/index/git tampering.
