# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-07-06 16:49 -- Work session ended

**Type:** Work / Session end
**Duration:** ~10m (Batch started 16:39 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-r4m2p: 1
**Commits:** 3 (1 batch-start claim + 1 task commit + this session-end line)
**Vision-conformance:** none — batch aligns with vision (the responsive-layout bug fix directly serves the "Unified Dashboard" v1 success criterion and the "Mobile-first — dashboard sections work as a vertical scroll on mobile" design principle; adjusting the Books-placeholder's `lg`→`xl` responsive breakpoint only tunes an existing layout slot and does not build the "Books (v2)" Out-of-Scope feature)
**Carry-over:** src/Client/index.css: left behind (owner: user's own WIP — the `.status-badge` sizing/weight tweak, carried uncommitted across prior sessions; this session's squash-merge touched only Layout.fs + Views.fs, disjoint from the CSS region). "Mediatheca Directions.html": left behind (owner: user's design reference doc — the ~912KB captured Claude design session, untracked by the user since before this session, not project bookkeeping). .agentheim/state/: left behind (owner: work's own advisory observability heartbeat — `in-flight.json`, the ADR-0027 git-ignored advisory-artifact category; never a lifecycle write). .worktrees/: removed (empty dir residue after the PASS teardown deregistered `aw/intelligence-r4m2p`; the node_modules junction was unlinked before `git worktree remove` so the shared root `node_modules` stayed intact — verified `node_modules/react` present post-unlink).
**Note:** Single isolated single-task intelligence batch under worktree isolation (ADR-0032), PASS first try. **intelligence-r4m2p** fixed the Dashboard header search sliding off-screen on the All / Movies / TV Series tabs. Root cause (confirmed by the verifier against the actual `overflow-x-auto` poster rows and the block-level descendant chain): the shared `Html.main` flex column in `src/Client/Components/Layout.fs` lacked `min-w-0`, so its default `min-width:auto` let a horizontally-scrolling poster row force the whole column — and the right-aligned `headerLine` search button — wider than the viewport (the Games tab's wrapping poster *grid* never hit this floor, which is why only Games looked correct). Fix: added `min-w-0` to that shared `main` — a one-class flexbox fix that protects every page, not just the Dashboard tabs. Separately raised the All-tab Games/Books split from `lg:grid-cols-2` to `xl:grid-cols-2` (`allTabView`, Views.fs) so it stays a single stacked column through the mid-width range where two columns were cramped. `npm run build` clean (✓ 41.48s). No BC README change (no new ubiquitous language), no ADR (well-understood CSS pattern, not architecturally significant), no conflicts, no concept candidates, no new backlog items. The intelligence board is now quiescent: backlog holds its remaining items, todo / doing empty. **Note for next session:** the intelligence `doing/` directory was missing at session start and had to be created before the claim CLI could move the task — worth confirming the other BCs have their `doing/` dirs before their first `work` run.

---

## 2026-07-06 16:48 -- Task verified and completed: intelligence-r4m2p - Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight

**Type:** Work / Task completion
**Task:** intelligence-r4m2p - Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight
**Summary:** Fix Dashboard header search sliding off-screen on All/Movies/TV tabs (min-w-0 on shared main flex column) and raise Games/Books split to xl so it stacks single-column at tight widths
**Duration:** 8m
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-06 16:37 -- Batch started: [intelligence-r4m2p]

**Type:** Work / Batch start
**Tasks:** intelligence-r4m2p - Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight
**Parallel:** no (1 worker — sole ready task; dashboard header/responsive bug, dep design-system-001 done)

---

## 2026-07-06 15:20 -- Modeling / Captured: intelligence-r4m2p - Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Dashboard responsive-layout bug. The shared header's "Search your library" control stays pinned to the viewport's right on the Games tab but slides off-screen on All/Movies/TV Series — hypothesis: horizontal overflow from full-width poster rows grows the page container past the viewport, carrying the right-aligned search with it. Also make the All-tab Games/Books split stack to one column at tight widths instead of a cramped two-up. Frontend gate met (design-system-001 done).

---

## 2026-07-06 15:11 -- Work session ended

**Type:** Work / Session end
**Duration:** ~15m (Batch started 14:56 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-dq8rk: 1
**Commits:** 3 (1 batch-start claim + 1 task commit + this session-end line)
**Vision-conformance:** none — batch aligns with vision (direction 3a directly realizes the "Unified Dashboard → All tab" success criterion and the "intent-driven, not a catalog" / "unified, not siloed" design principles by stripping analytics off the landing page to surface what's-next media; the Books column is a labelled coming-soon *stub* — no events/API — so it does not build the "Books (v2)" Out-of-Scope feature, only reserves its layout slot)
**Carry-over:** src/Client/index.css: left behind (owner: user's own WIP — the `.status-badge` sizing/weight tweak "per 3d system-board reference", carried uncommitted across prior sessions; the task's squash-merge touched no CSS so the two are disjoint). "Mediatheca Directions.html": left behind (owner: user's design reference doc — the ~912KB captured Claude design session cited by this task's Notes, untracked by the user since before this session, not project bookkeeping). .agentheim/state/: left behind (owner: work's own advisory observability heartbeat — `in-flight.json`, the ADR-0027 git-ignored advisory-artifact category; never a lifecycle write). `.worktrees/` residue: removed (empty dir after the PASS teardown deregistered `aw/intelligence-dq8rk`; no source, no task files, no uncommitted work — the node_modules junction was unlinked before teardown so the shared root `node_modules` stayed intact).
**Note:** Single isolated single-task intelligence batch under worktree isolation (ADR-0032), PASS first try. **intelligence-dq8rk** re-pointed the Dashboard to direction 3a: the shared `tabBar` now consumes the reusable `DesignSystem.underlineTabClass` shipped by design-system-k9p3v (no bespoke pill CSS), a same-row right-aligned "Search your library" control wires through the existing cross-MVU `SearchModal` via a new `Open_search_modal` message the root `State.fs` intercepts (mirroring the Games/Movies/Series sibling pattern — search was not reimplemented), the page `<h1>` title was dropped, and `allTabView` was rewritten to strip analytics (Activity heatmap + monthly breakdown, the 14-day games play chart + summary stats, the hero spotlight, and `newGamesSection`) in favor of a full-width TV Series Next-Up row → full-width Movies row → a two-column Games (In Focus) / Books-placeholder split. Orphaned helpers (`activitySection`, `heroSpotlight`, `gamesRecentlyPlayedChartWithStats`, `newGamesSection`, …) were left in place unused (harmless, no `WarningsAsErrors`) to keep the diff scoped to the All-tab composition. `npm run build` clean. No BC README change (pure layout re-point, no new ubiquitous language). No ADR, no conflicts, no concept candidates, no new backlog items. The intelligence board is quiescent: backlog holds its remaining items, todo / doing empty.

---

## 2026-07-06 15:09 -- Task verified and completed: intelligence-dq8rk - Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split

**Type:** Work / Task completion
**Task:** intelligence-dq8rk - Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split
**Summary:** Rework Dashboard All-tab to direction 3a — underline tabs + inline library search, full-width TV & Movies rows, Games/Books two-column split
**Duration:** 11m
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-06 14:56 -- Batch started: [intelligence-dq8rk]

**Type:** Work / Batch start
**Tasks:** intelligence-dq8rk - Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split
**Parallel:** no (1 worker — sole ready task; frontend Dashboard 3a layout, deps design-system-001 + design-system-k9p3v both done)

---

## 2026-07-06 14:54 -- Modeling / Promoted: intelligence-dq8rk - Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split

**Type:** Modeling / Promote
**BC:** intelligence
**From → To:** backlog → todo

---

## 2026-07-06 14:52 -- Modeling / Refined: intelligence-dq8rk - Dashboard All-tab 3a layout

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** todo (auto-promoted — blocker cleared)
**Summary:** Readiness pass now that the sole blocker shipped. `design-system-k9p3v` (the reusable underline-tab component) completed PASS at 14:45, so both `depends_on` (design-system-001 + design-system-k9p3v) are done and the task cleared the gate. Baked the now-concrete k9p3v API into the Notes — the worker consumes `DesignSystem.underlineTabClass isActive` (backed by `.underline-tab::after` / `--color-gold`), swapping the filled-pill class in `tabBar` (Views.fs:54), with the live StyleGuide "Underline Tabs" specimen as the design-system gate. Verified all worker-facing anchors still resolve (tabBar:54, activitySection:1800, allTabView:1815, heroSpotlight:911, gamesInFocusPosterSection:1111, newGamesSection:1261, placeholderTab:4248). No split, no ADR.
**Split into:** none
**ADRs written:** none

---

## 2026-07-06 14:46 -- Work session ended

**Type:** Work / Session end
**Duration:** ~11m (Batch started 14:35 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-k9p3v: 1
**Commits:** 3 (1 batch-start claim + 1 task commit + this session-end line)
**Vision-conformance:** none — batch aligns with vision (the underline-tab pattern directly serves the "Unified Dashboard" v1 tab strip and the "Unified, not siloed — one dashboard with tabs" design principle; pulls toward no Out-of-Scope item)
**Carry-over:** src/Client/index.css: left behind (owner: user's own WIP — the `.status-badge` sizing/weight tweak "per 3d system-board reference", carried uncommitted since before this session; preserved via stash-around-squash so the underline-tab squash-merge into the same file couldn't clobber it — the two regions are disjoint). "Mediatheca Directions.html": left behind (owner: user's design reference doc — the 912KB captured Claude design session cited by tasks' Notes, untracked by the user since before this session, not project bookkeeping). .worktrees/design-system-k9p3v: discarded (orphan teardown residue — held only regenerable MSBuild `obj/Debug/net9.0` AssemblyInfo/cache files after `git worktree remove` deregistered it; no source, no task files, no uncommitted work).
**Note:** Single isolated single-task design-system batch under worktree isolation (ADR-0032), PASS first try. **k9p3v** shipped the reusable **underline-tab** pattern — the header-tab sibling of the dir-3a sidebar nav: `DesignSystem.underlineTab`/`underlineTabActive`/`underlineTabInactive`/`underlineTabClass isActive` (modeled on the `navItem*` family), backed by `.underline-tab::after`/`.underline-tab-active::after` in `index.css` drawing a gold underline via the existing `--color-gold` token (no new colour), a live StyleGuide "Underline Tabs" specimen (1 active + 3 inactive), and a design-system README ubiquitous-language entry. No ADR (net-new additive pattern, nothing superseded — per the task's own latitude note). `npm run build` clean. No consumer migrated: the Dashboard `tabBar` re-point is `intelligence-dq8rk`'s job, which `depends_on` this task and is now unblocked — it remains in **intelligence backlog** (promotion is a `modeling` call, not `work`'s). No conflicts, no concept candidates, no new backlog items. The design-system board is quiescent again: backlog / todo / doing all empty, done: 14.

---

## 2026-07-06 14:45 -- Task verified and completed: design-system-k9p3v - Underline tab pattern — reusable DesignSystem component (dir 3a header tabs)

**Type:** Work / Task completion
**Task:** design-system-k9p3v - Underline tab pattern — reusable DesignSystem component (dir 3a header tabs)
**Summary:** Add reusable underline-tab pattern (gold-underline text tabs, no filled-pill chrome) to the design system
**Duration:** ~8m
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-06 14:35 -- Batch started: [design-system-k9p3v]

**Type:** Work / Batch start
**Tasks:** design-system-k9p3v - Underline tab pattern — reusable DesignSystem component (dir 3a header tabs)
**Parallel:** no (1 worker — sole ready task; consumer intelligence-dq8rk is blocked on it)

---

## 2026-07-06 13:46 -- Modeling / Refined: intelligence-dq8rk - Dashboard All-tab 3a layout

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** backlog (blocked on new dependency design-system-k9p3v)
**Summary:** Resolved the three open interpretation points with the user. (1) **Hero spotlight — dropped:** the TV Series row is a pure full-width equal-weight poster row, no featured lead card. (2) **Tab restyle — promoted to a reusable design-system pattern** (mirrors the 3a sidebar → ADR-0014 call): split off a new design-system task for the underline-tab component; this task now *consumes* it and gains `depends_on: design-system-k9p3v`. (3) **New Games — dropped from the All tab** (recently-added games stay on the Games tab). Also baked in a concrete cross-MVU finding for the worker: the library search modal lives in the **root** MVU (`model.SearchModal`), so the header "Search your library" control must follow the sibling-page pattern (dispatch a `Open_search_modal` message that root `State.fs` intercepts) rather than reimplement search. Acceptance criteria + What/Notes rewritten to match; open-interpretation section removed. Stays in backlog because its new dependency is not yet done.
**Split into:** design-system-k9p3v (Underline tab pattern — reusable DesignSystem component; authored ready → filed to design-system **todo**, `depends_on: design-system-001` [done], `blocks: intelligence-dq8rk`)
**ADRs written:** none

---

## 2026-07-06 13:36 -- Modeling / Captured: intelligence-dq8rk - Dashboard All-tab 3a layout

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** backlog
**Summary:** Rework the Dashboard All-tab to direction 3a: drop the "Dashboard" title, restyle the four tabs to text-with-orange-underline on one header line with an inline "Search your library" (wired to the existing SearchModal/searchLibrary), strip the Activity section (heatmap + monthly breakdown) and the yearly play/watch-time summary, and lay out a full-width TV Series row → full-width Movies row → two-column Games (left) / Books-placeholder (right, replacing 3a's recently-played). Frontend task; depends_on the design-system styleguide gate (design-system-001, done). Left in backlog — a few interpretive points open (hero spotlight fate, whether the tab restyle spins off a reusable design-system pattern, New Games section survival).

---

## 2026-07-03 15:24 -- Work session ended

**Type:** Work / Session end
**Duration:** ~35m (first Batch started 14:49 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-pv3mq: 1
**Commits:** 3 (1 batch-start claim + 1 task commit + this session-end line)
**Vision-conformance:** none — batch aligns with vision (a pure visual-material refactor, orthogonal to every v1 success criterion; dropping GPU-heavy `backdrop-filter` if anything aids the mobile-first principle)
**Carry-over:** src/Client/index.css: left behind (owner: user's own WIP, `.status-badge` sizing/weight tweak "per 3d system-board reference" — appeared after the 12:15 session end, unrelated to this task's overlay region; preserved uncommitted via stash-around-squash so the squash-merge couldn't clobber it). "Mediatheca Directions.html": left behind (owner: user's design reference doc — the 912KB captured Claude design session, untracked by the user since before this session, not project bookkeeping). No worktree orphans (`.worktrees/design-system-pv3mq` residual held only 3 regenerable MSBuild `obj/Debug/net9.0` AssemblyInfo files after `git worktree remove` deregistered it — discarded; `.worktrees/` now removed).
**Note:** Single isolated single-task design-system batch under worktree isolation (ADR-0032), PASS first try. **pv3mq** completed the Velvet Lobby re-skin's last open thread — retired glassmorphism as the floating-surface vocabulary and replaced it with **paper overlay** (opaque `--color-paper` fill + `--color-line` ring + true `--shadow-paper` elevation, no translucency/backdrop-filter, distinct from `.velvet-card`). Ripped out `--glass-*`, `.glass-card`, `.media-chrome-glass`, and the `glass*` DesignSystem compositions; repointed every dropdown/popover/modal/panel across Components + detail/list pages to `paperOverlay`/`paperDropdown`; rewrote the StyleGuide specimen (Glassmorphism → PaperOverlay). Wrote **ADR-0016** (design-system scope) superseding global **ADR-0006** bidirectionally — 0006's Tailwind/DaisyUI/dim-theme decisions still stand; only its mandatory-glassmorphism overlay rule is retired. Repointed CLAUDE.md (§ Conventions paper bullet; § Gotchas backdrop-filter trap removed as no overlay blurs anymore), context-map, design-system README, design-check skill, and (conductor-owned) `knowledge/index.md`. The one open decision — replacement material — was resolved with the user at dispatch: option (a) paper elevation (distinct), not (b) reuse `.velvet-card`. `npm run build` clean; grep-clean of `backdrop-filter`/`backdrop-blur`/`.glass-*` in `src/Client/`. No conflicts, no concept candidates, no new backlog items. The design-system board is fully quiescent again: backlog / todo / doing all empty, done: 13.

---

## 2026-07-03 15:22 -- Task verified and completed: design-system-pv3mq - Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)

**Type:** Work / Task completion
**Task:** design-system-pv3mq - Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)
**Summary:** Retired glassmorphism as the overlay vocabulary — replaced with **paper overlay** (opaque `--color-paper` fill + `--color-line` ring + true `--shadow-paper` elevation, no translucency/backdrop-filter, distinct from `.velvet-card`). Removed `--glass-*` tokens, `.glass-card`, `.media-chrome-glass`, and the `glassCard/glassOverlay/glassSubtle/glassDropdown/mediaChromeGlass` compositions; rewrote `.rating-dropdown` to paper; repointed every dropdown/popover/modal/panel across Components + detail/list pages to `paperOverlay`/`paperDropdown`; consolidated non-overlay "glass" page-chrome onto `velvetCard`. Rewrote the StyleGuide specimen section (Glassmorphism → PaperOverlay). Wrote superseding **ADR-0016** (design-system scope; supersedes global ADR-0006 bidirectionally — Tailwind/DaisyUI/dim-theme parts of 0006 still stand). Repointed CLAUDE.md (§ Conventions paper-overlay bullet; § Gotchas backdrop-filter trap removed as no overlay blurs anymore), context-map, design-system README, and the design-check skill. Conductor repointed the top-level `knowledge/index.md` (design-system BC description + ADR-0006 line marked superseded) — that file is conductor-owned so the worker correctly left it.
**Duration:** ~33m (dispatch 14:49 → verifier verdict 15:22)
**Verification:** PASS (iteration 1)
**Files changed:** 24
**Tests added:** 0 (visual/CSS refactor — verified via clean `npm run build` + grep-clean sweep of `backdrop-filter`/`backdrop-blur`/`.glass-*`)
**ADRs written:** 0016

---

## 2026-07-03 14:49 -- Batch started: [design-system-pv3mq]

**Type:** Work / Batch start
**Tasks:** design-system-pv3mq - Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)
**Parallel:** no (1 worker — sole ready task; task's own Notes ask for an isolated single-task batch since it rewrites shared overlay vocabulary consumed by many BCs' views)
**Planning advisory:** none (`.agentheim/state/whats-next.md` absent)

Open decision resolved at dispatch: replacement overlay material = **(a) paper elevation (distinct)** — opaque fill + elevation shadow + line ring, no translucency/backdrop-filter, kept distinct from `.velvet-card`. Recorded in the task file's Notes for the worker to bake into the superseding ADR-0006.

---

## 2026-07-03 14:40 -- Modeling / Promoted: design-system-pv3mq - Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)

**Type:** Modeling / Promote
**BC:** design-system
**From → To:** backlog → todo
**Summary:** Promoted the glassmorphism-retirement refactor to todo. Task has concrete acceptance criteria (grep-clean of backdrop-filter, paper/material overlays, StyleGuide section replaced, clean `npm run build`, ADR-0006 superseded + docs repointed, design-check retargeted) and no unmet dependencies. One open decision left for the worker to bake into the superseding ADR: the exact replacement material — (a) paper/solid elevation vs (b) reuse `.velvet-card` — the user's framing leans (a). Note: `task-lifecycle-cli.mjs promote` moved the file + updated the frontmatter `status` but threw on the INDEX edit (CLI marker regex expects LF; this repo's INDEX/protocol are CRLF), so the INDEX/count/protocol/commit bookkeeping was completed by hand.

---

## 2026-07-03 13:30 -- Modeling / Captured: design-system-pv3mq - Retire glassmorphism — overlays become paper/solid material (supersede ADR-0006)

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** backlog
**Summary:** User: "we don't use glassmorphism anymore — this is a paper-style material design at best." Captured as full retirement of glassmorphism as the overlay vocabulary — rip out the `glass*` compositions (`DesignSystem.fs`), `.glass-card`/`.rating-dropdown`/`.media-chrome-glass` + `--glass-*` (`index.css`), restyle every dropdown/popover/modal to a solid paper/material surface, remove the StyleGuide glass section, and supersede ADR-0006 (global) plus repoint CLAUDE.md / context-map / index / README / design-check skill. Left under-refined in backlog: the exact replacement material (paper/solid elevation vs. reuse `.velvet-card`) is an open decision for a refine pass — the superseding ADR is worker output per the sg8kd/grtw7 precedent.

---

## 2026-07-03 12:15 -- Work session ended

**Type:** Work / Session end
**Duration:** ~28m (first Batch started 11:47 → now)
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-grtw7: 1, design-system-sg8kd: 1
**Commits:** 5 (2 batch-start claims + 2 task commits + this session-end line)
**Carry-over:** Mediatheca Directions.html: left behind (owner: user's design reference doc — the 912KB captured Claude design session cited by tasks' Notes; untracked by the user since before this session, not project bookkeeping). Two leftover `.worktrees/` dirs (design-system-grtw7 from this session, design-system-fq3vp orphaned by the 09:xx session's cleanup): discarded — both held only regenerable MSBuild `obj/Debug/net9.0` build residue left behind after `git worktree remove` deregistered them (no source, no task files, no uncommitted work); `.worktrees/` now empty and removed.
**Note:** Two sequential single-task design-system batches under worktree isolation (ADR-0032), each PASS first try. **grtw7** reverted the sidebar active tab from ADR-0013's ivory placard + concave corner-notch back to dir 3a's burgundy fill (`oklch(0.22 0.035 25)`) + gold inset-left bar + gold ◆ icon, removed the ivory tokens and corner-notch machinery cleanly, landed the non-contentious 3a deltas (tagline, item metrics, one-step-smaller bottom group), and wrote **ADR-0014** superseding 0013 (bidirectional). **sg8kd** was promoted backlog→todo by a concurrent `modeling` session *during* grtw7's run (committed cleanly as 67857df between grtw7's batch-start and completion — picked up automatically on the next re-scan once its `depends_on: [grtw7]` cleared); it wrote **ADR-0015** superseding 0009, archived `styleguide.md` → `.workflow.archived/`, and repointed CLAUDE.md + the design-check skill + the design-system README to the **live in-app StyleGuide page** (backed by `DesignSystem.fs` + `index.css`) as the authoritative design-system artifact — the frontend gate redefined as "conform to the living system, reviewed on the running StyleGuide page" (anchor stays design-system-001). No conflicts (sequential single-task batches). No concept candidates, no new backlog items. The design-system board is now fully quiescent: backlog / todo / doing all empty, done: 12. The full-redesign human sign-off remains the natural next step for the user — a running-app review of the now-complete Velvet Lobby system, including the reverted 3a burgundy sidebar.

---

## 2026-07-03 12:12 -- Task verified and completed: design-system-sg8kd - Retire styleguide.md — in-app StyleGuide page is authoritative

**Type:** Work / Task completion
**Task:** design-system-sg8kd - Retire styleguide.md — the in-app StyleGuide page is the authoritative artifact (supersede ADR-0009)
**Summary:** Wrote **ADR-0015** superseding ADR-0009 (bidirectional): the live in-app StyleGuide page — backed by `DesignSystem.fs` + `index.css` — is now the authoritative design-system artifact, styleguide.md is retired, and the frontend gate is redefined as "conform to the living system, reviewed on the running StyleGuide page" (anchor stays `design-system-001`). Archived `styleguide.md` → `.workflow.archived/styleguide.md` (not deleted). Repointed CLAUDE.md § Conventions, the design-check skill's `design-rules.md` "Source of Truth", and the design-system README (Existing assets + gate section, stale `todo/` path fixed). Confirmed the glassmorphism spec + backdrop-filter gotcha remain intact in CLAUDE.md.
**Duration:** ~6m (dispatch 12:06 → verifier verdict)
**Verification:** PASS (iteration 1)
**Files changed:** 7
**Tests added:** 0
**ADRs written:** 0015

---

## 2026-07-03 12:06 -- Batch started: [design-system-sg8kd]

**Type:** Work / Batch start
**Tasks:** design-system-sg8kd - Retire styleguide.md — the in-app StyleGuide page is the authoritative artifact (supersede ADR-0009)
**Parallel:** no (1 worker)

---

## 2026-07-03 10:20 -- Modeling / Refined: design-system-sg8kd - Retire styleguide.md — in-app StyleGuide page is authoritative (supersede ADR-0009)

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Settled the three open decisions on this `type: decision` task and promoted backlog → todo. (1) Frontend gate **redefined around the living system** (`DesignSystem.fs` + `index.css` + in-app StyleGuide page, reviewed on the running page); frontend tasks still `depends_on` a design-system task (anchor stays `design-system-001`). (2) Glassmorphism spec + backdrop-filter gotcha **pre-verified present in CLAUDE.md** (lines 49/64) independent of styleguide.md — nothing lost. (3) styleguide.md **archived** to `.workflow.archived/`, not deleted (user-sanctioned exception to that folder's read-only guardrail). Rewire targets narrowed to CLAUDE.md L50, design-check `design-rules.md` Source-of-Truth, and the design-system README; ADR-0009 to be superseded (new ADR ~0014). New sequencing edge added: `depends_on: [design-system-grtw7]` — grtw7 is in `doing/` and still writes styleguide.md § 4 in lockstep, so sg8kd must run after it lands; reciprocal `blocks` on grtw7 deliberately deferred (in-flight worker owns the file). Concrete acceptance criteria finalized.
**Split into:** none
**ADRs written:** none (superseding ADR is worker output; new ADR marks ADR-0009 `superseded_by`)

---

## 2026-07-03 12:04 -- Task verified and completed: design-system-grtw7 - Sidebar nav — align with dir 3a (full revert to burgundy active tab)

**Type:** Work / Task completion
**Task:** design-system-grtw7 - Sidebar nav — align with dir 3a (full revert to burgundy active tab, supersedes ADR-0013)
**Summary:** Reverted the sidebar active-tab from ADR-0013's ivory placard + concave corner-notch back to dir 3a's burgundy fill (`oklch(0.22 0.035 25)`) + gold inset-left bar (`--ring-active`) + gold ◆ icon; removed the ivory tokens and the entire corner-notch machinery (no dead CSS); landed the non-contentious 3a deltas (tagline "Where entertainment lives", item metrics, one-step-smaller bottom group); kept `w-64` rail, no profile chip. Wrote superseding **ADR-0014** (supersedes 0013; 0013 `superseded_by: [0014]`, bidirectional). styleguide.md § 4, README, and the StyleGuide page specimen updated in lockstep.
**Duration:** ~17m (dispatch 11:47 → verifier verdict)
**Verification:** PASS (iteration 1)
**Files changed:** 9
**Tests added:** 0
**ADRs written:** 0014

---

## 2026-07-03 11:47 -- Batch started: [design-system-grtw7]

**Type:** Work / Batch start
**Tasks:** design-system-grtw7 - Sidebar nav — align with dir 3a (full revert to burgundy active tab, supersedes ADR-0013)
**Parallel:** no (1 worker)

---

## 2026-07-03 10:05 -- Modeling / Refined: design-system-grtw7 - Sidebar nav — align with dir 3a (full revert to burgundy active tab)

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Resolved the single gating question that kept grtw7 in backlog — the ADR-0013 ivory-placard vs. 3a-burgundy active-tab conflict. Re-asked the user (the 2026-07-03 AskUserQuestion had gone unanswered); user chose **full 3a revert**: burgundy fill `oklch(0.22 0.035 25)` + gold inset-left bar (`--ring-active`) + gold ◆ icon, removing the ivory tokens and the concave corner-notch machinery. This **supersedes ADR-0013** — the superseding ADR is a worker acceptance criterion (written in lockstep with the code removal, not now, to avoid desyncing the ADR from still-shipped ivory). Secondary decisions: keep rail width at `w-64`/256px (3a's 216px not adopted); skip the profile chip (single-user app). Baked option 1 + the non-contentious 3a deltas into concrete acceptance criteria and promoted backlog → todo.
**Split into:** none
**ADRs written:** none (superseding ADR deferred to the worker)

---

## 2026-07-03 09:45 -- Work session ended

**Type:** Work / Session end
**Duration:** ~10m (Batch started 09:35 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-fq3vp: 1
**Commits:** 3 (1 batch-start claim + 1 task + this session-end line)
**Carry-over:** Mediatheca Directions.html: left behind (owner: user's design reference doc — the 912KB captured Claude design session cited by every task's Notes; untracked by the user since before this session, not project bookkeeping)
**Note:** Single-task batch under worktree isolation (ADR-0032). design-system-fq3vp (compact on-poster "✦ Focus" pill) shipped and passed verification first try — clean squash-merge, no conflicts (only ready task this run, so no sibling to collide with). `DesignSystem.inFocusPill` / `.in-focus-pill` is the deliberately-solid third member of the In-focus family (badge `.gold-sweep`, poster `inFocusFrame` animated; pill solid), resolving the sweep-vs-solid tension the refine flow locked to solid. The design-system board is now quiescent: todo empty, doing empty; backlog holds only grtw7 (sidebar 3a alignment, still gated on the ADR-0013 ivory-vs-3a-burgundy tension). No concept candidates, no new backlog items, no ADRs this run. The full-redesign human sign-off flagged in styleguide.md's Sign-off section remains open — a running-app review of the now-complete Velvet Lobby tokens/typography/patterns/sidebar/filmstrip/in-focus motion (now including the pill) is the natural next step for the user.

---

## 2026-07-03 09:44 -- Task verified and completed: design-system-fq3vp - Compact on-poster "✦ Focus" pill (3c grid badge variant)

**Type:** Work / Task completion
**Task:** design-system-fq3vp - Compact on-poster "✦ Focus" pill (3c grid badge variant)
**Summary:** Shipped `DesignSystem.inFocusPill` / `.in-focus-pill` — the compact 3c grid-badge variant (8.5px/700/0.18em uppercase "✦ Focus", dark ink on solid gold), a genuinely separate composition from `statusBadge InFocus`, deliberately solid (no `.gold-sweep`, no new keyframe) for motion economy against the co-occurring animated `inFocusFrame`. Added a StyleGuide specimen pairing the pill with `inFocusFrame` on a poster; updated styleguide.md § 4 Poster grid + Motion discipline and README.md in lockstep.
**Duration:** ~9m (dispatch → verifier verdict)
**Verification:** PASS (iteration 1)
**Files changed:** 5
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-03 09:35 -- Batch started: [design-system-fq3vp]

**Type:** Work / Batch start
**Tasks:** design-system-fq3vp - Compact on-poster "✦ Focus" pill (3c grid badge variant)
**Parallel:** no (1 worker)

---

## 2026-07-03 09:20 -- Modeling / Refined: design-system-fq3vp - Compact on-poster "✦ Focus" pill (3c grid badge variant)

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Resolved the one open decision the capture deferred — sweep-vs-solid for the compact 3c grid pill. User chose **solid gold** (3c literal spec), not the animated gold-leaf sweep: the pill always co-occurs with the already-animated `inFocusFrame` border directly behind it, so a solid fill keeps one motion focal point per poster (motion economy), and a static gold fill is already an accepted In-focus signal (it's the reduced-motion fallback for the existing sweep carriers). Named the new composition `inFocusPill` / `.in-focus-pill` (poster-grid sibling of `inFocusFrame`). Locked the resolutions into acceptance criteria (added an explicit "no `.gold-sweep`" criterion and a styleguide § 4 Motion-discipline rationale criterion) and promoted backlog → todo.
**Split into:** none
**ADRs written:** none

---

## 2026-07-03 09:12 -- Work session ended

**Type:** Work / Session end
**Duration:** ~20m (first Batch started 08:52 → now)
**Completed:** 3 (first-try PASS: 3, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-snpnv: 1, design-system-bky6v: 1, design-system-wd5zk: 1
**Commits:** 5 (1 batch-start claim + 3 task + this session-end line)
**Carry-over:** Mediatheca Directions.html: left behind (owner: user's design reference doc — the 912KB captured Claude design session cited by every task's Notes; untracked since before this session)
**Note:** First fully-parallel 3-worker batch under worktree isolation (ADR-0032). All three design-system tasks (snpnv typography 3c type scale, wd5zk filmstrip 3a proportions, bky6v in-focus gold-sweep fix) shipped and passed verification first try. Because all three edited the same shared files (`DesignSystem.fs`, `index.css`, `styleguide.md`, StyleGuide specimen), merges were serialized: snpnv merged clean; wd5zk auto-merged clean (non-overlapping regions), fully-merged main re-built clean before commit; bky6v hit ONE real conflict in styleguide.md — a keep-both changelog collision where bky6v and snpnv each appended a "Shipped (…)" block at the same sign-off insertion point. Surfaced to the user per ADR-0032's never-auto-guess rule; user chose keep-both; resolved (both blocks stacked), fully-merged main re-built clean, committed. bky6v also filed a new backlog item **design-system-fq3vp** (compact on-poster "✦ Focus" pill — the 3c grid-badge variant it deferred rather than building). The design-system board is now quiescent: todo empty, doing empty; backlog holds grtw7 (sidebar 3a alignment, gated on the ADR-0013 ivory-vs-burgundy tension) and fq3vp. No concept candidates surfaced this run. The full-redesign human sign-off flagged in styleguide.md's Sign-off section remains open — a good next step for the user (a running-app review of the now-complete Velvet Lobby tokens/typography/patterns/sidebar/filmstrip/in-focus motion).

---

## 2026-07-03 09:11 -- Task verified and completed: design-system-bky6v - In-focus signifiers must animate — gold sweep

**Type:** Work / Task completion
**Task:** design-system-bky6v - In-focus signifiers must animate — gold sweep on status badge and poster frame
**Summary:** Root-caused the static In-focus badge to `.status-badge`'s `background: transparent` shorthand clobbering `.gold-sweep`'s `background-image` (fixed via `background-color: transparent`, confirmed in compiled CSS); replaced the static `.in-focus-frame` ring with a two-layer animated sweeping-gradient border + glow (`inFocusFrame` signature unchanged); added `prefers-reduced-motion` freezing for both sweep carriers. Deferred the compact on-poster "✦ Focus" pill to new backlog item design-system-fq3vp.
**Duration:** ~19m (dispatch → verifier verdict; +merge-conflict resolution)
**Verification:** PASS (iteration 1)
**Merge note:** squash-merge to main hit one real conflict in styleguide.md (bky6v vs snpnv each appended a "Shipped (…)" changelog block at the same insertion point). Surfaced per ADR-0032; user chose keep-both; conflict resolved (both blocks stacked), fully-merged main re-built clean before commit.
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none
**New backlog items:** design-system-fq3vp (compact on-poster "✦ Focus" pill)

---

## 2026-07-03 09:07 -- Task verified and completed: design-system-wd5zk - Movies filmstrip — full-width 3a proportions

**Type:** Work / Task completion
**Task:** design-system-wd5zk - Movies filmstrip — full-width 3a proportions (flex-1 posters, ~196px tall)
**Summary:** Reworked the Movies filmstrip pattern from a 64px thumbnail row to 3a's full-width cinematic proportions — flex-1 196px-tall posters, 8px sibling sprocket bars with 7px clearance, mirrored flex-1 caption row; `filmstripRow` signature unchanged.
**Duration:** ~15m (dispatch → verifier verdict)
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-03 09:04 -- Task verified and completed: design-system-snpnv - Typography — adopt dir 3c's list-page type scale

**Type:** Work / Task completion
**Task:** design-system-snpnv - Typography — adopt dir 3c's list-page type scale (grid captions, page header, filter pills)
**Summary:** Minted the 3c list-page type tiers (grid caption pair, list-page header+count baseline pairing, filter-pill active/inactive) as additions to the semantic type scale — styleguide.md § 2, typed helpers in DesignSystem.fs, live StyleGuide specimen; existing `cardTitle`/ink ladder untouched.
**Duration:** ~12m (dispatch → verifier verdict)
**Verification:** PASS (iteration 1)
**Files changed:** 5
**Tests added:** 0
**ADRs written:** none

---

## 2026-07-03 08:52 -- Batch started: [design-system-snpnv, design-system-bky6v, design-system-wd5zk]

**Type:** Work / Batch start
**Tasks:** design-system-snpnv - Typography — adopt dir 3c's list-page type scale; design-system-bky6v - In-focus signifiers must animate — gold sweep on status badge and poster frame; design-system-wd5zk - Movies filmstrip — full-width 3a proportions
**Parallel:** yes (3 workers)

---

## 2026-07-03 -- Modeling / Captured: design-system-snpnv, design-system-bky6v, design-system-wd5zk, design-system-grtw7 - Velvet Lobby drift vs design doc (dirs 3a/3c)

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo (snpnv, bky6v, wd5zk), backlog (grtw7)
**Summary:** User reviewed the captured Claude design session (`Mediatheca Directions.html`) and flagged four gaps between the shipped Velvet Lobby implementation and dirs 3a/3c: (1) snpnv — styleguide § 2 lacks 3c's list-page type tiers (sans 12px/600 grid card titles, 10.5px meta, 34px serif page header + baseline mono count, 11.5px filter pills); (2) bky6v — the InFocus status badge doesn't visibly run the gold sweep and `.in-focus-frame` is a static ring where 3c animates both; (3) wd5zk — the shipped filmstrip's `w-16` tiles are far below 3a's flex-1 / 196px-tall posters; (4) grtw7 — sidebar should align with 3a, filed to backlog because "look like 3a" collides with ADR-0013's explicit ivory-tab override (AskUserQuestion went unanswered; gating question documented in the task). All literal values extracted from the doc into the tasks so workers need not parse the 912KB archive.

---

## 2026-07-03 -- Work session ended

**Type:** Work / Session end
**Duration:** ~22m (first Batch started → now)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-t4b9k: 2
**Commits:** 1 (design-system-t4b9k task commit; this session-end line committed separately)
**Carry-over:** none — working tree clean
**Note:** design-system-t4b9k (layered sidebar nav) shipped. Worker's first pass was substantively correct — verifier caught one defect (the new ADR-0013 was missing its YAML frontmatter, which the index step reads `scope:` from), FAILed iteration 1, re-dispatched a scoped frontmatter-only fix, PASSed iteration 2. Reworked `Components/Sidebar.fs` into top/bottom nav groups (Events/Settings pinned via `mt-auto`), replaced the flat `bg-primary/10`/`.nav-glow` active state with a raised **ivory** active-tab layer (`--color-nav-active-bg` `oklch(0.94 0.02 75)`, dark-burgundy ink, gold icon) joined to the rail/content boundary by a concave corner-notch (two radial-gradient corner masks) — an explicit user override of the design brief's burgundy fill, pinned in ADR-0013. New tokens in `index.css`, typed `navItemClass`/`navItemActiveIconClass`/`navGroupTop`/`navGroupBottom` in `DesignSystem.fs`, live StyleGuide specimen, `styleguide.md` §§ 0/4/7 + README in lockstep. Clean `npm run build`. The design-system `todo/` is now empty and the whole board is quiescent. No new backlog items, no concept candidates surfaced this run. The full-redesign human sign-off flagged in styleguide.md's Sign-off section (running-app review of tokens/typography/patterns/now-sidebar) remains open — a good next step for the user.

---

## 2026-07-03 -- Task verified and completed: design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary

**Type:** Work / Task completion
**Task:** design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary
**Summary:** Reworked the desktop rail into a top/bottom grouped nav (`mt-auto` bottom pin) with a raised **ivory** active-tab layer (dark-burgundy ink, gold icon) joined to the rail/content boundary by a concave corner-notch (two radial-gradient corner masks); retired the old `.nav-glow` left-edge bar and `bg-primary/10` active state. New tokens in `index.css`, typed helpers in `DesignSystem.fs`, live StyleGuide specimen, styleguide/README in lockstep (ADR-0013).
**Duration:** ~15m (dispatch → verdict, incl. 1 re-dispatch for ADR frontmatter)
**Verification:** PASS (iteration 2)
**Files changed:** 7
**Tests added:** 0
**ADRs written:** 0013

---

## 2026-07-03 -- Verification failed: design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary

**Type:** Work / Verification failure
**Task:** design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary
**Iteration:** 1 of 3
**Reasons:** new ADR 0013 is missing its YAML frontmatter block (id/title/scope/status/date) — breaks the ADR indexing convention (cf. 0006/0009). All code criteria passed (ivory/ink/icon tokens exact, top/bottom `mt-auto` split, concave corner-notch, `.nav-glow`/`bg-primary/10` retired, lockstep artifacts, clean `npm run build`).
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-07-03 -- Batch started: [design-system-t4b9k]

**Type:** Work / Batch start
**Tasks:** design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary
**Parallel:** no (1 worker)

---

## 2026-07-03 -- Modeling / Refined: design-system-t4b9k - Layered sidebar nav — ivory active tab, curved-corner boundary

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Resolved the gating palette tension with the user present. Grounded against the captured design doc via DesignSync: dir 3a's Velvet Lobby active item is actually a *burgundy* raised layer (`oklch(0.22 0.035 25)` + gold `inset 2px 0 0` bar), **not** white — the white-tab ask came only from the pasted reference image. User confirmed the active tab lands as a warm **ivory** surface (`oklch(0.94 0.02 75)`, gold family) with dark-burgundy ink + gold icon — in-palette, not literal `#fff`. Curved/inverted-corner boundary stays a single in-task worker spike (technique chosen at build time, ADR only if non-obvious) — no split, no v1 descope. Task title, What point 2, and the active-tab acceptance criterion updated to ivory; palette-tension Note flipped to RESOLVED. Both gating unknowns closed → **promoted to `todo/`**.
**Split into:** none
**ADRs written:** none

---

## 2026-07-02 19:20 -- Modeling / Captured: design-system-t4b9k - Layered sidebar nav — white active tab, curved-corner boundary

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** backlog
**Summary:** Rework the desktop sidebar to the "Mediatheca Directions" doc's layered treatment — a white/light active tab that reads as a raised layer, a curved (inverted-corner) boundary where the active tab meets the content edge, and a top/bottom split with Events + Settings pinned to the foot of the rail. Filed to backlog because it changes the design language (styleguide gate) and the white-tab choice has a palette tension to resolve with the user first.

---

## 2026-07-02 18:53 -- Work session ended

**Type:** Work / Session end
**Duration:** ~19m (first Batch started 18:34 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-h3q8n: 1
**Commits:** 2 (design-system-h3q8n task commit; a carry-over reconciliation commit; this session-end line committed separately)
**Carry-over:** .gitignore: committed (reconcile stranded doc/config edits — ignore `.agentheim/.dashboard`); CLAUDE.md: committed (reconcile stranded doc/config edits — DB data-dir path correction to `DATA_DIR`/`~/app/mediatheca`, matches `src/Server/Program.fs`). Both were pre-existing user WIP carried across prior sessions; user present, chose "commit both" — the recurring carry-over leak is now closed.
**Note:** design-system-h3q8n (Velvet Lobby component patterns & motion) shipped and verified first-try. Ported the § 1.3–1.6 tokens (spacing/radii/shadows/animation incl. `--sweep` + gold-leaf sweep `@keyframes`), added the velvet-card (§ 3.1) and media-chrome-glass (§ 3.3) surfaces distinct from the unchanged overlay glass (§ 3.2, ADR-0006), and added nine component patterns (hero card, filmstrip row, secondary card, In-focus poster frame, six-state lifecycle status badges, segmented + continuous progress, star rating, section header, list row) plus three motion primitives (In-focus-only gold-leaf sweep, 400ms leave-transition, 200ms cross-fade) as typed Feliz compositions in `DesignSystem.fs`, with live StyleGuide specimens and `styleguide.md`/README in lockstep (ADR-0009). Clean `npm run build`. The design-system `todo/` is now empty and the whole board is quiescent. The worker surfaced a new games-BC backlog item **games-status-vocabulary-reconcile** (reconcile the games `GameStatus` type with the design system's six-state `LifecycleStatus` vocabulary — a decision task). Concept candidate **velvet-lobby-design-language** converging across r7k2m + h3q8n + ADR-0006 + ADR-0009 — user may want a concept page.

---

## 2026-07-02 18:51 -- Task verified and completed: design-system-h3q8n - Velvet Lobby re-skin — component patterns & motion

**Type:** Work / Task completion
**Task:** design-system-h3q8n - Velvet Lobby re-skin — component patterns & motion
**Summary:** Shipped the Velvet Lobby § 1.3–1.6 tokens (spacing/radii/shadows/animation incl. `--sweep` + the gold-leaf sweep `@keyframes`), the velvet-card (§ 3.1) and media-chrome-glass (§ 3.3) surfaces, and nine component patterns plus three motion primitives (gold-leaf sweep In-focus-only, 400ms leave-transition, 200ms cross-fade) as typed Feliz compositions in `DesignSystem.fs`, with live StyleGuide specimens and `styleguide.md`/README kept in lockstep. Overlay glass (§ 3.2, ADR-0006) left untouched.
**Duration:** ~17m (worker ~12m + verify ~2m; dispatch 18:34 → verdict 18:51)
**Verification:** PASS (iteration 1)
**Files changed:** 6 (+1 new games backlog item)
**Tests added:** 0 (presentational token/CSS/typed-view layer — no assertable pure logic, no frontend unit-test suite; verified via clean `npm run build`, `✓ built in 38.73s`)
**ADRs written:** none

---

## 2026-07-02 18:34 -- Batch started: [design-system-h3q8n]

**Type:** Work / Batch start
**Tasks:** design-system-h3q8n - Velvet Lobby re-skin — component patterns & motion
**Parallel:** no (1 worker)

---

## 2026-07-02 19:20 -- Modeling / Refined: design-system-h3q8n - Velvet Lobby re-skin — component patterns & motion

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Second refinement, user present, now that foundation [[design-system-r7k2m]] has shipped. User re-confirmed the three prior defaults (kept **cohesive / not-split**, typed `DesignSystem.fs` home, motion-**primitives-only**). Cross-checking against the shipped code surfaced a **token gap**: `styleguide.md` § 0 & § 7 carved the **§ 1.3–1.6 tokens** (spacing / radii / shadows / animation incl. the gold-leaf sweep `@keyframes`) plus the velvet-card (§ 3.1) and media-chrome-glass (§ 3.3) surfaces out of r7k2m and into this task, but the acceptance criteria assumed they already existed. Made that explicit as *step zero* in **What** and added two acceptance criteria (port § 1.3–1.6 tokens; velvet-card + media-chrome-glass helpers, distinct from the unchanged overlay glass). Dependency met + criteria concrete → **promoted to `todo/`**. Full running-app sign-off on the redesign stays open (user reviews foundation + components together when specimens land).
**Split into:** none
**ADRs written:** none

---

## 2026-07-02 19:10 -- Work session ended

**Type:** Work / Session end
**Duration:** ~25m (first Batch started 18:45 → now)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-r7k2m: 1
**Commits:** 1 (design-system-r7k2m; this session-end line committed separately)
**Carry-over:** .gitignore: left behind (owner: user WIP, pre-existing at session start — adds `.agentheim/.dashboard`; surfaced for disposition, user away, safe default B applied); CLAUDE.md: left behind (owner: user WIP, pre-existing DB data-dir path doc correction confirmed intentional by harness; the in-scope r7k2m fonts hunk was committed separately via staged hunk-split, DB-path hunk left unstaged; surfaced for disposition, user away, safe default B applied)
**Note:** design-system-r7k2m (Velvet Lobby re-skin — tokens & type foundation) shipped and verified first-try. Replaced the `dim` palette in place with the burgundy-black/gold oklch tokens, swapped Oswald/Inter for Instrument Serif/Instrument Sans/Spline Sans Mono (self-hosted `@fontsource`), retargeted `DesignSystem.fs` + the StyleGuide page, re-tinted the glass overlays (ADR-0006 rule kept fully in force), and reconciled `styleguide.md` as the gate source of truth. Clean `npm run build`. The pre-existing working-tree `styleguide.md` Velvet Lobby draft (criterion #5, ~340 lines) was folded into the task commit. Sibling **design-system-h3q8n** (component patterns & motion) is unblocked (its `depends_on: [design-system-r7k2m]` is now satisfied) but sits in `backlog/`, not `todo/` — it needs a `modeling` promote before `work` will pick it up. Two gating decisions on the foundation were recommended-defaults applied while user away and are flagged in the task Notes for re-confirm.

---

## 2026-07-02 19:05 -- Task verified and completed: design-system-r7k2m - Velvet Lobby re-skin — tokens & type foundation

**Type:** Work / Task completion
**Task:** design-system-r7k2m - Velvet Lobby re-skin — tokens & type foundation
**Summary:** Shipped the Velvet Lobby token + type foundation — replaced the `dim` theme's palette in place with the burgundy-black/gold oklch tokens, swapped Oswald/Inter for Instrument Serif/Instrument Sans/Spline Sans Mono (self-hosted `@fontsource`), retargeted `DesignSystem.fs`'s type helpers and the StyleGuide page, and re-tinted `.glass-card`/`.rating-dropdown` while keeping ADR-0006's mandatory overlay-glass rule fully in force. `styleguide.md` reconciled as the gate source of truth.
**Duration:** ~19m (worker ~17m + verify ~2m)
**Verification:** PASS (iteration 1)
**Files changed:** 9
**Tests added:** 0 (presentational token/CSS/markdown/view layer — no assertable pure logic, no unit-test suite; verified via clean `npm run build`, Fable + Tailwind)
**ADRs written:** none

---

## 2026-07-02 18:45 -- Batch started: [design-system-r7k2m]

**Type:** Work / Batch start
**Tasks:** design-system-r7k2m - Velvet Lobby re-skin — tokens & type foundation
**Parallel:** no (1 worker)

---

## 2026-07-02 18:40 -- Modeling / Refined: design-system-r7k2m - Velvet Lobby re-skin — tokens & type foundation

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Resolved the two open decisions that were gating promotion (recommended defaults applied while user away — flagged for re-confirm). (1) **Glassmorphism → keep ADR-0006's mandatory glass, re-tint only** — the re-skin re-parameterizes the glass tint (`.glass-card`, `.rating-dropdown`) to burgundy/gold; no ADR amendment, no relaxation of the no-opaque-overlay rule (lowest blast radius — ADR-0006 is `scope: global` and enforced by `design-check`); the brief's solid surfaces are page/card backgrounds, not floating overlays, so no genuine conflict. (2) **Theme → replace `dim` in place** — overwrite the dim palette with Velvet Lobby oklch tokens, keep the name `dim` and `data-theme="dim"`; touches no page attribute; a named theme is deferred until light-mode or the cool "Modern recolor" variant is actually wanted. Rewrote the two Notes bullets from open→resolved, updated the palette + glass acceptance criteria, and **promoted to `todo/`**. Sibling [[design-system-h3q8n]] inherits both resolutions and stays in backlog (must not promote ahead of this foundation).
**Split into:** none
**ADRs written:** none

---

## 2026-07-02 18:10 -- Modeling / Refined: design-system-h3q8n - Velvet Lobby re-skin — component patterns & motion

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** backlog
**Summary:** Sharpened the component-patterns task's own shape (its two gating decisions live on the [[design-system-r7k2m]] foundation, not here). Three refinement calls, recommended defaults applied while the user was away: (1) **not split** — kept as one cohesive component pass; (2) **code home = typed compositions in `DesignSystem.fs`, not inline** — resolves the BC README's standing open question and follows the design-system-003 ActionMenu prior art; (3) **motion = primitives only** — design-system owns the keyframes/helpers/discipline (gold-leaf sweep reserved for In-focus, 400ms leave-transition, 200ms cross-fade), while *where* queue-leave and tab cross-fade fire is dashboard/tab BC wiring, carved out like the 3b game-detail chrome. Also surfaced and codified the **In-focus poster gold-frame (3c)** as an explicit reusable pattern (was only implied), and rewrote all acceptance criteria to concrete/testable. Stays in `backlog/` — must not promote ahead of r7k2m.
**Split into:** none
**ADRs written:** none

---

## 2026-07-02 17:39 -- Modeling / Captured: design-system-r7k2m + design-system-h3q8n - Velvet Lobby re-skin

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** backlog
**Summary:** Captured a wholesale re-skin of the styleguide (the frontend gate) to the cinematic **Velvet Lobby** direction from the "Mediatheca design brief" Claude Design project (`c19616ce-…`, read via `DesignSync`). User chose Velvet Lobby (warm burgundy + gold) with variant 3a as the reference, split into two tasks: **design-system-r7k2m** (tokens & type foundation — replaces Oswald/Inter with Instrument Serif + Instrument Sans + Spline Sans Mono, swaps the dim palette for the 3d system-board oklch tokens, updates index.css `@theme` + DesignSystem.fs + StyleGuide page + styleguide.md) and **design-system-h3q8n** (component patterns & motion — hero card, filmstrip row, lifecycle status badges, segmented/continuous progress, star rating, gold-leaf sweep; depends on the foundation). Both backlog pending two open decisions (glassmorphism coexistence vs ADR-0006; theme replace-in-place vs new name).

---

## 2026-06-26 12:35 -- Work session ended

**Type:** Work / Session end
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Commits:** 1 (integration-m4k7p; this session-end line committed separately)
**Note:** integration-m4k7p (materialize missing season/episode from Jellyfin) shipped — projection-column provenance (`source TEXT DEFAULT 'tmdb'`) on series_seasons/series_episodes, a fault-isolated materialization pass before the watch-history sync, an `EpisodeDto.MetadataPending` badge, and ADR 0012 (TMDB-authoritative, Jellyfin self-healing supplement). Suite 278 green (7 new). Created backlog item integration-007 (fetch Jellyfin stills on materialize) — stills were deferred from v1 per the task's scope note. Concept candidate surfaced: **jellyfin-as-self-healing-metadata-supplement** across integration-005/006/m4k7p + ADR 0012.

---

## 2026-06-26 12:30 -- Task verified and completed: integration-m4k7p - Materialize a missing season/episode from Jellyfin when TMDB lacks it

**Type:** Work / Task completion
**Task:** integration-m4k7p - Materialize a missing season/episode from Jellyfin when TMDB lacks it
**Summary:** The Jellyfin sync now materializes season/episode metadata for anything TMDB lacks (tagged `source='jellyfin'`, shown with a subtle "metadata pending" badge), keeping TMDB authoritative so its later `INSERT OR REPLACE` refresh enriches the rows in place and clears the badge for free.
**Verification:** PASS (iteration 1) — full suite 278 passed / 0 failed (was 271); 7 new tests across `JellyfinMaterializeTests.fs` cover criteria a–e plus two fault-isolation cases; client Fable build green.
**Files changed:** 12
**Tests added:** 7
**ADRs written:** 0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md (scope: integration)

---

## 2026-06-26 12:00 -- Batch started: [integration-m4k7p]

**Type:** Work / Batch start
**Tasks:** integration-m4k7p - Materialize a missing season/episode from Jellyfin when TMDB lacks it
**Parallel:** no (1 worker)

---

## 2026-06-26 11:30 -- Modeling / Refined: integration-m4k7p - Materialize a missing season/episode from Jellyfin when TMDB lacks it

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** Resolved the three open refinement points against the real code (via orchestrator). **Provenance = a `source TEXT DEFAULT 'tmdb'` column on `series_episodes`/`series_seasons`, NOT a new event** — episode metadata is already a projection-only cache (`Series.evolve` ignores episode detail; `SeriesRefresh.refreshOne` writes via `applyToProjection`), so materialization mirrors that and tags `source='jellyfin'`. **Dedup/enrichment key = the existing PK `(slug, season, episode)`**; the "pending" flag clears for free because TMDB's `INSERT OR REPLACE` omits `source` and SQLite resets it to the default on enrichment — no second code path, no duplicate. User chose a **subtle "metadata pending" badge**, making this frontend-bearing → added `depends_on: [design-system-001]` (gate satisfied; styleguide is done) and traced the badge path column→read-model→`EpisodeDto.MetadataPending`→`SeriesDetail` render. Present-vs-played widening is free (Api already fetches the full episode batch). Key gotcha pinned: a materialized episode needs a synthetic `series_seasons` row or it orphans. Rewrote all 6 acceptance criteria to concrete/testable. **No split** (single vertical slice, size M). Promoted to `todo/`.
**Split into:** none
**ADRs written:** none (a short `scope: integration` ADR on TMDB-authoritative precedence + the enrichment mechanism is flagged to be written during work; `work` will backlink it)

---

## 2026-06-26 11:00 -- Modeling / Captured: integration-m4k7p - Materialize a missing season/episode from Jellyfin when TMDB lacks it

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** backlog
**Summary:** Diagnosed why Interview with the Vampire S3 (three episodes present on Jellyfin) never appeared: the app sources episode metadata only from TMDB, which still lists two seasons; the Jellyfin sync only records watch history for existing episodes and never materializes new season/episode metadata. integration-006 fixed only candidate selection; the real fix (Jellyfin-as-source) was decided in the integration-005 spike but never captured. Captured that deferred implementation task.

---

## 2026-06-26 10:00 -- Work session ended

**Type:** Work / Session end
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Commits:** 3 (integration-005 closeout file move, integration-006 fix, this session-end line)
**Note:** integration-005 (fallback-source spike) closed out — research deliverable verified against all 4 acceptance criteria, no code by design. integration-006 (nightly refresh skips Ended series) fixed — activity-gated candidate filter, 5 new tests, suite 271 green. A concurrent `modeling` session committed (fdb14cb, integration-006 refine→todo) mid-batch and swept up the 005 INDEX/protocol edits that were staged at the time; the 005 task-file move was then landed separately (c1190ed) to restore board↔INDEX consistency. integration-006 was promoted to todo by that same concurrent session and picked up automatically in the next scan, per the loop's design.

---

## 2026-06-26 09:55 -- Task verified and completed: integration-006 - Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up

**Type:** Work / Task completion
**Task:** integration-006 - Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up
**Summary:** `getRefreshCandidates` is now activity-gated — an `Ended` series re-enters the nightly TMDB-refresh candidate set when it is `in_focus` or was watched within the last 180 days (single `WHERE`-clause change, no schema migration), so a TMDB-added season on an engaged-with show is auto-discovered while a cold finished library stays excluded. The `Async.Sleep 500` throttle is untouched.
**Verification:** PASS (iteration 1) — full suite 271 passed / 0 failed (was 266); 5 new `SeriesRefresh.getRefreshCandidates` tests (3 positive, 2 negative) execute and pass. Candidate path confirmed wired to the scheduled nightly job (Program.fs).
**Files changed:** 3
**Tests added:** 5 (new SeriesRefreshTests.fs)
**ADRs written:** none (cadence heuristic kept inline per refinement decision)

---

## 2026-06-26 09:40 -- Batch started: [integration-006]

**Type:** Work / Batch start
**Tasks:** integration-006 - Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up
**Parallel:** no (1 worker)

---

## 2026-06-26 09:30 -- Modeling / Refined: integration-006 - Nightly series refresh skips Ended series, so a TMDB-added season is never auto-picked-up

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** Pinned the one open design point (re-check cadence for `Ended` series) and promoted to `todo/`. Decided **activity-gated**: an `Ended` series re-enters the nightly candidate set only when it carries a recency signal — `in_focus = 1` or watched within 180 days — reusing existing `in_focus` + the `MAX(series_episode_progress.watched_date)` subquery (no schema migration), so the candidate set stays bounded and the TMDB rate budget is preserved. Chosen over a staleness window (needs a new `last_refreshed` projection column) and a separate slower schedule (second `ScheduledJobs` entry, still re-fetches the whole finished library). Implementation is a single `WHERE`-clause change in `getRefreshCandidates` (`SeriesRefresh.fs:316`); acceptance criteria now cover positive + negative candidate paths and throttle preservation. Decision kept inline (no ADR — reversible single-filter heuristic in a generic BC). Independent of integration-005 but composes with it.
**Split into:** none
**ADRs written:** none

---

## 2026-06-26 09:05 -- Task verified and completed: integration-005 - Spike — fallback metadata source when TMDB lags on new seasons

**Type:** Work / Task completion
**Task:** integration-005 - Spike — fallback metadata source when TMDB lags on new seasons
**Summary:** Verified the spike's deliverable — the research report satisfies all four acceptance criteria (TheTVDB/Trakt/Jellyfin comparison with OMDb dismissed, explicit recommendation of Jellyfin-as-source supplementing TMDB, direct field mapping onto RefreshFetchResult/SeasonImportData/EpisodeImportData, and a follow-up sizing note). Cited code mechanisms confirmed real against SeriesRefresh.fs/Jellyfin.fs. No code by design; spike closed out.
**Verification:** PASS (iteration 1)
**Files changed:** 0 (research-only spike; task-file move only)
**Tests added:** 0
**ADRs written:** none

---

## 2026-06-26 09:00 -- Batch started: [integration-005]

**Type:** Work / Batch start
**Tasks:** integration-005 - Spike — fallback metadata source when TMDB lags on new seasons
**Parallel:** no (1 worker)

---

## 2026-06-26 00:16 -- Modeling / Refined: integration-005 - Spike — fallback metadata source when TMDB lags on new seasons

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** Ran the spike's research during refinement (user chose "run it now", even comparison across TheTVDB / Trakt / Jellyfin, OMDb demoted to a one-line dismissal). All four acceptance criteria are satisfied by the resulting report, so the spike is resolved and promoted to `todo/`. **Recommendation: Jellyfin-as-source supplementing (not replacing) TMDB** — materialize a missing season/episode from the already-integrated Jellyfin adapter when the sync reports a row the TMDB projection lacks; no new external dependency, no numbering remap. Trakt disqualified (sources from TMDB, inherits its lag); TheTVDB is the better raw source but costs a net-new adapter + subscription/PIN auth. Folded report slug into `related_research`; added a Resolution section and a provider-config caveat for the follow-up implementation task.
**Split into:** none
**ADRs written:** none

---

## 2026-06-26 00:16 -- Research: TV-series metadata fallback sources (TheTVDB vs Trakt vs Jellyfin)

**Type:** Research
**Requested by:** model
**Report:** knowledge/research/tv-series-metadata-fallback-sources-2026-06-26.md
**Review:** PASS (iteration 2) — iteration 1 FAILed on a contradicted claim ("Jellyfin's default TV scraper is TheTVDB"; corrected to "TheTVDB is an optional plugin, TMDB is Jellyfin's default", provider-provenance flagged ⚠️ UNVERIFIED), re-verified clean on iteration 2.
**Summary:**
- Jellyfin-as-source recommended as a TMDB *supplement* — targets the exact failure mode (the missing season is always one the user is watching, hence already in Jellyfin), reuses the existing adapter with no new key/dependency, and needs no numbering remap.
- Trakt disqualified: sources most TV info from TMDB (~24h refresh), so it inherits TMDB's lag.
- TheTVDB is the strongest raw TV source but costs a net-new adapter + subscription/PIN auth + attribution + Aired-Order pinning; revisit only for titles not in Jellyfin.

---

## 2026-06-25 14:00 -- Modeling / Captured: integration-005 + integration-006 — TMDB season-gap mitigation

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** backlog (both)
**Summary:** *Interview with the Vampire* S3 is airing (visible on IMDb / the user's Jellyfin) but TMDB still lists only 2 seasons, and the app is fully TMDB-bound for series metadata. Captured two siblings: **integration-005** (spike — compare TheTVDB / Trakt / OMDb / Jellyfin-as-source as a fallback when TMDB lags, recommend a direction; user chose "research first") and **integration-006** (bug — the nightly refresh's `Returning/InProduction` candidate filter excludes `Ended` series, so a TMDB-added season is never auto-discovered, only via manual refresh). Independent of each other; same "TMDB didn't deliver" origin.

---

