---
id: series-x4qte
title: Add client-side regression coverage for `NextUp.compute` — the frontier rule (gaps behind the furthest-watched episode are history, not a queue) currently has no client test, only its server-side mirror
status: backlog
type: chore
context: series
created: 2026-08-07
completed:
depends_on: [infrastructure-j7v3c]
blocks: []
tags: [testing, vitest, fable, client, series]
related_adrs: [0063, 0064]
related_research: []
prior_art: [series-k4zpn]
---

## Why

`series-k4zpn` extracted `NextUp.compute` (`src/Client/Pages/SeriesDetail/NextUp.fs`) as a pure,
Feliz-free `SeasonDto list -> (int * EpisodeDto) option` *specifically* so the frontier rule could
be asserted without driving the DOM — and so the series-detail hero card (`Views.fs:1213`) and the
Episodes-tab "NEXT" badge / "Coming Next" divider (`Views.fs:1769`), which both call it, are
mechanically guaranteed to agree.

It shipped with no client-side test. ADR-0063's "Client test coverage deferred" section records
why (no Vitest infrastructure, and standing it up needs an `npm install` that is unsafe from a
worker worktree) and backlogs this task by name. Its correctness today rests on mirroring the
`series_next_up` SQL view function-for-function plus 4 Expecto tests proving the identical rule
*server-side* — real evidence, but not coverage of the client function that actually renders.

**The blocker is gone.** `infrastructure-j7v3c` shipped the harness on 2026-08-08 (ADR-0064):
Vitest 3.2.7 runs `*.test.fs` through the app's existing `vite.config.mts` Fable plugin instance,
Fable.Mocha 2.17.0 is the DSL, `npm run test:client` is the entry point, and `src/Client/Smoke.test.fs`
proves the vite-node/SSR transform path end-to-end. What remains here is the series domain
assertion, which is where the frontier rule's language belongs.

## What

Write `src/Client/Pages/SeriesDetail/NextUp.test.fs` — on disk alongside `NextUp.fs`, so the rule
and its assertions sit together — following the harness conventions `infrastructure-j7v3c`
established.

Register it as an explicit `<Compile Include>` item in `Client.fsproj` **in the contiguous test
block immediately before `<Compile Include="App.fs" />`**, joining `Smoke.test.fs`:

```xml
<Compile Include="Views.fs" />
<Compile Include="Smoke.test.fs" />
<Compile Include="Pages\SeriesDetail\NextUp.test.fs" />
<Compile Include="App.fs" />
```

This is the shipped convention (ADR-0064: test files are `<Compile>` items *after the modules they
test*, typechecked by `npm run build` but out of the bundle) and it satisfies F#'s ordering rule,
since `NextUp.fs` compiles at line 43. An earlier draft of this task said "immediately after
`NextUp.fs`, before `State.fs`" — that was written a day before the harness existed and is
superseded; do not use it. Compile-order placement is independent of the on-disk path.

The rule under test, from `NextUp.fs`'s own docblock: Next Up is the first episode by
`(season, episode)` order that is unwatched **and strictly past the frontier**, where the frontier
is the maximum `(SeasonNumber, EpisodeNumber)` among watched episodes. A skipped episode behind
the frontier must not pin Next Up forever.

## Acceptance criteria

- [ ] `NextUp.test.fs` covers all six behaviours:
      **(1)** a gap behind the frontier is skipped — the result names the episode *after* the
      frontier, not the gap; **(2)** no watched episodes anywhere → the first episode overall;
      **(3)** nothing exists past the frontier → `None`, even with unwatched gaps sitting behind
      it; **(4)** a cross-season frontier (S1E10 watched, S2E1 unwatched → S2E1) confirming
      lexicographic tuple ordering; **(5)** an empty `seasons` list → `None` without throwing;
      **(6)** unordered input — seasons and episodes passed out of order — yields the same answer
      as the sorted equivalent, pinning the normalization `compute` does before finding the
      frontier.
- [ ] Every assertion checks **both** halves of the `(int * EpisodeDto) option` result — the
      season number and the episode number — not just the episode.
- [ ] `npm run test:client` is green with these specs included, and still runs `Smoke.test.fs`
      too (the block registers, it does not replace).
- [ ] `npm run build` still passes clean — the new compile item does not break ADR-0037's
      typecheck gate.

## Notes

- **Fixture helpers, not hand-rolled records.** `EpisodeDto` carries 10 fields and `SeasonDto` 8,
  of which only `SeasonNumber` / `EpisodeNumber` / `IsWatched` / `Episodes` matter here. Write two
  small private helpers (an `episode n isWatched` and a `season n episodes`) filling the rest with
  neutral defaults, so each of the six cases reads as its scenario rather than as DTO plumbing.
  A test whose intent is buried in irrelevant fields is the one that gets deleted in the next
  refactor.
- **Assert on the numbers, not the whole tuple.** `Expect.equal` against a full `EpisodeDto` record
  produces an unreadable diff on failure and turns any future DTO field addition into a broken
  test. Destructure the result and assert `SeasonNumber` + `EpisodeNumber`.
- **Module name:** `module Mediatheca.Client.Pages.SeriesDetail.NextUpTests` — *not* `...NextUp.Tests`,
  which would declare a `Tests` module under a `NextUp` **namespace** and collide with the existing
  `NextUp` module. Register with `Mocha.runTests <testList> |> ignore` at module level, mirroring
  `Smoke.test.fs`.
- **Case (4), the cross-season frontier, is the one worth being deliberate about:** F#'s structural
  tuple comparison is already lexicographic, unlike SQLite's — that asymmetry is called out in
  `NextUp.fs`'s docblock and is exactly the kind of thing a refactor could silently break.
- **Case (6) has no server-side mirror.** The other five behaviours are each also proven by an
  Expecto test against the `series_next_up` view, so a client regression would at least contradict
  a passing server test. The sort is different: server-side it is SQLite's `ORDER BY`, client-side
  it is `List.sortBy` inside `compute`. Nothing else in the repo pins it.
- **Watch-record scope** is whatever the caller's `SeasonDto list` carries in `EpisodeDto.IsWatched`
  (for the series-detail page, the *selected rewatch session*'s scope — deliberately not unified
  with the server view's union-across-rewatches scope). Fixtures should set `IsWatched` directly
  and not attempt to model rewatch scoping; that is a separate modelling question per ADR-0063.
- **Do not run `npm install`.** Both dependencies are already on `main` (vitest 3.2.7,
  Fable.Mocha 2.17.0). An install from a worktree writes through the `node_modules` junction into
  the shared main tree (ADR-0063).
- **Dispatch note for the conductor.** A fresh worktree has no root `node_modules` — it is
  gitignored, so `git worktree add` does not carry it. This task needs `npm run build` and
  `vitest`, so the worktree's `node_modules` must be junctioned to the main tree's real one before
  dispatch, and **unlinked before `git worktree remove --force`** or that command recurses through
  the junction and destroys the shared tree. Same mechanism the 2026-08-08 `infrastructure-j7v3c`
  session used; see that session-end protocol entry's "Harness note for future sessions".
