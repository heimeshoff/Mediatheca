# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-09-03 20:11 -- Task verified and completed: games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise

**Type:** Work / Task completion
**Task:** games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
**Summary:** The game detail page two-column grid is now the unconditional page frame — the right-hand card column (Links, play facets, friends, catalogs) stays mounted at the same DOM node across the Overview/Journal tabs — and the page opens Journal-first when the game journal document already has non-blank content, computed server-side via a new shared JournalBlock.hasContent rule.
**Duration:** 32m
**Verification:** PASS (iteration 1)
**Files changed:** 20
**Tests added:** 9
**ADRs written:** none

---

## 2026-09-03 19:38 -- Batch started: [games-t69rb]

**Type:** Work / Batch start
**Tasks:** games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
**Parallel:** no (1 worker — games-t69rb is the only ready task across every BC; nothing held back)

---

## 2026-09-03 19:25 -- Modeling / Captured: games-t69rb - Game detail page — persistent right-hand card column across Overview/Journal tabs, Journal-first when the game has journal content

**Type:** Modeling / Capture
**BC:** games
**Filed to:** todo
**Summary:** The game detail page's right-hand cards (Links, play facets, friends, catalogs, …) currently live inside the Overview tab arm and vanish on the Journal tab; the task lifts them out so the tab only swaps the content column. Adds a once-per-load default-tab rule: land on Journal when the game's journal document has a non-blank block, Overview otherwise, recommended via a HasJournalContent flag on the GameDetail DTO. Concrete enough to file straight to todo (styleguide gate design-system-001 already done).

---

## 2026-09-03 18:00 -- Work session ended

**Type:** Work / Session end
**Duration:** 5m (first "Batch started" 17:55 → 18:00)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** administration-b3xqf: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is a documentation-only chore (README entry reframed as history, ADR-0058 retirement note); it adds no admin-console scope, pulls toward no Out-of-Scope (v1) item, and moves nothing away from Remaining v1 Work.
**Batch mix:** 0% product-facing / 100% harness / 0% bookkeeping (1 task) — `type: chore` touching a BC README and an ADR, which the helper's heuristic classes as harness rather than bookkeeping.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 1 human commit since the 17:40 boundary — `2f7e74a Merge branch 'fix-cutover'`, the builder's merge of the previous session's branch into main; it carries no content of its own beyond the already-trailed task commits it integrates. Touches no governed surface; nothing to re-align.

**Builder runbook still pending (from infrastructure-r8kqt):** Part B — delete the three server backups under `/app/data/backups` and the dev-machine stale inventory under `C:\Users\marco\app\mediatheca\backups\`; then `/deploy` and confirm the first boot logs zero `[StartupCutover]` lines with drift 0/7.

**Harness notes:** (1) the `checkpoint` verb rejected a fileList JSON with backslash-escaped `C:\...` paths when passed through Git Bash (`invalid-opts-json`); forward-slash drive-letter paths (`C:/...`) worked. (2) The source-repo `lib/task-lifecycle-cli.mjs` (0.9.2 tree) DID fold the vacated `doing/` path into the checkpoint manifest this time, unlike last session's cached plugin. (3) Board is empty after this task — no ready work remains in any BC.

---

## 2026-09-03 18:00 -- Task verified and completed: administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt

**Type:** Work / Task completion
**Task:** administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
**Summary:** Reframed the administration README's Offline demoted-event filter bullet as settled history (purge executed 2026-08-05, tooling retired by infrastructure-r8kqt on 2026-09-03, pointers to the runbook and ADR-0058) and appended a Retirement note to ADR-0058 recording both the CLI subcommand and StartupCutover.fs's playSessionPhase guard as deleted.
**Duration:** 3m30s
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-03 17:55 -- Batch started: [administration-b3xqf]

**Type:** Work / Batch start
**Tasks:** administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
**Parallel:** no (1 worker — administration-b3xqf is the only ready task across every BC; nothing held back)

---

## 2026-09-03 17:51 -- Modeling / Promoted: administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt

**Type:** Modeling / Promote
**BC:** administration
**From → To:** backlog → todo

---

## 2026-09-03 17:51 -- Modeling / Refined: administration-b3xqf - Update the administration README's Offline demoted-event filter entry

**Type:** Modeling / Refine
**BC:** administration
**Status after:** todo
**Summary:** Grounded the stale-README follow-up against the tree: README line 28 is the only line naming the deleted `EventLogFilter.fs`/`StartupCutover.fs`; ADR-0052 already carries a retirement note, ADR-0058 does not; the runbook has its executed/retired header. Settled the open question in What: the entry is reframed as settled history (README's existing "retired"/"formerly" convention), the `StartupCutover.fs` compile-dependency anecdote is dropped in favour of a one-clause pointer to ADR-0058 (pointer over restatement), and a matching "Retirement note (2026-09-03)" is added to ADR-0058. Scope pinned to exactly two files, six machine-checkable criteria. No orchestrator round — pure documentation chore, findings were factual.
**Split into:** none
**ADRs written:** none

---

## 2026-09-03 17:40 -- Work session ended

**Type:** Work / Session end
**Duration:** 18m (first "Batch started" 17:22 → 17:40)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** infrastructure-r8kqt: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task retires temporary cutover machinery, a dead sync gate, and a fired one-shot CLI (net −1116 lines); it adds no admin-console scope, pulls toward no Out-of-Scope (v1) item, and moves nothing away from Remaining v1 Work.
**Batch mix:** 0% product-facing / 100% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: chore` touching real source (`Composition.fs`, `PlaytimeTracker.fs`, `Program.fs`, both fsprojs, four deleted modules) rather than purely bookkeeping surfaces.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** one untrailed human commit since the last session-end boundary — `ed07f87 research for dashboard` (three research reports plus three root-level dashboard notes). Touches no governed surface; nothing to re-align. 0 recognized machine-shape commits, 1 human commit.

