# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-08-01 02:29 -- Task verified and completed: integration-007 - Fetch Jellyfin episode stills when materializing a missing season

**Type:** Work / Task completion
**Task:** integration-007 - Fetch Jellyfin episode stills when materializing a missing season
**Summary:** Materialized Jellyfin episodes now get a real thumbnail — fetched best-effort from Jellyfins primary-image endpoint through the existing re-auth policy and stored at a distinct stills/{slug}-sXXeYY-jellyfin.jpg path so a later TMDB refresh still overwrites it with its own canonical still
**Duration:** 26m
**Verification:** PASS (iteration 2)
**Files changed:** 9
**Tests added:** 8
**ADRs written:** 0039

---

## 2026-08-01 02:23 -- Verification failed: integration-007 - Fetch Jellyfin episode stills when materializing a missing season

**Type:** Work / Verification failure
**Task:** integration-007 - Fetch Jellyfin episode stills when materializing a missing season
**Iteration:** 1 of 3
**Reasons:** Check 6 (ADRs for decisions) — two embedded decisions went unrecorded with `ADRS_WRITTEN: none`: the deliberate `-jellyfin.jpg` storage-path divergence from TMDB's canonical path (whose entire purpose is defeating `SeriesRefresh`'s `imageExists` short-circuit, so a maintainer "tidying" the suffix would silently break acceptance criterion 3 with no test-visible failure), and the accepted-orphan tradeoff and its ADR-0025 orphan-scanner interaction; the task's `## Outcome` declined the ADR on the grounds the shape was already recorded in the task file's own "Resolved implementation shape" section, which is exactly the task-file-narration-for-ADR substitution check 6 forbids; and ADR 0012's Consequences is now actively stale — it still states materialized stills are `NULL` because the wiring returns `None`, the very deferral this diff closes.
**Not in dispute:** checks 1-5 passed; `npm test` 435 passed / 0 failed (427 + 8 added), `npm run build` green, `materializeMissingEpisodes` confirmed purely additive (`numstat` 33/0 and 47/0). The `[human-eye]` criterion 6 was correctly left unchecked — builder eye-check pending.
**Iteration hint:** likely-fixable (documentation only — no production-code change needed)
**Next:** re-dispatched worker

---

## 2026-08-01 02:06 -- Batch started: [integration-007]

**Type:** Work / Batch start
**Tasks:** integration-007 - Fetch Jellyfin episode stills when materializing a missing season
**Parallel:** no (1 worker — the ready set was exactly one task; MAX_PARALLEL never bound, nothing held back)

---

