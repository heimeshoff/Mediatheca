---
id: administration-b3xqf
title: Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
status: backlog
type: chore
context: administration
created: 2026-09-03
completed:
depends_on: []
blocks: []
tags: [readme, cleanup, documentation]
related_adrs: [0052, 0056, 0058]
related_research: []
prior_art: []
---

## Why

`infrastructure-r8kqt` retired `StartupCutover.fs` (ADR-0052) and the fired one-shot
`EventLogFilter.fs` purge tooling (both the module and its `filter-demoted-events` CLI
dispatch branch in `Program.fs`) once their one-time jobs had run and their rollback
windows closed. `.agentheim/contexts/administration/README.md`'s "Offline demoted-event
filter" ubiquitous-language entry still describes `EventLogFilter.fs`'s live API
(`filterNdjson`, `purgeEligibleEventTypes`, the CLI invocation) and cross-references
`StartupCutover.fs`'s retirement precedent as something that hadn't happened yet — both
are now stale pointers to deleted code.

`infrastructure-r8kqt` could not fix this itself: cross-BC work (touching another
context's README) means the task itself was scoped wrong, so this is filed as a
follow-up instead, per the worker's scope-discipline rule.

## What

Update the "Offline demoted-event filter" entry in
`.agentheim/contexts/administration/README.md` to reflect that `EventLogFilter.fs` and
its CLI entry point are deleted (the purge ran once, successfully, 2026-08-05; see
`docs/runbooks/purge-demoted-metadata-events.md`, which carries an executed/retired
header note). Decide, while touching it, whether the surrounding language (the
`PlaySessionMigration.fs` retirement note, the `StartupCutover.fs` compile-dependency
anecdote) also needs updating now that `StartupCutover.fs` itself is gone — the anecdote
may be worth keeping as historical color (why the guard was reduced rather than deleted
at the time) even though the file it names no longer exists.

## Acceptance criteria

- [ ] The "Offline demoted-event filter" README entry no longer describes
      `EventLogFilter.fs` as live code; it either removes the entry or reframes it as
      historical ubiquitous language, consistent with how the README treats other
      retired mechanisms.
- [ ] No README prose asserts `EventLogFilter.fs` or `StartupCutover.fs` still exist in
      `src/`.
- [ ] `grep -rn "EventLogFilter\|StartupCutover" src/ tests/` (already empty as of
      infrastructure-r8kqt) stays empty — this task touches no code, only the README.

## Notes

Surfaced by `infrastructure-r8kqt` (2026-09-03) while retiring the cutover machinery and
the fired purge tooling — see that task's Outcome section and ADR-0052's retirement note
for what was deleted and why.