**Builder runbook pending (from infrastructure-r8kqt):** Part B — delete the three server backups under `/app/data/backups` and the dev-machine stale inventory under `C:\Users\marco\app\mediatheca\backups\`; then `/deploy` and confirm the first boot logs zero `[StartupCutover]` lines with drift 0/7. The task file's checkboxes for those criteria are deliberately unticked. New backlog item `administration-b3xqf` (administration README's Offline demoted-event filter entry now cross-references deleted files) was filed by the worker and indexed.

**Harness notes, carried forward:** (1) the `checkpoint` verb refuses every path as `outside-worktree` when the fileList is built from Git Bash's `$PWD` (POSIX `/c/...` form) — pass drive-letter paths. (2) Plugin 0.9.2's `checkpoint` manifest did not fold in the vacated `doing/` path; it was staged explicitly, and git recorded the move as a rename. (3) The main-tree `node_modules` junction was required again for `npm run build` and was unlinked before `git worktree remove --force`; main-tree `node_modules` verified intact afterwards (206 entries).

---

## 2026-09-03 17:35 -- Task verified and completed: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes

**Type:** Work / Task completion
**Task:** infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes
**Summary:** Retired the one-shot startup-cutover machinery (StartupCutover.fs and its Composition.fs call sites), the dead PlaytimeTracker Steam-sync gate, and the fired EventLogFilter purge CLI, restoring the plain Projection.startAllProjections boot path; ADR-0052 and the purge runbook carry retirement notes. Builder-owned Parts A/B (live and dev backup deletion) and the post-deploy boot check remain as runbook steps.
**Duration:** 13m40s
**Verification:** PASS (iteration 1)
**Files changed:** 12
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-03 17:22 -- Batch started: [infrastructure-r8kqt]

**Type:** Work / Batch start
**Tasks:** infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes
**Parallel:** no (1 worker — infrastructure-r8kqt is the only ready task across every BC; nothing held back)

---

## 2026-09-03 17:20 -- Modeling / Promoted: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes

**Type:** Modeling / Promote
**BC:** infrastructure
**From → To:** backlog → todo

---

## 2026-09-03 21:05 -- Modeling / Refined: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups

**Type:** Modeling / Refine
**BC:** infrastructure
**Status after:** todo
**Summary:** Window-closed grounding pass. Both rollback windows (2026-08-17, 2026-08-19) have elapsed, so the Part A precondition was run read-only from the modeling session: 0 `StartupCutover] Phase` log lines in 24h, container up 3 days healthy, drift check 0 discrepancies across all 7 projections, and `/app/data/backups` holds exactly the three inventoried files. Recorded in Notes that the backups are now expired rollback points, so the one-way-ordering hazard no longer constrains sequencing — Part C (worker) and Part B (builder) may run in either order. Re-grounded every named symbol against the working tree: all present, only fsproj/Composition line hints drifted (noted; task already says re-locate by name). Settled `plan.md`'s disposition — it is tracked (commit 648db9c, "kept for the record"), not untracked as the 2026-08-04 note claimed; it stays, out of scope, same reasoning as the purge runbook. No orchestrator round — third refinement, findings were factual corrections grounded directly in source and production state.
**Split into:** none
**ADRs written:** none

---