## 2026-08-01 02:05 -- Modeling / Promoted: integration-007 - Fetch Jellyfin episode stills when materializing a missing season

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-08-01 02:04 -- Modeling / Refined: integration-007 - Fetch Jellyfin episode stills when materializing a missing season

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** Resolved the implementation shape against the real code. The load-bearing finding: storing the Jellyfin still at TMDB's canonical `stills/{slug}-sXXeYY.jpg` path would have silently violated the task's own third acceptance criterion — `SeriesRefresh.fs:99-110` short-circuits its download on `ImageStore.imageExists`, so a Jellyfin file at that path would make TMDB skip its own download and keep the Jellyfin bytes permanently. Refined to a distinct `-jellyfin.jpg` suffix, which needs zero changes to `SeriesRefresh`/`Tmdb`. Also pinned down: a new binary sibling to `fetchJsonWithAuth` (Jellyfin's adapter is JSON-only today) reached through the existing `withReauthRetry` policy; an unconditional fetch attempt rather than a `PrimaryImageTag` pre-check (materialization only touches episodes missing from the projection, so the attempt is cheap and robust against `ImageTags` not being populated); the sync seam is synchronous so the fetch runs via `Async.RunSynchronously` per existing precedent; and a pure injected-effect `fetchEpisodeStill` as the testable unit. `materializeMissingEpisodes` stays untouched. Criteria went 3 → 6, five machine-checkable plus one `[human-eye]` for the rendered thumbnail.
**Split into:** none
**ADRs written:** none — no new decision beyond ADR 0012's already-recorded deferral; the path-collision resolution is an implementation detail recorded in the task.

---

## 2026-08-01 01:37 -- Work session ended

**Type:** Work / Session end
**Duration:** 23m (batch start 01:13 → session end 01:37)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-vk7rd: 1
**Commits:** 3 (1 batch start, 1 task integration, this session-end entry)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back, and the conflict pre-scan had nothing to order.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 0 human commits since the 2026-07-31 20:40 boundary. Both intervening commits (`6c712f3` session-end bookkeeping, `28cd7a2` the vk7rd capture) carry `[task-id]` subject trailers, so neither reads as untrailed. No governed-surface drift; nothing written to `whats-next.md` by this pass. Done **by hand** — `lib/session-start-churn.mjs` is absent from the installed plugin (0.9.2).
**Vision-conformance:** none — batch aligns with vision. `design-system-vk7rd` is a defect fix in *already-shipped* shared UI chrome (the desktop rail every page renders, the Unified Dashboard included), not new scope; it touches no `## Out of Scope (v1)` item and is not admin-console scope, so the Operability **Boundary** does not govern it. Recorded without a flag rather than manufacturing drift. Worth stating as context, not as a flag: this is now the **fourth consecutive session** with no media-experience roadmap work (In Focus, Unified Dashboard, Steam Import, HLTB) — that arc remains entirely unbuilt, and after this session the todo board is empty, so the next session will hit Phase 2's vacuum guard unless the builder promotes something.

Note on mechanization: as on 2026-07-31, `lib/vision-conformance.mjs`'s `extractVisionSections` could not drive this pass — it looks for `## What success looks like` / `## Non-goals`, and this project's `vision.md` carries neither (it uses `## Out of Scope (v1)`, `## Design Principles`, and the Operability Boundary paragraph nested inside a roadmap section). Judged by hand against those surfaces instead. Second consecutive session recording this mismatch.
**Batch mix:** 100% product-facing (1 task). Classified **by hand** against `classifyTask`'s documented heuristic — `lib/vacuum-guard.mjs` is still absent from plugin 0.9.2, so `formatBatchMixLine` could not be invoked. `design-system-vk7rd` is `type: bug` and its touched files are entirely product surfaces (`src/Client/Components/Sidebar.fs`, `tests/e2e/sidebar-rail-viewport-pinned.spec.ts`, the design-system BC README — none under `lib/`, `skills/`, `agents/`, `references/`, `evals/`, or `knowledge/decisions/`), so the path-aware amendment classifies it product-facing rather than harness.
**Carry-over:** left behind (user WIP, 1 file — `"Mediatheca Directions.html"`, untracked and pre-existing, unchanged since before this session; it is the dir-3a/3c design reference the sidebar tasks quote from). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain.
**Worktree husks:** none left. `.worktrees/design-system-vk7rd` was torn down with its root `node_modules` junction unlinked **first** via a targeted `(Get-Item …).Delete()` guarded on `LinkType -eq 'Junction'` — never a recursive delete, which follows the junction into the shared real `node_modules` — with the shared copy verified intact (180 entries, `vite` present) afterward.

**Notes carried out of this run:**

1. **The verifier earned its keep by refusing a self-reported `[human-eye]` check, and this is the one outstanding item from the session.** The worker marked the final criterion `[x]` and appended "verified against the running dev server". The verifier declined to accept that as coverage and routed it back to **builder eye-check pending** (the ADR-0061 precedent this project set on 2026-07-31), passing the task on the other six criteria while recording the departure explicitly. **The task file on `main` still carries that criterion as `[x]`** — deliberately left as the worker wrote it rather than silently rewritten post-verification, so the correction lives here and in the end-of-run report instead. What still needs the builder's eyes on a desktop viewport: wordmark, tagline, group spacing, the bottom group's smaller scale, and the dir-3a burgundy active tab all unchanged.
2. **The fix is two classNames, and the reason it was ever wrong is now written down where the next person will trip over it.** `Html.aside` went `min-h-screen` → `lg:sticky lg:top-0 lg:h-screen` (a ceiling, not a floor) and the inner `Html.nav` gained `overflow-y-auto`. `DesignSystem.navGroupBottom`'s `mt-auto` was untouched — it was always correct, it just had nothing bounded to resolve against. The BC README's "Layered sidebar nav" entry now states the durable rule outright: **`mt-auto` alone is not sufficient — the rail's own box must be viewport-height for the pin to read as "foot of the viewport."** design-system-t4b9k shipped the `mt-auto` half in July and left the height half undone, so the pin has been against the document ever since.
3. **A doctrine-bearing README amendment shipped with its own enforcement in the same commit.** The verifier's check 6c fired on the ubiquitous-language edit and found the new rule is not prose-only — `tests/e2e/sidebar-rail-viewport-pinned.spec.ts` asserts it behaviorally (scroll-top/scroll-bottom viewport-relative position equality on a page proven taller than the viewport first, so it cannot pass trivially).
4. **The new e2e spec is deliberately *not* behind the `CI` gate, and the verifier checked that claim rather than trusting it.** The gate exists for destructive specs (`administration-svq3t`'s precedent); this one only navigates read-only, so it runs on a bare `npx playwright test`. The verifier read the spec source to confirm no `test.skip` was present — the failure mode it was guarding against is a gated spec reporting "3 skipped, exit 0" and being mistaken for evidence.
5. **One honest coverage gap, judged non-blocking:** criterion 5's second clause (the `min-w-0` overflow behavior — a horizontally scrolling poster row must not widen the page) has no assertion. The spec covers only the no-horizontal-gap half. `Components/Layout.fs`, which owns `min-w-0`, is untouched by this diff, so the verifier treated it as an unexercised non-regression clause rather than a defect. Worth an assertion if that behavior is ever load-bearing again.
6. **Test suites: Expecto still 427 (no server code touched); Playwright e2e grew 11 → 14 flows.** The worker reports a genuine red-then-green TDD cycle — it stashed the `Sidebar.fs` change and measured the new spec failing for the right reason (the "Admin" bounding box overflowing the viewport by ~386px, and a nonzero document `scrollTop` on the short-viewport case) before restoring the fix.

**Harness defects observed (in the installed `agentheim` plugin 0.9.2, not this project):**

1. **`checkpoint` still does not fold in the vacated lifecycle path** — ADR-0057 / `agentic-workflow-w2njd` specify that naming a task file's new location should add the moved-from path to `changed`. It did not; the conductor staged the vacated `doing/` path by hand, after which git recorded the transition as a clean 67%-similarity rename. **Fourth consecutive session reporting this.**
2. **`lib/vacuum-guard.mjs` is still absent from plugin 0.9.2**, so `formatBatchMixLine` could not be invoked and the batch mix was classified by hand. Third consecutive session reporting this.
3. **Several other `lib/` modules the SKILL prose instructs the conductor to call are absent from 0.9.2** — `session-start-churn.mjs`, `worktree-salvage.mjs`, `adr-allocation.mjs`, `index-entry-length.mjs`. Each step was performed by hand instead (or was moot this session — no ADR was written, nothing was abandoned, so salvage and ADR-renumbering never fired). Recorded so the gap between the prose and the shipped package is visible rather than silently absorbed.
4. **`lib/vision-conformance.mjs` is present but inapplicable to this project's vision shape** — see the Vision-conformance note above. Second consecutive session.
5. **The `checkpoint` CLI rejects Windows backslash paths in its JSON `fileList`** (`invalid-opts-json`, "Bad escaped character in JSON") when the JSON is composed in a Bash-tool single-quoted argument, because `\s`/`\c` are not valid JSON escapes. Forward slashes work first try and are the reliable form here — the same class of Windows/shell papercut as the 2026-07-31 `cmd /c mklink` finding.

---

## 2026-08-01 01:35 -- Task verified and completed: design-system-vk7rd - Sidebar bottom group (Admin/Settings) must pin to the bottom of the viewport, not the bottom of the document — the rail is `min-h-screen` and stretches with page content, so on any scrolling page the group sits below the fold

**Type:** Work / Task completion
**Task:** design-system-vk7rd - Sidebar bottom group (Admin/Settings) must pin to the bottom of the viewport, not the bottom of the document — the rail is `min-h-screen` and stretches with page content, so on any scrolling page the group sits below the fold
**Summary:** Sidebar rail is now viewport-height and viewport-pinned (lg:sticky lg:top-0 lg:h-screen in place of min-h-screen, plus overflow-y-auto on the inner nav), so the bottom groups mt-auto pin resolves against the viewport instead of the document and Admin/Settings no longer sit below the fold
**Duration:** 21m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 3
**ADRs written:** none

---

## 2026-08-01 01:13 -- Batch started: [design-system-vk7rd]

**Type:** Work / Batch start
**Tasks:** design-system-vk7rd - Sidebar bottom group (Admin/Settings) must pin to the bottom of the viewport, not the bottom of the document — the rail is `min-h-screen` and stretches with page content, so on any scrolling page the group sits below the fold
**Parallel:** no (1 worker) - the ready set is exactly one task; nothing held back

---

## 2026-08-01 -- Modeling / Captured: design-system-vk7rd - Sidebar bottom group must pin to the viewport, not the document

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** The rail's Admin/Settings group sits below the fold on any scrolling page — `Sidebar.fs`'s aside is `min-h-screen` inside `Layout.fs`'s stretching flex row, so it grows to document height and `mt-auto` pins the group to the foot of the document rather than the viewport. Fix is a viewport-height, `sticky top-0` rail plus internal nav scroll for short viewports; filed straight to todo as a well-understood layout defect.

---

