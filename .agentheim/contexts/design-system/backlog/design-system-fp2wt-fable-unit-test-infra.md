---
id: design-system-fp2wt
title: "SUPERSEDED — recommended for dismissal. Harness scope moved to `infrastructure-j7v3c`; the progress-primitive unit tests are dropped as low-value (branch-free view functions already guarded by the `bool list` signature, `dotnet build`, and StyleGuide review)"
status: backlog
type: chore
context: design-system
created: 2026-08-07
completed:
depends_on: []
blocks: []
tags: [testing, infra, fable, client, superseded]
related_adrs: [0015, 0027]
related_research: []
prior_art: []
---

## Why

This task was captured 2026-08-07 to (a) stand up client-side unit-test infrastructure and
(b) regression-test `progressEpisodes` / `progressSeasons` / `seriesSeasonEpisodeProgress` from
`design-system-mz9v7`. Refinement on 2026-08-08 found that neither half survives here.

**The harness half was never design-system's.** A client unit-test runner is globally true — if
design-system did not exist, the harness would still be wanted. `series-x4qte` independently
captured near-identical prose for it the same day, which is itself the evidence that it belongs
to neither BC. It now lives as `infrastructure-j7v3c`, alongside its sibling `infrastructure-p1h9a`
/ ADR-0037 (the client build gate).

**The test half targets code with no testable logic.** All three functions
(`DesignSystem.fs:341-382`) return `ReactElement` and are straight `List.indexed` maps to divs
with a conditional class — there is no pure function underneath to assert against. More
importantly, the bug they were built to prevent cannot recur: the retired count-based
`progressSegmented filled total` could only ever paint a *prefix* because a count was all it was
given, and the replacement takes a `bool list`. **That guarantee is structural, in the type
signature — not behaviour that could regress.** A test here would restate the implementation
rather than guard it.

The two ways to make them testable were both considered and rejected: extracting a pure
`bool list -> string list` helper moves the class-string away from the `Html.div` that carries it
for zero testability gain (test-induced design damage), and a jsdom render assertion is
disproportionate machinery for a six-line branch-free function.

Coverage for these primitives therefore stays where it is: `dotnet build` typechecking plus
StyleGuide visual review on the running page (ADR-0015) — whose specimens already carry a
mid-season gap fixture precisely so the prefix-paint shape cannot silently return. For a
branch-free view function that is strictly stronger than a change-detector unit test.

## What

Nothing. No scope survives independently of `infrastructure-j7v3c`.

## Acceptance criteria

None — this task is not intended to be worked. See Notes.

## Notes

**Recommended for dismissal — awaiting builder confirmation.** Superseded by
`infrastructure-j7v3c` (harness) with no residual design-system scope. Left in `backlog/` rather
than deleted because dismissal requires an explicit builder decision; run
`/agentheim:modeling dismiss design-system-fp2wt` to drop it.

Establishes the general rule this refinement settled, worth keeping even if the task is deleted:
**branch-free Feliz view functions are not unit-test targets in this project.** The harness ADR
(provisional 0064, written by `infrastructure-j7v3c`) records it as a dropped-scope clause, so
the rule outlives this file.
