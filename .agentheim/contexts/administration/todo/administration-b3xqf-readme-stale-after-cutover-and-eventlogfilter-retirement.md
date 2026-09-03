---
id: administration-b3xqf
title: Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
status: todo
type: chore
context: administration
created: 2026-09-03
completed:
depends_on: []
blocks: []
tags: [readme, cleanup, documentation]
related_adrs: [0052, 0056, 0058]
related_research: []
prior_art: [administration-z6ymt]
---

## Why

`infrastructure-r8kqt` (2026-09-03) retired `StartupCutover.fs` (ADR-0052 carries the
retirement note) and the fired one-shot `EventLogFilter.fs` purge tooling — the module,
its `EventLogFilterTests.fs`, both fsproj entries, and the `filter-demoted-events`
dispatch branch in `Program.fs`'s `main` — once their one-time jobs had run and their
rollback windows closed. `.agentheim/contexts/administration/README.md`'s
"Offline demoted-event filter" ubiquitous-language entry (line 28, the only stale line in
the file) still describes `EventLogFilter.fs`'s live API (`filterNdjson`,
`purgeEligibleEventTypes`, the CLI invocation) in the present tense and narrates
`StartupCutover.fs`'s `playSessionPhase` guard as still-existing code. Both are stale
pointers to deleted files.

`infrastructure-r8kqt` could not fix this itself: touching another context's README is
cross-BC work, so it filed this follow-up per the worker's scope-discipline rule.

## What

Reframe the "Offline demoted-event filter" entry in
`.agentheim/contexts/administration/README.md` as **settled history**, the way the README
already treats other retired mechanisms (the "formerly the Events tab of a standalone
`/admin` console" aside on the Event browser bullet; the "startup-time forced rebuild …
is retired — `buildApp` now only calls `Projection.startAllProjections`" sentence on the
Projections bullet). Keep the term — the purge is why eleven Game metadata event types
never appear in the log, and the runbook still documents it — but drop the live-API
description and the code-walkthrough narration.

Concretely, the rewritten entry should say, in roughly this shape:

- **Offline demoted-event filter** (administration-z6ymt, ADR-0056, ADR-0058) — the
  one-shot, operator-executed purge of the eleven demoted Game metadata event types
  (`games-v4nqe`) from the event log, run once on 2026-08-05 via export → offline
  type-level NDJSON filter → wipe-first import (ADR-0038) → Rebuild-all → drift-check.
  The filter tooling (`EventLogFilter.fs` and its `filter-demoted-events` CLI subcommand)
  was retired by `infrastructure-r8kqt` on 2026-09-03 since the purge does not recur;
  `docs/runbooks/purge-demoted-metadata-events.md` remains as the historical record
  (executed/retired header note), and git history is the escape hatch. ADR-0058 records
  the tooling's original CLI shape.
- Keep one sentence on `administration-z6ymt` also retiring the completed games-h4mrd
  play-session-history migration machinery (`PlaySessionMigration.fs`, the
  `/api/stream/migrate-play-sessions*` routes, `AdminGuards.PlaySessionMigrationInProgress`
  and its mutual-exclusion arms in `decideAndClaimWipeImportGuard`/`decideAndClaimRebuildGuard`) —
  those guard functions still exist in `Administration.fs`, so that part is current.
- **Drop** the `StartupCutover.fs` compile-dependency anecdote (why `playSessionPhase`
  was reduced to a guard rather than deleted). Decision made during refinement: the
  README restates ADR-0058's Decision 2 nearly verbatim, and the file it explains is now
  gone. The README keeps a pointer — "ADR-0058 also records the `StartupCutover.fs`
  compile-dependency it uncovered; that file was itself retired by `infrastructure-r8kqt`
  (ADR-0052 retirement note)" — and nothing more. Pointer over restatement, per the
  drift-fix discipline: the ADR is the canonical record, the README should not re-narrate it.

While touching the cross-reference, also append a short **"Retirement note (2026-09-03)"**
section to ADR-0058 (`.agentheim/knowledge/decisions/0058-offline-filter-cli-and-startup-cutover-forced-edit.md`),
mirroring the one `infrastructure-r8kqt` appended to ADR-0052: both things the ADR decided
the shape of (`EventLogFilter.fs`'s CLI subcommand, `StartupCutover.playSessionPhase`'s
guard) are deleted as of `infrastructure-r8kqt`; the decision stands as history. ADR-0058
is scoped to this BC, so this is in-scope. Do **not** touch ADR-0052 or ADR-0056 (0052
already has its note; 0056 decides the operator-executed shape, which is unaffected).

This task touches no code. Scope is exactly two files: the administration README and ADR-0058.

## Acceptance criteria

- [ ] The "Offline demoted-event filter" entry in
      `.agentheim/contexts/administration/README.md` is rewritten as history: it names the
      purge as executed (2026-08-05), names the tooling as retired by `infrastructure-r8kqt`
      (2026-09-03), points at the runbook and ADR-0058, and no longer describes
      `filterNdjson`, `purgeEligibleEventTypes`, or the `dotnet run … filter-demoted-events`
      invocation as live code.
- [ ] `grep -n "StartupCutover" .agentheim/contexts/administration/README.md` returns at
      most one line, and that line says the file was retired (points at ADR-0052/ADR-0058);
      the `playSessionPhase` compile-dependency narration is gone.
- [ ] No sentence in the README asserts `EventLogFilter.fs` or `StartupCutover.fs` exists
      under `src/` — every remaining mention is past-tense or marked retired.
- [ ] The README's other content is untouched: the diff to `README.md` is confined to the
      "Offline demoted-event filter" bullet (line 28 as of 2026-09-03).
- [ ] ADR-0058 gains a `## Retirement note (2026-09-03)` section naming
      `infrastructure-r8kqt` and stating that `EventLogFilter.fs` + its CLI branch and
      `StartupCutover.fs` are deleted; its frontmatter `status:` stays `accepted` (mirrors
      ADR-0052's treatment).
- [ ] `grep -rn "EventLogFilter\|StartupCutover" src/ tests/ --include=*.fs --include=*.fsproj`
      is empty before and after — this task changes no code, and `npm test` is not required.

## Notes

- Surfaced by `infrastructure-r8kqt` (2026-09-03) while retiring the cutover machinery
  and the fired purge tooling — see that task's Outcome section and ADR-0052's retirement
  note for what was deleted and why.
- Refinement (2026-09-03) grounded the task against the tree: README line 28 is the only
  line naming either deleted file; ADR-0052 carries a retirement note, ADR-0058 does not
  (hence the added criterion); the runbook already carries its executed/retired header;
  `Program.fs`'s `main` now goes straight to `Composition.buildApp`;
  `decideAndClaimWipeImportGuard`/`decideAndClaimRebuildGuard` still exist in
  `Administration.fs`. Stale build artefacts under `src/*/bin/` and `src/*/obj/` still
  contain the old symbol names — ignore them, they are not source.
- Compare the "Purge the 11 demoted metadata event types" entry in this BC's done-list
  (`administration-z6ymt`) and the infrastructure BC README, which `infrastructure-r8kqt`
  checked and found clean — the administration README is the only doc surface left.
