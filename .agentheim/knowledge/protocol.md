# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-08-07 15:55 -- Task verified and completed: integration-hebjs - One-click Steam Family import — automatic access-token acquisition

**Type:** Work / Task completion
**Task:** integration-hebjs - One-click Steam Family import — automatic access-token acquisition
**Summary:** One-click Steam Family import — Connect Steam QR login persists a refresh token, family access tokens self-mint and self-heal via withTokenRefresh, reconnect prompt on revocation, manual paste demoted to fallback
**Duration:** 26m
**Verification:** PASS (iteration 1)
**Files changed:** 12
**Tests added:** 4
**ADRs written:** 0061

---

## 2026-08-07 15:25 -- Batch started: [integration-hebjs]

**Type:** Work / Batch start
**Tasks:** integration-hebjs - One-click Steam Family import — automatic access-token acquisition
**Parallel:** no (1 worker — single ready task; ready set was exactly 1)

---

## 2026-08-07 14:08 -- Modeling / Promoted: integration-hebjs - One-click Steam Family import — automatic access-token acquisition

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-08-07 14:08 -- Modeling / Refined: integration-hebjs - One-click Steam Family import — automatic access-token acquisition

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** The ADR-0019 builder gate ran live and **passed**: the builder QR-scanned into the spike harness (SteamKit2 3.1.0, MobileApp platform, persistent session), `GenerateAccessTokenForApp` minted an access token over plain HTTP (no CM connection), and `GetFamilyGroupForUser` returned HTTP 200 with real family data — the minted token carries the required audience/scope, so the browser-retrieval fallback is not needed and the acceptance criteria stand as written. Gate outcome + implementation intel (SteamKit2 API deltas, required `steamid` param from the JWT `sub` claim, ~30 s QR rotation, render-as-image requirement) recorded in the task; BC README's open question resolved; spike harness fixes committed under the task trailer. Auto-promoted per the readiness gate.
**Split into:** none
**ADRs written:** none (ADR-0019 anticipated both branches; the PASS branch is now recorded in the task and BC README)

---

## 2026-08-07 12:27 -- Work session ended

**Type:** Work / Session end
**Duration:** 26m (batch start 12:01 → 12:27)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-ev65k: 1
**Commits:** 3 (batch start, task squash-merge commit, this entry)
**Session-start churn:** 0 recognized machine-shape commits, 0 human commits since the 2026-08-07 11:43 boundary — both commits in the window (`cff5f55` capture of games-ev65k, `188fedd` prior session-end bookkeeping) carry `[games-ev65k]` / `[games-k3vps]` trailers. Nothing flagged; no advisory written.
**Vision-conformance:** none — batch aligns with vision (games-ev65k lands inside the named Steam Import Enhancement workstream and the Games tab's "expandable over time" clause, and is the cleanest expression yet of the Replayable principle: release date is cache-tier by construction — no new event, no event-payload change, `Year` byte-identical, ADR-0045's zero-grep property independently re-verified by the verifier, and the new ADR-0060 records the partial-precision sort and self-draining cursor as first-class decisions rather than incidental implementation)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified (type: feature; files under src/, tests/, plus the BC README, one ADR, and routine task documentation)
**Carry-over:** No `.agentheim/`-owned files stranded. left behind (user WIP, 3 files: `src/Server/Rawg.fs` modified, `Mediatheca Directions.html` and `plan.md` untracked). `git worktree list` clean — the games-ev65k worktree was unlinked (node_modules junction removed first, real copy verified intact) and removed after PASS integration, branch `aw/games-ev65k` deleted, and Windows released the directory fully this time. `.worktrees/` still retains the same four gitignored-by-intent-but-untracked directory shells from prior sessions (administration-z6ymt, games-j6wkr, games-k3vps, games-v4nqe) holding only MSBuild `obj/` residue (12 files, no source) — surfaced to the builder for a second session running, not removed on this session's initiative.

---

## 2026-08-07 12:26 -- Task verified and completed: games-ev65k - Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab

**Type:** Work / Task completion
**Task:** games-ev65k - Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab
**Summary:** Game release dates from Steam — cached raw/parsed/coming-soon metadata with a self-draining backfill, surfaced on the detail page, list cards, and a new Upcoming section
**Duration:** 23m
**Verification:** PASS (iteration 1)
**Files changed:** 20
**Tests added:** 29
**ADRs written:** 0060

---

## 2026-08-07 12:01 -- Batch started: [games-ev65k]

**Type:** Work / Batch start
**Tasks:** games-ev65k - Game release dates from Steam — cached for every Steam-linked game, auto-refreshed while unreleased, surfaced on the detail page and list cards, plus an Upcoming section on the Games tab
**Parallel:** no (1 worker — single ready task; ready set was exactly 1)

---

## 2026-08-07 11:52 -- Modeling / Captured: games-ev65k - Game release dates from Steam (cached, auto-refreshed, Upcoming view)

**Type:** Modeling / Capture
**BC:** games
**Filed to:** todo
**Summary:** Release date becomes cached third-party metadata (ADR-0043 tier — re-derivable, and it *changes* on delays) on every Steam-linked game: `SteamStoreDetails` decodes `release_date`/`coming_soon`, `game_metadata_cache` gains raw-string + parsed-date + coming-soon columns, and a b8xnw-shaped own-cursor backfill re-polls only unreleased games until the set drains. Surfaced on the detail page, as an unreleased-only list-card hint, and in a new Upcoming section on the Games tab (builder decisions: all three surfaces, all games, auto-refresh). Filed directly to todo — machinery fully precedented (games-b8xnw/a7dqx), concrete ACs including a Tenebris Somnia (appId 2121510, Oct 2026) end-to-end criterion; depends_on games-k3vps (completed mid-capture) and design-system-001 (done), both met.

---

## 2026-08-07 11:43 -- Work session ended

**Type:** Work / Session end
**Duration:** 26m (batch start 11:17 → 11:43)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-k3vps: 1
**Commits:** 3 (batch start, task squash-merge commit, this entry)
**Session-start churn:** 0 recognized machine-shape commits, 1 human commit since the 2026-08-04 23:52 boundary — `ff1e040` "removed cardhover in settings" touches `src/Client/Pages/Settings/Views.fs`, the settings surface ADR-0041 describes, and drops `DesignSystem.cardHover` from the integration card and the admin section card. Flagged as a governed-surface hit; advisory written to `.agentheim/state/whats-next.md`, no task auto-filed.
**Vision-conformance:** none — batch aligns with vision (games-k3vps extends the Steam Import Enhancement workstream inside the v1 media experience, and honors the Replayable principle: the Steam-sourced import writes description/short-description/website-url/facets to `game_metadata_cache` via the creation code path and carries only identity-card fields on `Add_game` — verifier confirmed no `*Projection.fs` reference to `MetadataCache`, ADR-0043/ADR-0045 discipline intact)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified (type: feature; all files under src/, tests/, plus the BC README and routine task documentation)
**Carry-over:** left behind (user WIP, 3 files: `src/Server/Rawg.fs` modified, `Mediatheca Directions.html` and `plan.md` untracked). No `.agentheim/`-owned files stranded. `git worktree list` clean — the games-k3vps worktree was unlinked (node_modules junction removed first) and removed after PASS integration, branch deleted; `.worktrees/` retains four gitignored directory shells (administration-z6ymt, games-j6wkr, games-k3vps, games-v4nqe) holding only MSBuild `obj/` residue (12 files, no source) that Windows left behind across teardowns — surfaced to the builder, not removed on this session's initiative.

---

## 2026-08-07 11:42 -- Task verified and completed: games-k3vps - Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results

**Type:** Work / Task completion
**Task:** games-k3vps - Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results
**Summary:** Selectable RAWG/Steam search sources in the games search tab — source-toggle checkboxes, merged badge-tagged results, and two new endpoints (searchSteamGames, addGameFromSteam)
**Duration:** 22m
**Verification:** PASS (iteration 1)
**Files changed:** 8
**Tests added:** 6
**ADRs written:** none

---

## 2026-08-07 11:17 -- Batch started: [games-k3vps]

**Type:** Work / Batch start
**Tasks:** games-k3vps - Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results
**Parallel:** no (1 worker — single ready task)

---

## 2026-08-07 10:32 -- Modeling / Captured: games-k3vps - Selectable search sources in the games search tab (RAWG / Steam checkboxes)

**Type:** Modeling / Capture
**BC:** games
**Filed to:** todo
**Summary:** The search modal's Games tab gets a source-toggle row below the tab bar — RAWG checked by default on every open, Steam unchecked, never persisted; toggling immediately includes/excludes each API's results. Filed directly to todo: all server machinery exists (Steam.searchSteamByName for query search, getSteamStoreDetails → Add_game for Steam-sourced import), so the task is a client feature plus two thin endpoints, with concrete ACs and the frontend gate met via design-system-001 (done).

---

## 2026-08-04 23:51 -- Task verified and completed: games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge

**Type:** Work / Task completion
**Task:** games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge
**Summary:** Steam Deck compatibility (Verified/Playable/Unsupported/Unknown) shipped as a cache-only facet with its own resumable throttled backfill and a badge beside the play-facet badges — the named ajaxgetdeckappcompatibilityreport endpoint proved retired, replaced by scraping the store page data-hardwarecompatibility attribute (ADR-0059); iteration 2 restored ADR-0045 by-construction invariant via a private GameProjection reader
**Duration:** 40m
**Verification:** PASS (iteration 2)
**Files changed:** 16
**Tests added:** 20
**ADRs written:** 0059

---

## 2026-08-04 23:18 -- Batch started: [games-b8xnw]

**Type:** Work / Batch start
**Tasks:** games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge
**Parallel:** no (1 worker — single ready task)

---

## 2026-08-04 23:17 -- Modeling / Promoted: games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-04 23:58 -- Modeling / Refined: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups

**Type:** Modeling / Refine
**BC:** infrastructure
**Status after:** backlog
**Summary:** Backlog refinement pass — goal, scope, and acceptance criteria confirmed current with no changes to the What. Recorded the ADR-0056 execution split (Part C code deletion is worker-executable; Parts A/B production verification and backup deletion plus deploy stay builder-executed runbook steps) and the promotion trigger (builder confirms Part A clean on/after 2026-08-17, then promotes). This completes the refine-all-backlog pass: every backlog task (integration-hebjs, movies-v2gkh, infrastructure-r8kqt) now carries an explicit refinement record; each stays in backlog on a named gate, not for lack of refinement.

## 2026-08-04 23:52 -- Work session ended

**Type:** Work / Session end
**Duration:** 28m (batch start 23:24 → 23:52)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-b8xnw: 2
**Commits:** 3 (batch start, task squash-merge commit, this entry)
**Vision-conformance:** none — batch aligns with vision (games-b8xnw enriches the Games tab within the v1 media experience and explicitly honors the Replayable principle: Deck compatibility is cached third-party metadata, never evented, with the ADR-0045 by-construction invariant restored at iteration 2)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified (type: feature, all files under src/ and tests/ plus routine task documentation)
**Carry-over:** left behind (user WIP, 3 files). No `.agentheim/`-owned files stranded; `.worktrees/` fully swept (worktree removed clean after PASS integration, branch deleted).

---

## 2026-08-04 23:45 -- Verification failed: games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge

**Type:** Work / Verification failure
**Task:** games-b8xnw - Steam Deck compatibility readiness (Verified/Playable/Unsupported) as a cached facet with a badge
**Iteration:** 1 of 3
**Reasons:** GameProjection.fs calls MetadataCache.readDeckCompat at three sites — the first code reference to MetadataCache from any *Projection.fs, breaking ADR-0045's by-construction zero-grep property (prior cutovers kept such readers as private GameProjection helpers); secondary: backfill throttle for the new store-page source inherited unmeasured from GameFacetBackfill with no recorded rate-limit observation
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-08-04 23:18 -- Modeling / Refined: integration-hebjs - One-click Steam Family import — automatic access-token acquisition

**Type:** Modeling / Refine
**BC:** integration
**Status after:** backlog
**Summary:** Rewrote the provisional ACs around the landed ygwsa spike: added the explicit ADR-0019 builder gate (Marco runs the two spike fsx scripts, ~30 min, QR + mobile app — worker cannot) as a hard pre-promotion step; settled both open questions (manual paste stays as fallback footnote; scheduled family sync out of scope, future capture). Stays in backlog until the gate outcome is recorded.

---

## 2026-08-04 23:18 -- Modeling / Refined: movies-v2gkh - Move Movie TMDB metadata into the cache

**Type:** Modeling / Refine
**BC:** movies
**Status after:** backlog
**Summary:** Wrote the acceptance criteria following the shipped series-q8jwc/games-a7dqx cutover shape (typed cache columns, identity-card COALESCE, four-part tolerance rule, drift zero). Trigger condition re-confirmed: stays parked until a movie-refresh feature is captured, then becomes its depends_on prerequisite.

---

## 2026-08-04 20:46 -- Work session ended

**Type:** Work / Session end
**Duration:** 32m (batch start 20:14 → 20:46)
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-j6wkr: 1, administration-z6ymt: 1
**Commits:** 4 (batch start, 2 task squash-merge commits, this entry)
**Vision-conformance:** none — batch aligns with vision (games-j6wkr closes the games-tab play-mode UI gap in the v1 media experience; administration-z6ymt executes the recognized Operability workstream within its stated boundary — operator tooling, builder-executed live purge per ADR-0056, serving the Replayable principle)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (2 tasks) — hand-classified (lib/vacuum-guard.mjs not present in this plugin install); both tasks shipped app code/tooling under src/, with ADR/README writes as routine task documentation
**Carry-over:** left behind (user WIP, 3 files). No `.agentheim/`-owned files stranded; `.worktrees/` fully swept (both worktrees removed clean after PASS integration, node_modules junctions unlinked first).

---

## 2026-08-04 20:44 -- Task verified and completed: administration-z6ymt - Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change

**Type:** Work / Task completion
**Task:** administration-z6ymt - Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change
**Summary:** Shipped the offline NDJSON purge filter as a Server CLI subcommand (EventLogFilter.fs, deny-listing the 11 demoted Game metadata event types, byte-stable pass-through) with fixture-backed tests, retired the completed games-h4mrd migration machinery, and committed the operator runbook — the live purge stays builder-executed per ADR-0056
**Duration:** 28m
**Verification:** PASS (iteration 1)
**Files changed:** 14
**Tests added:** 8
**ADRs written:** 0058

---

## 2026-08-04 20:33 -- Task verified and completed: games-j6wkr - Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened)

**Type:** Work / Task completion
**Task:** games-j6wkr - Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened)
**Summary:** Rewrote the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges on cards and detail hero, pure client-side facet filter pills, and seven Auto/On/Off segmented controls that POST a single-field-changed PlayFacetsOverride (ADR-0053 trap guarded by 8 Expecto tests)
**Duration:** 23m
**Verification:** PASS (iteration 1)
**Files changed:** 13
**Tests added:** 8
**ADRs written:** 0057

---

## 2026-08-04 20:11 -- Batch started: [games-j6wkr, administration-z6ymt]

**Type:** Work / Batch start
**Tasks:** games-j6wkr - Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened), administration-z6ymt - Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change
**Parallel:** yes (2 workers — no file overlap: games-j6wkr is client-only Games UI, administration-z6ymt is server-side purge tooling + runbook)

---

## 2026-08-04 20:09 -- Modeling / Promoted: administration-z6ymt - Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change

**Type:** Modeling / Promote
**BC:** administration
**From → To:** backlog → todo

---

## 2026-08-04 19:05 -- Modeling / Refined: administration-z6ymt - Purge the 11 demoted metadata event types via the ADR-0038 wipe-first import + retire the games-h4mrd migration machinery

**Type:** Modeling / Refine
**BC:** administration
**Status after:** todo (auto-promoted after readiness check)
**Summary:** Post-v4nqe/r2xhv reconciliation via the orchestrator (tactical-modeler + architect). Three stale premises corrected: the purge set is 11 enumerated Game types (not "rawg/hltb setters"); the "~1000 duplicate identity events" premise was disproven by games-w4tzc (all identity-card types excluded, never dropped); Series_refreshed is still fully live — its ~566 no-change rows deferred at the builder's direction (payload-level filter, ~5% of the reduction, false-positive risk). Builder decisions: deferral LIFTED (production cutover completed 2026-08-03); Game_play_time_set included with the completed games-h4mrd migration machinery retired in the same change; GameAddedData payload scrub dropped entirely (Required-field decoder makes it stream-corrupting). Execution shape per new ADR-0056: worker ships offline byte-preserving NDJSON filter + fixtures + runbook; builder executes the live purge by hand through the Settings UI.
**Split into:** none
**ADRs written:** 0056

---

## 2026-08-04 18:08 -- Modeling / Promoted: games-j6wkr - Rewrite the Games UI for typed play facets — Solo/Co-op/Versus/Couch badges, per-facet Auto/On/Off override controls, and client-side list filters over the landed PlayFacets contract (split 3 of 3, closes the no-play-mode-UI window games-v4nqe opened)

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-04 18:05 -- Modeling / Refined: games-j6wkr - Rewrite the Games UI for typed play facets (split 3 of 3)

**Type:** Modeling / Refine
**BC:** games
**Status after:** todo (auto-promoted after readiness check)
**Summary:** Post-v4nqe reconciliation — verified every assumption against the landed code. The contract landed as specified (PlayFacets/PlayFacetsOverride on both DTOs, overrideGamePlayFacets, VrSupport 3-case DU; picker deleted, only tombstone comments remain). Fixed the one stale spec: the SQL COALESCE merge-rule (and its ADR-0048 comment instruction) never shipped — the merge is pure F# (FacetDerivation.merge) server-side, so DTO facets arrive pre-merged and list filters are pure client-side (existing StatusFilter pattern). Added a machine-checkable criterion for the ADR-0053 one-field-override trap, linked ADR-0054 and prior-art games-v4nqe, noted the StyleGuide segmented-control precedent. Both dependencies (games-v4nqe, design-system-001) confirmed done.
**Split into:** none
**ADRs written:** none

---

## 2026-08-04 17:25 -- Task verified and completed: games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)

**Type:** Work / Task completion
**Task:** games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)
**Summary:** Converted every Game metadata emission site (18 call sites, 5 flows) to game_metadata_cache writes, deleted the eight demoted commands via the four-part rule, dropped the unread projection columns, wired PlayFacets/PlayFacetsOverride into the public DTOs, and deleted the uncompilable client play-mode picker — with genres kept event-carried per ADR-0055 (amending ADR-0043) after the verifier caught the doctrinal conflict
**Duration:** 1h29m
**Verification:** PASS (iteration 3)
**Files changed:** 15
**Tests added:** 18
**ADRs written:** 0055

---

## 2026-08-04 17:27 -- Work session ended

**Type:** Work / Session end
**Duration:** 1h22m (batch start 16:05 → 17:27)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-v4nqe: 3
**Commits:** 3 (batch start, task squash-merge commit, this entry)
**Vision-conformance:** none — batch aligns with vision (games-v4nqe executes the "Replayable" design principle — ADR-0043's doctrine, with ADR-0055 keeping genres replay-deterministic when the task's own spec would have broken it; touches no out-of-scope item)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task)
**Carry-over:** left behind (user WIP, 3 files). No `.agentheim/`-owned files stranded; `.worktrees/` fully swept (games-v4nqe worktree removed clean after PASS integration, node_modules junction unlinked first).

---

## 2026-08-04 17:20 -- Verification failed: games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3)

**Type:** Work / Verification failure
**Task:** games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3)
**Iteration:** 2 of 3
**Reasons:** Two stale source comments contradict the worker's own ADR-0055 — `Games.fs:251`'s `Game_categorized` evolve arm still says "genres now cache-derived" (ADR-0055 explicitly names this comment's correction), and `MetadataCache.fs:475-477` still promises a creation-path cache writer for `genres` that ADR-0055 decided will never exist. Everything substantive clean: 630/630 tests, build green, behavior consistent, departure honestly recorded, ADR-0055 well-formed.
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-08-04 16:55 -- Verification failed: games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3)

**Type:** Work / Verification failure
**Task:** games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3)
**Iteration:** 1 of 3
**Reasons:** Diff contradicts ADR-0043's classification of Game `genres` (event-carried identity-card projection column) — columns dropped and re-sourced from `game_metadata_cache.genres`, which no replay or refresh path writes (only creation paths do), with no amending/superseding ADR recorded; ADR-0048/0051 precedent runs the other way; BC README's new "Identity card" ubiquitous-language entry inverts ADR-0043's meaning of the term. Code/tests/build/scope all clean (632/632 green, Fable build green, demoted-command grep zero).
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-08-04 16:05 -- Batch started: [games-v4nqe]

**Type:** Work / Batch start
**Tasks:** games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)
**Parallel:** no (1 worker — only one ready task on the board)

---

## 2026-08-04 16:03 -- Modeling / Promoted: games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3 — stops the 7668-event play-mode bloat games-a7dqx's schema made possible)

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-04 16:20 -- Modeling / Refined: games-v4nqe - Convert every Game metadata emission site to cache writes, delete the demoted commands, drop the projection columns, and prove drift zero (split 2 of 3)

**Type:** Modeling / Refine
**BC:** games
**Status after:** todo (auto-promoted after readiness check)
**Summary:** Post-a7dqx reconciliation — verified the task's assumptions against the landed foundation. Fixed three stale references: `upsertGameMetadata` doesn't exist (the identity-card cache writer is authored in this task, following `upsertGameFacets`'s ON-CONFLICT slice discipline — new acceptance criterion pins that a facet row survives an identity-card write); the cache's `genres` column already shipped unpopulated in a7dqx (seed only, no ADD COLUMN); `findGamesWithEmptyDescriptionAndSteamAppId` moved to `GameProjection.fs:819`. Linked ADR-0054 bidirectionally.
**Split into:** none
**ADRs written:** none

---

## 2026-08-04 15:50 -- Work session ended

**Type:** Work / Session end
**Duration:** 53m (batch start 14:57 → 15:50)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-a7dqx: 2
**Commits:** 3 (batch start, task squash-merge commit, this entry)
**Vision-conformance:** none — batch aligns with vision (games-a7dqx executes the ADR-0043 replayability doctrine — the vision's "Replayable" design principle — and touches no out-of-scope item)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task)
**Carry-over:** left behind (user WIP, 3 files). No `.agentheim/`-owned files stranded; `.worktrees/` fully swept (games-a7dqx worktree removed clean after PASS integration, node_modules junction unlinked first).

---

## 2026-08-04 15:48 -- Task verified and completed: games-a7dqx - Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI)

**Type:** Work / Task completion
**Task:** games-a7dqx - Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI)
**Summary:** Built the strictly-additive play-facets cache/domain foundation (ADR-0053): the Game_play_facets_overridden/Override_play_facets event/command pair, a live-verified FacetDerivation.deriveFacets/merge module, the game_metadata_cache/game_detail schema extensions, safe cache-sourced reads for already-seeded description/HLTB/steam-last-played fields, and a resumable throttled Steam facet backfill job — old play-mode system, Shared.fs DTOs, and client untouched
**Duration:** 59m
**Verification:** PASS (iteration 2)
**Files changed:** 20
**Tests added:** 47
**ADRs written:** 0054

---

## 2026-08-04 15:43 -- Verification failed: games-a7dqx - Build the play-facets cache/domain foundation

**Type:** Work / Verification failure
**Task:** games-a7dqx - Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3)
**Iteration:** 1 of 3
**Reasons:** ADR-0054 file has no YAML frontmatter (id/title/scope/status/date/related_tasks all absent) and no `# ADR 0054:` heading, violating the house ADR template; minor — `## Rejected alternatives` should be `## Alternatives considered`, `## Consequences` un-subsectioned. Code/tests/scope all passed (615/615 green, build green).
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-08-04 14:57 -- Batch started: [games-a7dqx]

**Type:** Work / Batch start
**Tasks:** games-a7dqx - Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI)
**Parallel:** no (1 worker — only one ready task on the board)

---

## 2026-08-04 14:43 -- Modeling / Promoted: games-a7dqx - Build the play-facets cache/domain foundation — schema, ADR-0053 override event/command, Steam facet derivation, safe cache-sourced reads for already-seeded fields, and the resumable backfill job (split 1 of 3; games-v4nqe converts emission sites, games-j6wkr rewrites the UI)

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-04 13:55 -- Modeling / Refined: games-a7dqx - split into three sequenced tasks after bounce

**Type:** Modeling / Refine
**BC:** games
**Status after:** backlog (all three; promotion follows readiness check)
**Summary:** Marco approved splitting the bounced cutover along the worker's recommended seam. Orchestrator resolved two compile-coupling holes in the naive split: the Shared.fs DTO rename (PlayModes → PlayFacets) and the forced old-picker deletion both belong to task 2 (command deletion breaks them at compile time), not task 1 or 3. games-a7dqx re-scoped to the strictly-additive foundation (schema, ADR-0053 override event/command, deriveFacets, safe cache reads for already-seeded fields, backfill job); games-v4nqe carries emission-site conversion + command deletion + column drops + DTO finalization; games-j6wkr carries the new UI (badges, Auto/On/Off controls, filters). Three transitional gaps named and bounded in the task files. administration-z6ymt repointed to depend on games-v4nqe (purge only safe after the four-part-rule pass); games-b8xnw unchanged. All original acceptance criteria partitioned, none lost.
**Split into:** games-v4nqe, games-j6wkr (games-a7dqx re-scoped in place, id retained)
**ADRs written:** none (ADR-0053 unchanged, still governs)

---

## 2026-08-04 13:26 -- Work session ended

**Type:** Work / Session end
**Duration:** 8m (batch start 13:17 → 13:25)
**Completed:** 0 (first-try PASS: 0, re-dispatched: 0, skipped: 0)
**Bounced:** 1 (games-a7dqx — five independently-testable layers that must land together; worker note recommends a Series-precedent split, needs Marco's sign-off since he explicitly asked to keep it one task)
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-a7dqx: 1
**Commits:** 3 (batch start, bounce integration, this entry)
**Vision-conformance:** none — no tasks completed this session, nothing to assess
**Batch mix:** none — no tasks completed this session
**Carry-over:** left behind (user WIP, 3 files). No `.agentheim/`-owned files stranded; `.worktrees/` fully swept (games-a7dqx worktree removed clean after bounce integration, no husks).

---

## 2026-08-04 13:25 -- Task bounced: games-a7dqx - Move Game attribute metadata into the cache and stop emitting it

**Type:** Work / Task bounced
**Task:** games-a7dqx - Move Game attribute metadata into the cache and stop emitting it
**Reason:** Full-file survey of every in-scope module (Games.fs 854 lines, GameProjection.fs 1018 lines with ~10 readers of columns slated for drop, MetadataCache.fs, Steam.fs with 4 fetch sites, Api.fs 4410 lines with 18 call sites across 5 distinct emission flows, plus the full client UI rewrite) confirms at minimum five independently-testable layers that must all land together to compile/boot — unmanageable for one worker pass per the task's own hazard note 2. Worker note recommends a Series-precedent-style split (needs Marco's sign-off — he explicitly asked to keep this one task).
**Moved to:** backlog

---

## 2026-08-04 13:17 -- Batch started: [games-a7dqx]

**Type:** Work / Batch start
**Tasks:** games-a7dqx - Move Game attribute metadata into the cache and stop emitting it — 7668 Game_play_mode_added events are literally Steam Store category tags and make up 43% of the entire event log
**Parallel:** no (1 worker — only one ready task on the board)

---

## 2026-08-04 13:11 -- Modeling / Promoted: games-a7dqx - Move Game attribute metadata into the cache and stop emitting it — 7668 Game_play_mode_added events are literally Steam Store category tags and make up 43% of the entire event log

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-04 13:10 -- Modeling / Refined: games-a7dqx - Move Game attribute metadata into the cache and stop emitting it

**Type:** Modeling / Refine
**BC:** games
**Status after:** todo
**Summary:** Resolved both open questions with code evidence (`Categorize_game` is dead code with no genre-editing UI → genres move to cache; Steam Deck readiness split out as games-b8xnw). Orchestrator/tactical-modeler pass produced the full event disposition table (7 demoted event groups under the four-part tolerance rule; `Game_steam_last_played_set` derived from `game_play_session` rather than cached), the `Game_play_facets_overridden` override model, complete acceptance criteria, and five recorded hazards (identity-card write conflict, Api.fs emission-site sweep, decoder reshape, two dead endpoints). Marco decided the override UX: per-facet Auto/On/Off segmented controls (VR four-way). Dependencies verified done (administration-c3nvp, games-w4tzc, design-system-001 added per frontend gate).
**Split into:** games-b8xnw (follow-up, not a split — captured from decision 6)
**ADRs written:** 0053

---

## 2026-08-04 13:10 -- Modeling / Captured: games-b8xnw - Steam Deck compatibility readiness

**Type:** Modeling / Capture
**BC:** games
**Filed to:** backlog
**Summary:** Steam Deck compatibility (Verified/Playable/Unsupported) as a `deck_compat` column on `game_metadata_cache` with a card/detail badge — fetched from the unofficial `ajaxgetdeckappcompatibilityreport` endpoint, reusing games-a7dqx's resumable throttled-backfill infrastructure (hence `depends_on: games-a7dqx`). Scoped out of a7dqx at refinement per decision 6's "refiner should scope it".

---

## 2026-08-04 10:00 -- Work session ended

**Type:** Work / Session end
**Completed:** 0 — vacuum guard exit (no ready tasks; todo/ and doing/ empty across every BC; vision.md has no open questions to surface). Session-start churn reconciliation: 0 recognized machine-shape commits, 1 human commit since 2026-08-02 09:45 — `344d0f6` (ADR-0052 automated cutover; completed series-t3jkv and series-x9mfp out-of-band, deployed COMPLETE 2026-08-03 per builder) — advisory written to state/whats-next.md, no re-alignment task filed.

---

## 2026-08-04 -- Modeling / Captured: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups

**Type:** Modeling / Capture
**BC:** infrastructure
**Filed to:** backlog
**Summary:** Quick capture of the deferred cleanup that ADR-0052 always anticipated: after a two-week stability hold (earliest 2026-08-17), delete `StartupCutover.fs`, its test file, and its three `Composition.fs` call sites, revert `ensureSafeCatchUp` to `Projection.startAllProjections`, and remove the two pre-cutover backups from the server volume plus the pristine dev copy. Carries a full operator procedure (verify-window → remove backups → delete code → test/build/deploy) and the one-way-ordering caveat that deleting the harness makes any surviving pre-cutover backup unmigratable.

---

## 2026-08-02 09:45 -- Work session ended

**Type:** Work / Session end
**Duration:** ~2h (builder-authorized conflict-resolution arc, 2026-08-02 morning; continues the 2026-08-01 21:09 session)
**Completed:** 5 — administration-kv7dp and games-p6vkz (verified 2026-08-01, integrated after builder-authorized manual conflict resolution with full re-verification on the merged tree: 486/486 and 508/508 + build), journal-w3sbq (PASS iteration 1), games-h4mrd (PASS iteration 2), series-d5tpn (PASS iteration 3). **The 15-task deterministic-rebuild workstream's now-slice (12 tasks) is fully landed; todo/ and doing/ are empty across every BC.**
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0 (three verification FAILs — games-h4mrd iteration 1, series-d5tpn iterations 1-2 — all recovered via re-dispatch; none reached the builder)
**Dispatches:** journal-w3sbq: 1, games-h4mrd: 2, series-d5tpn: 3 (administration-kv7dp / games-p6vkz: 0 new — integration-only)
**Commits:** 7 this arc (2 conflict-resolution integrations, 1 batch start, 3 task integrations, this entry)
**Live-DB incident and repair (series-d5tpn iteration 1):** the worker ran its migration against the LIVE mediatheca.db out-of-band and in the wrong order, creating the cache tables empty before the rename — stranding 370 seasons/4624 episodes and zeroing dashboard series numbers. Conductor repaired at 09:10 (fresh VACUUM INTO backup; drop-empties + rename + view cycling in one transaction; verified 4624/370 under the new names, series_next_up=44, series_episode_counts=104). Backups retained: backups/mediatheca-pre-repair-20260802-091003.db and -091031.db (VACUUM INTO), plus the worker's raw-copy pre-task backup. The worker's 11 compensating events (global_position 17641-17651) bypassed the ADR-0032 composer's audit metadata — permanently untagged, recorded honestly in ADR-0051. Iterations 2-3 hardened MetadataCache.recoverStranded (view-safe, atomic, non-fatal) with fixtures the final verifier independently proved falsifying.
**ADR renumbering at integration:** kv7dp's provisional 0043 → **0049** (now superseded by 0051 per its own retirement criterion); p6vkz's provisional 0045 → **0050** (amended by games-h4mrd per its sanctioned fold); series-d5tpn wrote **0051**.
**Vision-conformance:** none — batch aligns with vision. The arc completes the deterministic-rebuild workstream (Replayable design principle, Operability & Observability). The live-DB incident is an execution defect, not vision drift, and is recorded above. Judged by hand.
**Batch mix:** 100% product-facing (5 tasks), classified by hand and on substance.
**Carry-over:** left behind (user WIP, 2 files — modified `src/Client/Pages/Settings/Views.fs`, untracked `Mediatheca Directions.html`, both pre-existing; protected through three merge aborts/repairs). No `.agentheim/`-owned files stranded. `.worktrees/` fully swept: the two conflict-stranded worktrees were integrated and removed; ~12 deregistered husk directories (Windows file-lock leftovers from this and PRIOR sessions' `git worktree remove` calls, including administration-k3vmt/n8kqw/svq3t/design-system-vk7rd) were deleted after `dotnet build-server shutdown` released the handles — the husk cause is dotnet build-server file locks surviving worktree removal.
**Board state after this session:** todo/ and doing/ empty in every BC. Backlog: integration-hebjs, administration-z6ymt (premise stale — re-check before promoting, see 2026-08-01 notes), games-a7dqx, movies-v2gkh, series-t3jkv (cache write path — newly-added series read empty third-party fields until it lands), series-x9mfp. Human-eye checks still pending: GameDetail prior-playtime line (now on main), series list/detail/Next Up render parity, and a first real run of the play-session migration preview + apply from Settings.

**Notes carried out of this arc:**

1. **The verification gate earned its keep three times:** h4mrd's missing dry-run preview (a silent ADR-0034 guardrail violation with a fabricated ADR claim), d5tpn's live-DB incident, and d5tpn's iteration-2 guard that was fatal against the exact state it claimed to repair (caught because the re-verify prompt demanded the fixture include the views). Each fix was verified falsifying before landing.
2. **Workers must never touch the live DATA_DIR database.** d5tpn iteration 1 did, out-of-band, causing the incident above. Migrations verify against fixtures; live operator actions are builder/conductor-only. Worker prompts for migration-shaped tasks should carry this constraint explicitly from the start (iterations 2-3 did).
3. **CWD drift bit twice more** (a self-merge no-op and a failed relative-path rollback) — both caught by the doctrine's own first-check. `git -C` everywhere remains the rule.

---

## 2026-08-02 09:39 -- Task verified and completed: series-d5tpn - Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard

**Type:** Work / Task completion
**Task:** series-d5tpn - Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard
**Summary:** Dropped the externally-sourced columns from series_list/series_detail (status and backdrop_ref retained per the identity-card clause), proved checkProjectionDrift zero for SeriesProjection, and retired the ADR-0049 lossy-rebuild guard - integrated after builder-authorized conflict resolution and a conductor live-DB repair, with a view-safe atomic recoverStranded guard added across three verification iterations
**Duration:** 1h30m
**Verification:** PASS (iteration 3)
**Files changed:** 11
**Tests added:** 6
**ADRs written:** 0051-series-projection-drift-reaches-zero-via-column-drop-and-guard-retirement.md

---

## 2026-08-02 09:45 -- Verification failed: series-d5tpn - Drop the externally-sourced columns, prove drift zero, retire the lossy-rebuild guard

**Type:** Work / Verification failure
**Task:** series-d5tpn (iteration 2)
**Iteration:** 2 of 3
**Reasons:** The iteration-2 `recoverStranded` guard throws on the exact incident state it exists to repair — the `series_next_up`/`series_episode_counts` views block the ALTER TABLE RENAME (reproduced against SQLite 3.49.1), and the unguarded call converts a booting-but-degraded app into a hard startup crash with the cache table dropped; both new tests omit the views from their fixtures, so they pass vacuously; ADR-0051 again records claims the code does not honor. Iteration-1 items 3-5 (metadata gap, backup description, criterion narrowing) verified fixed and honest.
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker into the same worktree (iteration 3 — final before escalation)

---

## 2026-08-02 09:15 -- Verification failed: series-d5tpn - Drop the externally-sourced columns, prove drift zero, retire the lossy-rebuild guard

**Type:** Work / Verification failure
**Task:** series-d5tpn - Drop the externally-sourced columns from series_list and series_detail, prove drift zero, retire the lossy-rebuild guard
**Iteration:** 1 of 3
**Reasons:** The worker's out-of-band run against the LIVE database created the cache tables empty before the rename could run, stranding 370/4624 real rows under the old names (measured live regression: dashboard counts zeroed) — the exact hazard class the retired ADR-0049 guard existed to prevent; the ordering hazard is unguarded in code; the 11 compensating events bypassed the ADR-0032 composer's audit metadata (permanently untagged); the pre-task backup was a raw WAL file copy, not VACUUM INTO. The drift-zero claim itself verified honest (independently re-run: SeriesProjection 0 discrepancies); backdrop_ref retention principled and documented; 509/509 tests.
**Live-DB repair (conductor, 09:10):** empty cache tables dropped, populated tables renamed into place, views cycled — verified 4624/370 under the new names, series_next_up=44, series_episode_counts=104. Safety backups: backups/mediatheca-pre-repair-20260802-091003.db and -091031.db (VACUUM INTO); the worker's raw-copy pre-task backup also retained.
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker into the same worktree (code guard + ADR corrections only; live DB off-limits)

---

## 2026-08-02 09:07 -- Task verified and completed: games-h4mrd - Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration

**Type:** Work / Task completion
**Task:** games-h4mrd - Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration
**Summary:** Reconstructed play-session history from the 204 cumulative Game_play_time_set totals via pure PlaySessionMigration.plan plus an operator-triggered SSE migration with a read-only dry-run preview (ADR-0034 guardrail 2, added in iteration 2 after a verifier FAIL) - table-covered games win outright behind the integrity gate, reconstruction-only games get a dateless prior-playtime lump plus per-delta sessions, and the cursor carries across via Steam_observed_total_reconciled
**Duration:** 54m
**Verification:** PASS (iteration 2)
**Files changed:** 9
**Tests added:** 26
**ADRs written:** none (ADR-0050 amended per the task's sanctioned fold)

---

## 2026-08-02 09:05 -- Verification failed: games-h4mrd - Reconstruct play-session history from the 204 cumulative Game_play_time_set totals

**Type:** Work / Verification failure
**Task:** games-h4mrd - Reconstruct play-session history from the 204 cumulative Game_play_time_set totals
**Iteration:** 1 of 3
**Reasons:** ADR-0034 guardrail 2 (dry-run preview + explicit confirm) silently unimplemented despite the task's What binding it; the seven-field preview report contract computed by MigrationPlan but read by nothing; integrity-gate refusals invisible to the operator (clean `complete` while a slug was silently skipped); ADR-0050 addendum claims a preview path that does not exist
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker into the same worktree

---

## 2026-08-02 08:26 -- Task verified and completed: journal-w3sbq - Correct Journal's README to the first-class play-session event model — and to the read-model owners that actually exist, since there is no JournalProjection.fs

**Type:** Work / Task completion
**Task:** journal-w3sbq - Correct Journal's README to the first-class play-session event model — and to the read-model owners that actually exist, since there is no JournalProjection.fs
**Summary:** Corrected Journal's README to the first-class play-session event model (ADR-0050) and the real read-model owners (GameProjection, PlaytimeTracker, PlaySessionProjection) in place of the nonexistent JournalProjection
**Duration:** 10m
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 0
**ADRs written:** none

---

## 2026-08-02 08:22 -- Batch started: [series-d5tpn, journal-w3sbq, games-h4mrd]

**Type:** Work / Batch start
**Tasks:** series-d5tpn - Drop the externally-sourced columns from series_list and series_detail, prove the drift check reports zero for SeriesProjection, and retire the lossy-rebuild guard, journal-w3sbq - Correct Journal's README to the first-class play-session event model — and to the read-model owners that actually exist, since there is no JournalProjection.fs, games-h4mrd - Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration
**Parallel:** yes (3 workers - the entire ready set of 3, unblocked by the builder-authorized conflict resolutions of administration-kv7dp and games-p6vkz; file surfaces are near-disjoint (series projection/Administration guard retirement vs journal README-only vs games migration), no merge-order annotation needed beyond landing them in verifier-return order)

---

## 2026-08-02 08:21 -- Task verified and completed: games-p6vkz - Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor

**Type:** Work / Task completion
**Task:** games-p6vkz - Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor
**Summary:** Play sessions and pre-tracking playtime are first-class Games events keyed on (game, gaming day); the Steam sync collapsed into one pure Record_steam_observed_total decision with its cursor derived via the two-fold aggregate (TotalPlayTimeMinutes vs SteamObservedMinutes); steam_playtime_snapshot deleted; game_play_session is now checkpoint-tracked PlaySessionProjection - verified PASS 2026-08-01, integrated after builder-authorized conflict resolution, re-verified 508/508 + Fable build on the merged tree
**Duration:** 32m
**Verification:** PASS (iteration 1)
**Files changed:** 20
**Tests added:** 22
**ADRs written:** 0050-play-sessions-first-class-events-two-fold-cursor.md

---

## 2026-08-02 08:17 -- Task verified and completed: administration-kv7dp - Block projection rebuild for handlers with out-of-band writers — rebuilding SeriesProjection today permanently destroys 780 refreshes' worth of TMDB metadata plus 23 Jellyfin-materialized episodes

**Type:** Work / Task completion
**Task:** administration-kv7dp - Block projection rebuild for handlers with out-of-band writers — rebuilding SeriesProjection today permanently destroys 780 refreshes' worth of TMDB metadata plus 23 Jellyfin-materialized episodes
**Summary:** Rebuild is refused outright, server-side, for projections registered in lossyRebuildProjections (SeriesProjection today) at both the SSE rebuild route and CinemarcoImport's post-import loop - guard verified PASS on 2026-08-01, integrated after builder-authorized conflict resolution against the landed t9bzx registry, re-verified 486/486 on the merged tree
**Duration:** 20m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 3
**ADRs written:** 0049-rebuild-blocked-outright-for-projections-with-out-of-band-writers.md

---

## 2026-08-01 21:09 -- Work session ended

**Type:** Work / Session end
**Duration:** 2h26m (batch start 18:43 → session end 21:09)
**Completed:** 7 (first-try PASS: 7, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 2 — both verified PASS (iteration 1) but stranded at squash-merge by real conflicts with already-landed siblings; never a verifier failure. (1) administration-kv7dp: conflicts with administration-t9bzx in `src/Server/Administration.fs` (derived `projectionTables` vs `lossyRebuildProjections` guard, same region) and the administration README; work preserved on `aw/administration-kv7dp` @ 1b8ccbb, worktree `.worktrees/administration-kv7dp` kept, task stays in doing/. (2) games-p6vkz: conflicts with administration-c3nvp in `src/Server/Composition.fs` and with games-w4tzc in `tests/Server.Tests/GamesTests.fs`; work preserved on `aw/games-p6vkz` @ d0d292b, worktree `.worktrees/games-p6vkz` kept, task stays in doing/. Both worktrees are clean (all work committed) — nothing uncommitted to salvage. Builder resolves manually or asks for a re-run against current main; series-d5tpn / journal-w3sbq / games-h4mrd stay blocked behind them.
**Dispatches:** infrastructure-e4kwm: 1, administration-t9bzx: 1, administration-kv7dp: 1, administration-c3nvp: 1, games-p6vkz: 1, games-w4tzc: 1, series-m7fdk: 1, series-r2xhv: 1, series-q8jwc: 1
**Commits:** 14 (1 session-start reconcile of stranded refine edits, 5 batch starts, 7 task integrations, this session-end entry)
**Session-start churn reconciliation:** 2 recognized machine-shape commits (the 17:25 capture and 17:52 refine, each matched to its own protocol entry), 0 human commits since the 2026-08-01 15:55 boundary. Nothing flagged, no whats-next write. Additionally found the 17:52 refine session's sync-gate edits to games-p6vkz/h4mrd uncommitted in the working tree — committed as `0e71afa` before batch 1 so worktrees (which fork from committed HEAD) would carry the current specs. Done by hand (`lib/session-start-churn.mjs` still absent from plugin 0.9.2).
**Vision-conformance:** none — batch aligns with vision. The entire batch executes the deterministic-rebuild workstream: it serves the vision's Operability & Observability arc (the event substrate must be inspectable and trustworthy) and infrastructure-e4kwm added the "Replayable" Design Principle the rest of the batch enforces. No task pulls toward an Out of Scope (v1) item; no media-experience work was displaced (none was ready). Judged by hand — `lib/vision-conformance.mjs` is now present in 0.9.2 but this vision has no "What success looks like"/"Non-goals" sections for `extractVisionSections` to find (eighth consecutive session judged manually).
**Batch mix:** 100% product-facing (7 tasks). Classified by hand and on substance: every task changed production code, tests, or the project's own domain doctrine (ADRs/vision/context-map for e4kwm). By `classifyTask`'s letter, the six tasks whose FILE_LIST includes an ADR under `.agentheim/knowledge/decisions/` would read harness — same known heuristic quirk as prior session-end entries.
**Carry-over:** `.agentheim/contexts/games/todo/{games-p6vkz,games-h4mrd}` refine edits: committed at session START (`0e71afa`, chore(games): reconcile stranded refine edits) — orphaned bookkeeping from the 17:52 modeling session, committed so worker worktrees saw current specs. No `.agentheim/`-owned files stranded at session end. left behind (user WIP, 2 files — modified `src/Client/Pages/Settings/Views.fs`, untracked `Mediatheca Directions.html`, both pre-existing at session start; protected through both merge aborts via `git reset --merge`). `.worktrees/administration-kv7dp`: kept (owner: administration-kv7dp, verified PASS but merge-conflicted, branch @ 1b8ccbb, nothing uncommitted). `.worktrees/games-p6vkz`: kept (owner: games-p6vkz, verified PASS but merge-conflicted, branch @ d0d292b, nothing uncommitted).

**Notes carried out of this run:**

1. **Three workers independently numbered their ADR 0043** (e4kwm, t9bzx, kv7dp) and two later workers claimed 0045 (c3nvp, p6vkz) — parallel-provisional numbering worked as designed; conductor renumbered t9bzx→0044 at integration (`lib/adr-allocation.mjs` absent from 0.9.2, done by hand); kv7dp's and p6vkz's renumbering is pending their conflict resolution (kv7dp's provisional 0043 and p6vkz's provisional 0045 both collide with landed ADRs — renumber to the then-free numbers when integrating).
2. **games-w4tzc's premise was stale**: the idempotence guards it was filed to add have existed since 2026-02-15 (`git blame`: a4d1697/2dcfca4); the 1019-events-per-1019-streams evidence was historical accumulation from before those guards. Worker correctly shipped tests-only. **Follow-up advisory:** backlog task `administration-z6ymt` (event-log purge) cites the same disproven "~1000 duplicates" premise — re-check before promoting.
3. **Merge-conflict lesson for parallel same-BC batches:** both stranded tasks conflicted exactly where the Phase 3 pre-scan predicted overlap (same-file adjacent regions). Sequential merge ordering surfaced the conflicts safely, but landing order determines who strands. An automatic rebase-and-reverify path (ADR-0032's named future enhancement) would have closed both without builder involvement.
4. **Human-eye checks pending for the builder:** (a) games-p6vkz's GameDetail prior-playtime line (in the stranded worktree); (b) series-q8jwc's series list / detail / dashboard Next Up render parity (landed on main).
5. **Doc-drift backlog candidates surfaced by verifiers (none blocking):** series README's "Next Up" wording (any-rewatch vs default-rewatch, predates workstream); series README's ubiquitous-language "Status — Active / Finished / Abandoned" entry contradicting `SeriesStatus`; `game_metadata_cache.cover_ref`/`backdrop_ref` must join `Administration.imageRefColumns` in the same commit that drops the projection columns (games-a7dqx).
6. **Until series-t3jkv lands** (new backlog item from q8jwc), series added after the one-time cache seed read `Overview = ""` / `TmdbRating = None` — disclosed in ADR-0048's accepted-tradeoffs and the series README.

**Harness defects observed (installed `agentheim` plugin 0.9.2, not this project):**

1. **`checkpoint` still does not fold in the vacated `doing/` lifecycle path** (ADR-0057 / agentic-workflow-w2njd) — staged by hand on all 7 checkpoints; git recorded clean renames every time. Tenth consecutive session.
2. **`lib/session-start-churn.mjs`, `lib/vacuum-guard.mjs`, `lib/adr-allocation.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` absent from 0.9.2** — churn reconciliation, batch-mix, and ADR renumbering done by hand. (`lib/vision-conformance.mjs`, `lib/protocol-rotation.mjs`, `lib/index-rotation.mjs` ARE present — the prior session-end note understated 0.9.2's contents.)
3. **PowerShell 5.1 mangles JSON args to the lifecycle CLI** (inner double quotes stripped → `invalid-opts-json`); Git Bash with forward-slash paths works. All CLI invocations this session went through Bash.
4. **CWD-drift near-miss confirmed the doctrine's warning verbatim:** a squash-merge run while the shell sat inside a worktree produced the predicted "Already up to date" self-merge no-op; caught by the doctrine's own first-check (`git rev-parse --abbrev-ref HEAD`) and re-run from the main tree. No damage.

---

## 2026-08-01 21:08 -- Task verified and completed: series-q8jwc - Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged

**Type:** Work / Task completion
**Task:** series-q8jwc - Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged
**Summary:** SeriesProjection query functions now compose DTOs by joining series_metadata_cache and the series_next_up/series_episode_counts views at query time - closing the stale-but-read gap left by series-r2xhv with every Shared DTO and the whole client byte-identical
**Duration:** 29m
**Verification:** PASS (iteration 1)
**Files changed:** 9
**Tests added:** 9
**ADRs written:** 0048-series-reads-composed-from-metadata-cache-at-query-time.md

---

## 2026-08-01 20:37 -- Batch started: [series-q8jwc]

**Type:** Work / Batch start
**Tasks:** series-q8jwc - Compose Series read models from the metadata cache — join in the query function, not the API layer — keeping every Shared DTO and the whole client unchanged
**Parallel:** no (1 worker - the ready set is exactly one task; series-d5tpn, journal-w3sbq and games-h4mrd remain blocked on the stranded administration-kv7dp / games-p6vkz merges awaiting the builder)

---

## 2026-08-01 20:37 -- Task verified and completed: series-r2xhv - Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time

**Type:** Work / Task completion
**Task:** series-r2xhv - Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time
**Summary:** Cut SeriesRefresh/Jellyfin writes over to cache-only (season/episode cache seeded and cleaned up at command time, never from replay) and narrowed Series_refreshed to fire only on a real airing-status transition with previousStatus sourced from the aggregate - the projection handler now applies the transition, backward-compatible with all 780 historical events
**Duration:** 26m
**Verification:** PASS (iteration 1)
**Files changed:** 10
**Tests added:** 15
**ADRs written:** 0047-series-refreshed-narrowed-to-real-airing-status-transitions.md

---

## 2026-08-01 20:09 -- Batch started: [series-r2xhv]

**Type:** Work / Batch start
**Tasks:** series-r2xhv - Cut Series refresh and Jellyfin materialization over to cache-only writes, and narrow Series_refreshed to fire only on a real airing-status transition — making status replayable from the log for the first time
**Parallel:** no (1 worker - the ready set is exactly one task; series-q8jwc chains behind it, everything else blocked on the two stranded verified tasks awaiting builder conflict resolution)

---

## 2026-08-01 20:09 -- Task verified and completed: series-m7fdk - Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views

**Type:** Work / Task completion
**Task:** series-m7fdk - Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views
**Summary:** Renamed the Series season/episode tree into the metadata cache tier (series_episode_cache/series_season_cache) via idempotent ALTER TABLE RENAME with zero data movement, retargeted the ADR-0025 image-ref and ADR-0044 drift registries, and replaced the materialized next-up/count columns with two read-time SQL views
**Duration:** 25m
**Verification:** PASS (iteration 1)
**Files changed:** 13
**Tests added:** 11
**ADRs written:** 0046-series-episode-tree-renamed-into-cache-views-replace-materialized-columns.md

---

## 2026-08-01 19:44 -- Batch started: [series-m7fdk]

**Type:** Work / Batch start
**Tasks:** series-m7fdk - Rename the Series season/episode tree into the metadata cache tier (ALTER TABLE RENAME, zero data movement) and replace the materialized next-up/count columns with SQL views
**Parallel:** no (1 worker - the ready set is exactly one task; series-r2xhv/q8jwc/d5tpn chain sequentially behind it, journal-w3sbq and games-h4mrd are blocked on stranded games-p6vkz, series-d5tpn additionally on stranded administration-kv7dp - both verified-PASS but merge-conflicted, preserved in their worktrees for the builder)

---

## 2026-08-01 19:21 -- Task verified and completed: administration-c3nvp - Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents

**Type:** Work / Task completion
**Task:** administration-c3nvp - Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents
**Summary:** Stood up the metadata cache tier - MetadataCache.fs creates game_metadata_cache (typed RAWG/HLTB columns, seeded once from game_detail behind a settings marker) and the movie_metadata_cache stub, wired into Composition.buildApp outside every ProjectionHandler and registered Cache in tableRegistry
**Duration:** 20m
**Verification:** PASS (iteration 1)
**Files changed:** 8
**Tests added:** 5
**ADRs written:** 0045-metadata-cache-tier-typed-per-bc-tables.md

---

## 2026-08-01 19:17 -- Task verified and completed: games-w4tzc - Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards

**Type:** Work / Task completion
**Task:** games-w4tzc - Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards
**Summary:** Confirmed the Set_steam_app_id and Add_family_owner idempotence guards already exist in Games.decide (predating this session) and added the four missing Expecto cases making the acceptance criteria explicit - no production change needed, the re-emission premise was stale
**Duration:** 12m
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 4
**ADRs written:** none

---

## 2026-08-01 19:05 -- Batch started: [administration-c3nvp, games-p6vkz, games-w4tzc]

**Type:** Work / Batch start
**Tasks:** administration-c3nvp - Stand up the metadata cache tier — per-BC typed tables that survive Drop/Init/replay, seeded once from current projections, following the ImageStore and JellyfinStore precedents, games-p6vkz - Model play sessions and pre-tracking playtime as first-class Games events — replacing the non-event-sourced game_play_session table, the republished-SUM Game_play_time_set, and the unrebuildable steam_playtime_snapshot cursor, games-w4tzc - Make the retained external-identity Game events idempotent — Set_steam_app_id and Add_family_owner re-emit on every sync for values that never change, unlike Set_steam_library_date which already guards
**Parallel:** yes (3 workers - the entire ready set of 3; nothing held back. games-p6vkz and games-w4tzc annotated for sequential merge ordering (both edit Games.decide and the games README); administration-c3nvp and games-p6vkz both touch Administration.tableRegistry entries - merge those sequentially too. administration-kv7dp excluded: PASSED verification but its squash-merge conflicts with landed administration-t9bzx in Administration.fs + README - preserved in its worktree for manual resolution, task remains in doing/)

---

## 2026-08-01 19:00 -- Task verified and completed: administration-t9bzx - Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions

**Type:** Work / Task completion
**Task:** administration-t9bzx - Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions
**Summary:** Added Administration.tableRegistry classifying all 27 durable tables as Projected | Cache | Imperative, derived projectionTables from it (retiring the hand-maintained duplicate), and surfaced Cache/Imperative row counts via a new additive IAdminApi.getUnrebuildableTableStats method
**Duration:** 25m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 4
**ADRs written:** 0044-every-durable-table-classified-projected-cache-imperative.md

---

## 2026-08-01 18:54 -- Task verified and completed: infrastructure-e4kwm - Record the event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description — and amend ADR-0012's retracted justification

**Type:** Work / Task completion
**Task:** infrastructure-e4kwm - Record the event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description — and amend ADR-0012's retracted justification
**Summary:** Recorded the event-worthiness doctrine (ADR-0043, global scope) - an event records an observation of the user's own engagement, a cache records a third party's description - and amended ADR-0012 in place, retracting its two cache-justification passages
**Duration:** 14m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 0
**ADRs written:** 0043-event-worthiness-doctrine-observation-vs-third-party-cache.md

---

## 2026-08-01 18:43 -- Batch started: [administration-kv7dp, administration-t9bzx, infrastructure-e4kwm]

**Type:** Work / Batch start
**Tasks:** administration-kv7dp - Block projection rebuild for handlers with out-of-band writers — rebuilding SeriesProjection today permanently destroys 780 refreshes' worth of TMDB metadata plus 23 Jellyfin-materialized episodes, administration-t9bzx - Classify every durable table as Projected, Cache or Imperative in one registry, and derive projectionTables from it — replacing tribal knowledge currently encoded as scattered comments explaining omissions, infrastructure-e4kwm - Record the event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description — and amend ADR-0012's retracted justification
**Parallel:** yes (3 workers - the entire ready set of 3; nothing held back. administration-kv7dp and administration-t9bzx annotated for sequential merge ordering - both touch src/Server/Administration.fs and the administration README)

---

## 2026-08-01 17:52 -- Modeling / Refined: games-p6vkz, games-h4mrd - pre-tracking playtime becomes its own dateless event, and the Steam sync cursor is retired

**Type:** Modeling / Refine
**BC:** games
**Status after:** todo (both)
**Summary:** The builder rejected the original model's treatment of a game's first Steam observation. `PlaytimeTracker.fs:667-680` records a game's entire pre-Mediatheca lifetime total as a single play session dated at `rtime_last_played` — for a 500-hour game that asserts a 500-hour day that never happened and poisons the heatmap. Playtime accumulated before tracking began is a *different fact*: it has a magnitude but no date. Modelled as `Prior_play_time_recorded of minutes` (dateless, counts toward the total, writes no session row, so the Journal excludes it by construction rather than by filter). Threshold for "this is history, not a sitting" is a named 960-minute (16h) constant living in `Games.decide`, not the adapter — which turns the whole Steam sync policy into a pure, directly-testable `Record_steam_observed_total` decision.

**Second-order consequence, and the reason `steam_playtime_snapshot` could be deleted rather than guarded:** once prior playtime and every session are in the log, the aggregate knows what has been accounted for, so the cursor is derivable. But the naive derivation is a trap — deleting a Steam-sourced session drops our counted total below Steam's reported total (Grounded: log 2282, Steam ~2952) and the next sync would fabricate a 670-minute phantom session. Resolved with **two folds over the same events**: `TotalPlayTimeMinutes` (prior + current session minutes — what the user asserts) and `SteamObservedMinutes` (prior + steam deltas as *originally* recorded, never reduced by a later correction or removal — what Steam has told us). The second is computable because the correction and removal events already carry `previousMinutes`. A sixth event, `Steam_observed_total_reconciled`, carries the existing cursor across the migration cutover for the ≤12 games that have a snapshot row, and remains the standing resync primitive.

**Net simplifications:** the `Imported` play-session source is gone (every migrated session is now a genuinely observed delta on a genuinely known date), and `games-h4mrd`'s written-down accepted cost — *"one day in early 2026 shows ~2952 minutes for Grounded"* — is eliminated rather than tolerated. The migration now introduces **no invented dates at all**.

**Builder decisions taken during refinement:** 16h fixed constant (not a setting, not `rtime_last_played`-driven); retire the cursor by deriving from the log (not keep-and-guard); GameDetail breaks prior playtime out as its own line rather than silently summing it.

**Not filed as a separate task:** the `steam_playtime_snapshot` hazard the builder asked to capture is absorbed into `games-p6vkz`, which deletes the table outright. Filing a guard task alongside a task that removes the guarded thing would be bookkeeping noise. `administration-t9bzx`'s registry note now records that its `Imperative` entry is expected to disappear in `games-p6vkz`'s diff.

**Split into:** none
**ADRs written:** none — the `games-p6vkz` ADR spec grew the two-fold design and the threshold rationale.

---

## 2026-08-01 17:25 -- Modeling / Captured: 15 tasks — separate third-party metadata from domain events so projection rebuilds are deterministic

**Type:** Modeling / Capture
**BC:** infrastructure, administration, series, games, journal, movies (a single capture genuinely landing in six BCs — the sanctioned multi-BC index exception)
**Filed to:** todo (12), backlog (3)
**Summary:** A drift check reported 2437 discrepancies, all in SeriesProjection. Root cause verified against the live DB: `SeriesRefresh.applyToProjection` writes TMDB results straight into `series_list`/`series_detail`/`series_seasons`/`series_episodes` while `Series_refreshed` is a no-op summary event, so 780 refreshes plus 23 Jellyfin-materialized episodes exist only in the live tables — and `rebuildProjection`'s `Drop; Init; replay` would destroy all of it at one button press. A broader audit found the same category across BCs (rawg_rating, hltb_hours, tmdb_rating on movies/series/episodes, artwork refs, and 7668 `Game_play_mode_added` events carrying Steam Store category tags — 43% of the 17,638-event log), and the mirror-image defect in playtime: `game_play_session` is a non-event-sourced imperative table holding 42 rows of unrebuildable user history, while `Game_play_time_set` carries a republished `SUM` that is provably non-monotonic (Grounded 2952→2282, Windrose 975→375, Starcom 979→811).

Orchestrator ran strategic-modeler, tactical-modeler and architect. Doctrine landed as an **event-worthiness test** (`infrastructure-e4kwm`, scope global): an event records an observation of the user's own engagement; a cache records a third party's description; operative form is re-derivability. Plus an identity-card second clause that keeps `name`/`year`/`poster_ref`/`genres` as projection columns. ADR-0012 is **amended, not superseded** — two passages retracted.

**Builder decisions taken during capture:**
1. Now-slice = guard + Series chain + play sessions (tasks 1-12 to `todo/`); Games attribute cache, Movies cutover and the log purge stay in `backlog/`.
2. `Series_refreshed` is **narrowed to real airing-status transitions**, not retired. Verified basis: 566 of the 780 historical events already carry null statuses (no change) and 214 carry real transitions, and replaying `Series_added_to_library.status` + those 214 reproduces live status for 103 of 105 series. So `status` survives as a projection column instead of being demoted to cache.
3. Auto-promotion to InFocus narrows to **newly recorded sessions only** — correcting or moving an existing session no longer yanks a Retired game back into focus.

Drift is to reach zero by **removing columns, not by ignoring them** — no ignore-list on `diffTable`, which stays byte-for-byte as ADR-0031 wrote it. The one-time event-log purge is deferred at the builder's explicit direction (the deployed live version cannot take the migration yet).

**Filed to todo:** administration-kv7dp, administration-t9bzx, administration-c3nvp, infrastructure-e4kwm, series-m7fdk, series-r2xhv, series-q8jwc, series-d5tpn, games-p6vkz, games-h4mrd, games-w4tzc, journal-w3sbq
**Filed to backlog:** games-a7dqx, movies-v2gkh, administration-z6ymt
**ADRs written:** none yet — 7 are specified in the task bodies (global ×1, administration ×3, series ×1, games ×2), to be written by the workers.

---

## 2026-08-01 15:55 -- Work session ended

**Type:** Work / Session end
**Duration:** 5m (batch start 15:50 → session end 15:55)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-x7k2p: 1
**Commits:** 3 (1 batch start, 1 task integration, this session-end entry)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 0 human commits since the 2026-08-01 15:44 boundary — all three intervening commits carry `[design-system-x7k2p]` / `[games-status-vocabulary-reconcile]` trailers. Nothing flagged, no `whats-next.md` write. Done **by hand** — `lib/session-start-churn.mjs` still absent from installed plugin 0.9.2, eighth consecutive session.
**Vision-conformance:** none — batch aligns with vision. design-system-x7k2p synchronizes doctrine prose with ADR-0042, which itself implements the "Add InFocus status to Game lifecycle" bullet of `## Remaining v1 Work`; a prose-sync of an already-conforming decision cannot diverge. Judged **by hand** — `lib/vision-conformance.mjs`'s section-heading expectations still don't match this vision's shape (seventh consecutive session).
**Batch mix:** 100% bookkeeping (1 task). Classified **by hand** (`lib/vacuum-guard.mjs` still absent, eighth consecutive session) and on substance: `type: chore` touching only two `.agentheim/` BC READMEs — project-knowledge bookkeeping; by `classifyTask`'s letter (READMEs are not protocol/INDEX/state surfaces) it would read harness — same known heuristic quirk as prior session-end entries.
**Carry-over:** left behind (user WIP, 2 files — modified `src/Client/Pages/Settings/Views.fs` and untracked `Mediatheca Directions.html`, both pre-existing at session start, neither under `.agentheim/`). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain — `.worktrees/design-system-x7k2p` torn down cleanly after integration (no `node_modules` link was ever created; markdown-only task).
**Board state after this session:** `todo/` empty, `doing/` empty across every BC. `backlog/` holds only `integration-hebjs` (one-click Steam family import, unrefined). Next session hits the vacuum guard unless something is refined+promoted — vision.md still has no `## Open questions` section.

**Notes carried out of this run:**

1. **First-try PASS on a verifier-note handoff task:** the games iteration-1 verifier's suggested fix (file a design-system follow-up rather than make an out-of-scope cross-BC edit) closed cleanly one session later — the two stale README lines it quoted were the exact and complete edit set, confirmed by the refinement sweep and this verifier's own repo-wide `Playing`/`OnHold` check.
2. **Cross-BC single-line waiver worked as designed:** the worker prompt explicitly waived rule 5 for the one journal README line the task's What section names; the worker touched exactly that line and nothing else.

**Harness defects observed (installed `agentheim` plugin 0.9.2, not this project):**

1. **`claim`/`complete` with explicit `{"context":"design-system"}` override worked first try** — the deriveContext multi-word-id quirk from last session never fired because the override was passed preemptively. Doctrine note holds: always pass the explicit context override.
2. **`checkpoint` still does not fold in the vacated lifecycle path** (ADR-0057 / agentic-workflow-w2njd) — vacated `doing/` path staged by hand again; git recorded a clean rename. Ninth consecutive session.
3. **`lib/session-start-churn.mjs`, `lib/vacuum-guard.mjs`, `lib/vision-conformance.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` still absent from 0.9.2** — churn reconciliation, batch-mix, and vision-conformance each done manually.
4. **`references/worker-return-format.md` absent from the dev checkout** (`C:\src\heimeshoff\agentic\agentheim\skills\work\` has only SKILL.md) — resolved from the plugin cache 0.9.2 copy instead; the resolve-plugin-file-convention fallback worked as documented.

---

## 2026-08-01 15:54 -- Task verified and completed: design-system-x7k2p - Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)

**Type:** Work / Task completion
**Task:** design-system-x7k2p - Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)
**Summary:** Synced the design-system README lifecycle-status vocabulary entry and the journal README Games subscription line with ADR-0042 five-state unification (Backlog | InFocus | Retired | Abandoned | Dismissed, 1:1, reconciliation closed)
**Duration:** 3m
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 0
**ADRs written:** none

---

## 2026-08-01 15:51 -- Batch started: [design-system-x7k2p]

**Type:** Work / Batch start
**Tasks:** design-system-x7k2p - Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)
**Parallel:** no (1 worker - the ready set was exactly one task; MAX_PARALLEL never bound and nothing was held back)

---

## 2026-08-01 15:48 -- Modeling / Promoted: design-system-x7k2p - Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)

**Type:** Modeling / Promote
**BC:** design-system
**From → To:** backlog → todo

---

## 2026-08-01 15:48 -- Modeling / Refined: design-system-x7k2p - Sync README lifecycle-status vocabulary with the five-state unification (ADR-0042)

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Verifier-authored handoff task verified against disk: both stale lines exist exactly as cited (design-system README:23 six-state vocabulary entry, journal README:32 "(Playing/Completed transitions)"), ADR-0042 exists. A completeness sweep of living doctrine (vision, context-map, all BC READMEs) for `Playing`/`OnHold` found one spot the handoff missed — `context-map.md:21`, the Games core-language line still listing the seven-state pre-remodel vocabulary — fixed directly in this pass (modeling-owned artifact, precedent: the games refine corrected vision.md in-pass). Task Notes now record that the two README edits are the complete remaining set. All four acceptance criteria machine-checkable, none `[human-eye]`; ADR-0059 convention check n/a (synchronizes prose with an already-recorded decision, establishes nothing new). No split, no orchestrator round — everything was verifiable directly on disk.
**Split into:** none
**ADRs written:** none

---

## 2026-08-01 15:44 -- Work session ended

**Type:** Work / Session end
**Duration:** 27m (batch start 15:17 → session end 15:44)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-status-vocabulary-reconcile: 2
**Commits:** 3 (1 batch start, 1 task integration, this session-end entry)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 0 human commits since the 2026-08-01 13:47 boundary — both intervening commits carry `[games-status-vocabulary-reconcile]` modeling trailers. Nothing flagged, no `whats-next.md` write. Done **by hand** — `lib/session-start-churn.mjs` still absent from installed plugin 0.9.2, seventh consecutive session.
**Vision-conformance:** none — batch aligns with vision. games-status-vocabulary-reconcile implements the "Add InFocus status to Game lifecycle (Backlog → InFocus → Retired / Abandoned / Dismissed)" bullet of `## Remaining v1 Work` verbatim (the bullet was pre-updated to the five-state vocabulary during the same day's refinement, so there is zero drift by construction). Judged **by hand** — `lib/vision-conformance.mjs`'s section-heading expectations still don't match this vision's shape (sixth consecutive session).
**Batch mix:** 100% product-facing (1 task). Classified **by hand** (`lib/vacuum-guard.mjs` still absent, seventh consecutive session) and on substance: `type: refactor` with every production file in src/ or tests/; by `classifyTask`'s letter the ADR-0042 file in FILE_LIST would flip it to harness — same known heuristic quirk as the two prior session-end entries.
**Carry-over:** left behind (user WIP, 2 files — modified `src/Client/Pages/Settings/Views.fs` and untracked `Mediatheca Directions.html`, both pre-existing at session start, neither under `.agentheim/`). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain.
**Worktree husks:** none left. `.worktrees/games-status-vocabulary-reconcile` torn down cleanly; its root `node_modules` junction (created by the conductor this session so the verifier could run the Fable build) was `rmdir`ed FIRST, and the shared `node_modules` verified intact (180 entries) after `git worktree remove --force`.
**Board state after this session:** `todo/` empty, `doing/` empty. `backlog/` holds `integration-hebjs` and the new `design-system-x7k2p` (README vocabulary sync handoff filed at the verifier's direction). Next session hits the vacuum guard unless something is promoted — vision.md still has no `## Open questions` section.

**Notes carried out of this run:**

1. **One verifier-fail iteration, and it was the gate working as designed:** iteration 1 shipped green code (Expecto 445/445, `npm run build` clean) but left the design-system and journal BC READMEs asserting the retired six-state vocabulary with no handoff. The verifier's suggested fix — file a design-system backlog task rather than make an out-of-scope cross-BC edit — was followed exactly in iteration 2 (`design-system-x7k2p`) and the iteration-2 verifier confirmed the handoff is real (quotes both stale lines, cites ADR-0042, four falsifiable criteria) before passing.
2. **Legacy upcast proven by test, not inspection:** four new Expecto tests cover `"OnHold"`→InFocus and `"Completed"`→Retired at both the deserialization and full replay/projection-rebuild layers, plus any-status auto-promotion (Retired/Abandoned/Dismissed → InFocus on play). The only surviving `'Completed'`/`'OnHold'` SQL literals are the idempotent migration UPDATEs in `GameProjection.fs:79-88`.
3. **What still needs the builder's eyes** (`[human-eye]` criterion, correctly left unchecked): do the Retired and Dismissed badges read as quiet, distinct states beside the colored ones — on the StyleGuide page and GameDetail.

**Harness defects observed (installed `agentheim` plugin 0.9.2, not this project):**

1. **`deriveContext` mis-derives the BC from multi-word task ids:** `claim games-status-vocabulary-reconcile` rejected with `not-found` because the id's BC prefix is ambiguous; worked on retry via the documented `{"contexts":{...}}` / `{"context":"games"}` override on both `claim` and `complete`. Worth a doctrine note: always pass the explicit context override for ids whose BC name is a prefix of a longer hyphenated id.
2. **`checkpoint` still does not fold in the vacated lifecycle path** (ADR-0057 / agentic-workflow-w2njd) — vacated `doing/` path staged by hand again; git recorded a clean rename. Eighth consecutive session.
3. **`lib/session-start-churn.mjs`, `lib/vacuum-guard.mjs`, `lib/adr-allocation.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` still absent from 0.9.2** — churn reconciliation, batch-mix, and ADR-number finalization (0042 verified collision-free by hand against 0001–0041) each done manually.
4. **Forward slashes in the CLI's `fileList` JSON worked first try on Windows again** — fifth session confirming the workaround.

---

## 2026-08-01 15:43 -- Task verified and completed: games-status-vocabulary-reconcile - Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages

**Type:** Work / Task completion
**Task:** games-status-vocabulary-reconcile - Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages
**Summary:** Remodeled the Games lifecycle to five states (Backlog/InFocus/Retired/Abandoned/Dismissed) — OnHold removed, Completed renamed Retired, Playing never added — with parse-time-only legacy upcast in both server mappers, and unified DesignSystem.LifecycleStatus 1:1 with Shared.GameStatus, wiring DesignSystem.statusBadge into the Games list, GameDetail, StyleGuide, and Dashboard
**Duration:** 41m (dispatch 15:17 → PASS verdict 15:58, incl. one verifier-fail re-dispatch)
**Verification:** PASS (iteration 2)
**Files changed:** 17
**Tests added:** 4
**ADRs written:** 0042-games-lifecycle-remodeled-to-five-states.md

---

## 2026-08-01 15:41 -- Verification failed: games-status-vocabulary-reconcile - Remodel the game lifecycle to five states

**Type:** Work / Verification failure
**Task:** games-status-vocabulary-reconcile - Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed
**Iteration:** 1 of 3
**Reasons:** design-system BC README's "Lifecycle status vocabulary" entry still documents the six-state vocabulary and advertises the now-closed reconciliation as outstanding, journal BC README still says "(Playing/Completed transitions)" — cross-BC ubiquitous-language rot with no sanctioned handoff filed (NEW_BACKLOG_ITEMS: none). Tests (445/445) and npm run build both green; scope otherwise clean.
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-08-01 15:17 -- Batch started: [games-status-vocabulary-reconcile]

**Type:** Work / Batch start
**Tasks:** games-status-vocabulary-reconcile - Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages
**Parallel:** no (1 worker - the ready set was exactly one task; MAX_PARALLEL never bound and nothing was held back)

---

## 2026-08-01 15:13 -- Modeling / Promoted: games-status-vocabulary-reconcile - Remodel the game lifecycle to five states — Backlog, InFocus, Retired (né Completed), Abandoned, Dismissed; OnHold removed, Playing never added — and unify DesignSystem.LifecycleStatus 1:1, wiring statusBadge into the Games pages

**Type:** Modeling / Promote
**BC:** games
**From → To:** backlog → todo

---

## 2026-08-01 14:20 -- Modeling / Refined: games-status-vocabulary-reconcile - Remodel the game lifecycle to five states

**Type:** Modeling / Refine
**BC:** games
**Status after:** todo
**Summary:** The captured either/or (add `Playing` vs document a mapping) was overtaken by a builder remodel of the lifecycle itself: `Playing` will never exist (InFocus explicitly covers "actively playing", alongside near-future intent and want-to-recommend), `OnHold` is removed as a distinction that never mattered (current OnHold games become InFocus), and `Completed` is renamed **Retired** ("played enough for now" — chosen over Played/Finished/Satisfied). `Dismissed` stays (Backlog games never to be played, kept for the record) and gains a muted badge variant, so `Shared.GameStatus` and `DesignSystem.LifecycleStatus` unify 1:1 at five states. Task-048's any-status auto-promotion was deliberately reaffirmed — a play session on a Retired/Abandoned/Dismissed game still pulls it to InFocus (the capture-era question "does replaying resurface it" answered yes). Migration specced as parse-time upcast only ("OnHold"→InFocus, "Completed"→Retired in both DU↔string mappers) plus projection rebuild — no event rewriting. Touchpoint sweep against real code found the stats layer the capture missed (`GamesCompleted`/`CompletedPerYear`/game `CompletionRate`, four SQL literals on `'Completed'`) and the stale styleguide.md pointer (in-app StyleGuide is canonical, design-system-sg8kd). vision.md's two Playing/OnHold lifecycle claims corrected in the same pass. Task retyped decision→refactor (the decision is now made; the worker records it as an ADR at execution). Acceptance criteria went 3 → 10. No split. No orchestrator round — the decision was the builder's and all mechanics were verified directly against the code.
**Split into:** none
**ADRs written:** none (one specified as an execution deliverable)

---

## 2026-08-01 13:47 -- Work session ended

**Type:** Work / Session end
**Duration:** 49m (batch start 12:58 → session end 13:47)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** administration-k3vmt: 1
**Commits:** 3 (1 batch start, 1 task integration, this session-end entry)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 4 human commits since the 2026-08-01 10:20 boundary (`3bca8bb` searchLibrary removal, `3611a53` Brotli/gzip compression, `3df9150` Ctrl+K games cold-start fix, `62375ce` desktop run modes + STAThread). Two governed-surface hits flagged and written to `whats-next.md`: `62375ce` touches `src/Desktop/Program.fs` (subject of ADR-0018) and `3bca8bb` touches `src/Shared/Shared.fs` (the ADR-0004 API contract, with a client-side-search decision recorded only in a code comment). No task auto-filed. Done **by hand** — `lib/session-start-churn.mjs` still absent from installed plugin (0.9.2), sixth consecutive session.
**Vision-conformance:** none — batch aligns with vision. administration-k3vmt sits inside the recognized "Operability & Observability — Admin Console" workstream and fixes the mobile-unreachability gap (mobile-first principle). Judged **by hand** against `## Remaining v1 Work` / `## Out of Scope (v1)` / `## Design Principles` — `lib/vision-conformance.mjs`'s `extractVisionSections` still expects section headings this vision doesn't use (fifth consecutive session). Note, not a flag: vision.md's Admin-Console "Shipped" bullet still says "tabbed `/admin` shell" — wording now stale after this task dissolved that shell into Settings.
**Batch mix:** 100% product-facing (1 task). Classified **by hand** — `lib/vacuum-guard.mjs` still absent from plugin 0.9.2 (sixth consecutive session). Heuristic quirk worth recording again: administration-k3vmt is `type: refactor` and its worker DID list its own ADR (0041) in `FILE_LIST`, which by `classifyTask`'s letter (`.agentheim/knowledge/decisions/` in the file set → harness) would classify this obviously product-facing UI restructure as harness — the same no-meaningful-reason sensitivity the 2026-08-01 10:20 entry predicted. Classified on substance: every production file touched is client UI or e2e spec.
**Carry-over:** left behind (user WIP, 1 file — untracked `Mediatheca Directions.html`, pre-existing, not under `.agentheim/`). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain.
**Worktree husks:** none left. `.worktrees/administration-k3vmt` torn down cleanly; its `node_modules` junction (needed this session — the task ran the Fable build and the full CI-gated Playwright suite in the worktree) was `rmdir`ed FIRST, and the shared `node_modules` verified intact (184 entries, `vite` present) after `git worktree remove --force`. The ADR-0037 junction-recursion trap was armed this session for the first time and the unlink-first discipline held.
**Board state after this session:** `todo/` empty, `doing/` empty. `backlog/` holds `integration-hebjs` and `games-status-vocabulary-reconcile`. The next session will hit Phase 2's vacuum guard unless the builder promotes something — `vision.md` still has no `## Open questions` section, so the guard will have no open item to surface.

**Notes carried out of this run:**

1. **First-try PASS on a 20-file, 940-insertion client restructure.** The refinement's precision paid for itself: the two traps it named in advance (the `Settings.State.init` unconditional-batch cold-start trap and the criteria-7/8 dirty-banner-under-lazy-sections contradiction) were both handled correctly by the worker and both proven by dedicated network-assertion e2e specs rather than static inspection — `settings-admin-sections.spec.ts` asserts the exact query set (`["getProjectionStats"]`) on a `/settings` visit and zero admin queries on a cold start away from Settings.
2. **The verifier ran the full triple gate** — Expecto 441/441, `npm run build` exit 0, `CI=1` Playwright 18/18 executed (not skipped) — and confirmed the `admin-surgery.spec.ts` CI gate survived verbatim while its URL assertion became a DOM assertion, exactly as the criterion demanded.
3. **One minor doc inaccuracy left as-is** (verifier's judgment, endorsed): the task's Outcome claims the `adminSectionCard`-vs-`.velvet-card` deviation is documented in ADR-0041's Consequences; it isn't, but it IS documented at both code sites where a maintainer would meet it. Not worth a re-dispatch.
4. **What still needs the builder's eyes** (`[human-eye]` criterion, correctly left unchecked): does `/settings` read as one coherent page rather than two apps stapled together — and `DesignSystem.navGroupBottom`'s `mt-auto` layout with a single item in the bottom group.

**Harness defects observed (installed `agentheim` plugin 0.9.2, not this project):**

1. **`checkpoint` still does not fold in the vacated lifecycle path** (ADR-0057 / agentic-workflow-w2njd) — vacated `doing/` path staged by hand again; git recorded a clean rename. Seventh consecutive session.
2. **`lib/session-start-churn.mjs`, `lib/vacuum-guard.mjs`, `lib/adr-allocation.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` all still absent from 0.9.2** — churn reconciliation, batch-mix, and ADR-number finalization (0041 verified collision-free by hand against 0001–0040) each done manually. Sixth/seventh consecutive session depending on module.
3. **`lib/vision-conformance.mjs` present but inapplicable to this project's vision shape** — fifth consecutive session.
4. **Forward slashes in the `checkpoint` CLI's `fileList` JSON worked first try on Windows again** — fourth session confirming the workaround.

---

## 2026-08-01 13:42 -- Task verified and completed: administration-k3vmt - Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button

**Type:** Work / Task completion
**Task:** administration-k3vmt - Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button
**Summary:** The /admin console dissolved into Settings as six lazy-loaded collapsible inline sections below Data Imports; the sidebar bottom nav drops to a single Settings button, the ADR-0023 Follow-teardown re-keys to leaving-Settings plus a new collapse trigger, and the ADR-0034 dirty banner Go-to-Projections becomes an in-page expand+scroll
**Duration:** 44m
**Verification:** PASS (iteration 1)
**Files changed:** 20
**Tests added:** 4
**ADRs written:** 0041

---

## 2026-08-01 12:57 -- Batch started: [administration-k3vmt]

**Type:** Work / Batch start
**Tasks:** administration-k3vmt - Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button
**Parallel:** no (1 worker - the ready set was exactly one task; MAX_PARALLEL never bound and nothing was held back)

---

## 2026-08-01 12:19 -- Modeling / Promoted: administration-k3vmt - Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button

**Type:** Modeling / Promote
**BC:** administration
**From → To:** backlog → todo

---

## 2026-08-01 12:19 -- Modeling / Refined: administration-k3vmt - Dissolve the /admin console into Settings

**Type:** Modeling / Refine
**BC:** administration
**Status after:** todo
**Summary:** All six open mechanics settled against the real code, and three things the capture had not noticed were found and resolved. **Criteria 7 and 8 as captured contradicted each other:** the ADR-0034 dirty banner is client-derived from `AdminProjections.Model.Stats`, which today is always populated because `Admin.State.init` eagerly fires all six children's `Load` — under lazy sections a store left dirty by an earlier session would show no warning at all. Resolved at the builder's direction with a single named exception: `getProjectionStats` fires on every `/settings` visit regardless of collapse state; every other section stays lazy. **A second trap: `Settings.State.init`'s `Cmd` is batched by root `State.init` unconditionally**, so a naive absorption would fire admin queries at every app cold start, not just on visiting Settings — `Admin.State.init`'s `Cmd`, by contrast, is deliberately created-and-dropped at root init today. The eager stats load is therefore specified into root `State.Url_changed`'s `Settings` branch, never into `Settings.State.init`, with its own criterion. **Third: the banner's link target is asserted by URL in a committed spec** (`admin-surgery.spec.ts:292`, `toHaveURL(/#\/admin\/projections$/)`), and a **third** e2e spec the capture didn't name (`event-tail-follow.smoke.spec.ts`) also navigates `/#/admin/events`. **Builder decisions:** `Pages/Admin/Types.fs`+`State.fs` survive as a headless composite child (only `Views.fs`'s header+`tabBar` deleted), preserving the `Surgery_msg` → Projections-reload handler ADR-0034 depends on; per-section deep-linkability **dropped** — `/settings` is the only address, the banner's "Go to Projections" becomes an in-page expand+scroll, and the `Settings of section option` route alternative was declined; all six sections render on mobile, nothing viewport-hidden. **Settled without a question:** load-on-first-expand with no refetch (all six children already return exactly one load `Cmd` from `init`, so deferral is mechanical); both teardown triggers call the one exported idempotent `EventBrowser.State.stopFollowing`; Settings' existing DaisyUI collapse idiom is *uncontrolled* and cannot be reused as-is — lazy loading and collapse-stops-the-poll both need the open state in the model. Two criteria added the capture missed entirely: re-pointing `Stream_detail`'s "← Back to Event Store" link and the sidebar-highlight predicate (`Route.isAdminSection`) at `Settings`, so the drill-in doesn't orphan itself. Acceptance criteria went 10 → 16. No split. ADR judged warranted (retracts p0jka's shell shape, amends ADR-0023's trigger and adds a second, moves ADR-0034's banner, drops deep-linkability, establishes the lazy-section convention) — the worker writes it.
**Split into:** none
**ADRs written:** none (one judged warranted at execution time)

---

## 2026-08-01 11:12 -- Modeling / Captured: administration-k3vmt - Dissolve the /admin console into Settings

**Type:** Modeling / Capture
**BC:** administration
**Filed to:** backlog
**Summary:** The builder rejected having two buttons in the sidebar's bottom group for what is one destination — Settings (Integrations + Data Imports) and Admin (Events / Projections / Health / Images / Jobs / Surgery) are both this BC's operator surface. Offered three merge shapes at capture; the builder chose **fully inline**: the `/admin` shell dissolves entirely, its six tab views become collapsible sections on the Settings page below Data Imports, `Router.Page.Admin` is removed with every former admin URL (including the legacy `/events` alias) resolving to `Settings`, and `Sidebar.bottomNavItems` drops to one item. `Stream_detail` (`/admin/streams/<id>`) survives as its own page — it is parameterized and cannot inline, and was already a top-level `Page` case rather than an `AdminTab` variant. Filed to `backlog/` because the shape is chosen but six mechanics are open, chief among them whether Settings absorbs the six admin child models directly or `Pages/Admin/State.fs` survives headless as a composite child; where the ADR-0034 dirty banner's "Go to Projections" link points once `/admin/projections` stops existing; how ADR-0023's Follow-epoch teardown re-keys from "leaving `Admin _`" to "leaving `Settings`" *plus* a genuinely new section-collapse trigger; and whether the change warrants its own ADR given it nudges ADR-0017's client shell, ADR-0023 and ADR-0034 at once.

---

## 2026-08-01 10:20 -- Work session ended

**Type:** Work / Session end
**Duration:** 15m (batch start 10:05 → session end 10:20)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-q7wv3: 1
**Commits:** 3 (1 batch start, 1 task integration, this session-end entry)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back, and the conflict pre-scan had nothing to order.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 0 human commits since the 2026-08-01 02:34 boundary. All three intervening commits (`db58d39` capture, `5d7bf04` refine, `32cede7` promote) carry `[integration-q7wv3]` subject trailers, so none reads as untrailed. No governed-surface drift; nothing written to `whats-next.md` by this pass (the write below came from the vision-conformance pass instead). Done **by hand** — `lib/session-start-churn.mjs` is still absent from the installed plugin (0.9.2).
**Vision-conformance:** **integration-q7wv3: diverges from non-goal "Trakt.tv / Jellyfin sync (v2)"** (`vision.md` → `## Out of Scope (v1)`) — flagged, and `whats-next.md` written accordingly. Identical in shape and in substance to the finding recorded for `integration-007` on 2026-08-01 02:34: the flag is honest but the drift is **not introduced by this task**. This is a *bug fix completing already-shipped capability* — the Jellyfin sync arc spans integration-001, -002, -003, -m4k7p, -007 and now -q7wv3, with ADRs 0010, 0011, 0012, 0039 and 0040 governing it. The document is stale, not the work. **Fourth consecutive session** recording this contradiction; `whats-next.md` now recommends the 2026-07-21 Admin-Console precedent (move the arc into `## Remaining v1 Work` as recognized v1 work) rather than continuing to re-report it.

Note on mechanization: as on 2026-07-31 and both 2026-08-01 sessions, `lib/vision-conformance.mjs`'s `extractVisionSections` could not drive this pass — it looks for `## What success looks like` / `## Non-goals`, and this project's `vision.md` carries neither (it uses `## Remaining v1 Work`, `## Out of Scope (v1)`, `## Design Principles`). Judged by hand against those surfaces instead. **Fourth consecutive session** recording this mismatch.
**Batch mix:** 100% product-facing (1 task). Classified **by hand** against `classifyTask`'s documented heuristic — `lib/vacuum-guard.mjs` is still absent from plugin 0.9.2, so `formatBatchMixLine` could not be invoked. `integration-q7wv3` is `type: bug`, which the amended path-aware heuristic classifies product-facing when its touched files are *entirely* product surfaces; its `FILE_LIST` is three server sources, two test files and one BC README — none under `lib/`, `skills/`, `agents/`, `references/`, `evals/`, or `.agentheim/knowledge/decisions/`. (ADR 0040 was reported in `ADRS_WRITTEN`, not in `FILE_LIST`, so it does not pull the classification toward harness — worth noting because a bug task that listed its own ADR in `FILE_LIST` would classify differently for no meaningful reason.) **Fifth consecutive session** reporting this module absent.
**Carry-over:** left behind (user WIP, 4 files — `README.md`, `package.json`, `src/Desktop/Program.fs` all tracked-modified, and untracked `"Mediatheca Directions.html"`; all pre-existing, unchanged by this session, none under `.agentheim/`). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain.
**Worktree husks:** none left. `.worktrees/integration-q7wv3` was torn down cleanly. **No `node_modules` junction was ever created for it** — deliberately: this project's `npm test` is `dotnet run` and needs no node deps, and the task was server-only, so the ADR-0037 junction-recursion data-loss trap was avoided by never arming it rather than by unlinking carefully. The shared `node_modules` was verified intact (180 entries, `vite` present) both before and after `git worktree remove --force`.
**Board state after this session:** `todo/` empty, `doing/` empty. `backlog/` holds `integration-hebjs` and `games-status-vocabulary-reconcile`. The next session will hit Phase 2's vacuum guard unless the builder promotes something — and `vision.md` still has no `## Open questions` section, so the guard will have no open item to surface either.

**Notes carried out of this run:**

1. **The previous session's verifier failure paid off one task later.** `integration-007` FAILed iteration 1 for returning `ADRS_WRITTEN: none` while narrating its design in the task file. That finding was passed forward into this worker's dispatch prompt as a lead — and this worker wrote **ADR 0040** unprompted-by-the-task (the refinement had explicitly concluded "no ADR"; the worker judged otherwise and was right). The verifier then checked 0040 *in the opposite direction* — is it ceremony restating the pre-refined plan? — and found it substantive: two genuine decisions, each with real rejected alternatives and hard evidence. **First-try PASS.** The refinement's "no ADR needed" call was the one thing it got wrong, and the loop caught it without a re-dispatch.
2. **The task's most load-bearing criterion was the one written most precisely.** Criterion 2 required `AND source = 'jellyfin' AND still_ref IS NULL` **in the `UPDATE`'s own `WHERE` clause**, not merely at candidate selection — a guard against a TMDB refresh landing between the SELECT and the UPDATE. The verifier checked this against the emitted SQL rather than the prose around it, and criterion 7 (the `else` branch byte-for-byte unchanged) against the actual diff hunk (`@@ -181,8 +196,28 @@`: the only removed lines are the two-line skip). Criteria specific enough to be checked mechanically got checked mechanically.
3. **Test suite: Expecto 435 → 441 (6 added).** Genuine TDD cycle reported and corroborated by the verifier's own run (exit code 0). New coverage includes a real-SQLite test driving `backfillEpisodeStill` directly at a *non-candidate* row — the only way to prove the WHERE-clause guard rather than the selection logic. `npm run build` was **not** run and was not needed: server-only change, no client code touched, and no `node_modules` in the worktree by design.
4. **The `[human-eye]` criterion was left correctly unchecked, second session running.** No proxy claim in the worker's `## Outcome`; the verifier confirmed it. **What still needs the builder's eyes:** *Interview with the Vampire* S3 showing real thumbnails instead of the placeholder TV icon, against the live Jellyfin server. This is the same check that failed on 2026-08-01 and produced this very task — if it still shows placeholders, that is a new bug to capture, not a re-open.
5. **The accepted-repetition tradeoff is now recorded, not merely decided.** An episode Jellyfin genuinely has no image for is re-attempted every sync, forever. ADR 0040 records why that is acceptable (the candidate set drains itself, because `SeriesRefresh`'s `INSERT OR REPLACE` omits `source` and so resets it to the `'tmdb'` default even when TMDB has no still either) and names the escalation path (a side table, with its own ADR) should it ever bite.

**Harness defects observed (in the installed `agentheim` plugin 0.9.2, not this project):**

1. **`checkpoint` still does not fold in the vacated lifecycle path** — ADR-0057 / `agentic-workflow-w2njd` specify that naming a task file's new location should add the moved-from path to `changed`. It did not; the conductor staged the vacated `doing/` path by hand, after which git recorded the transition as a clean rename (`R071`). **Sixth consecutive session reporting this.**
2. **`lib/vacuum-guard.mjs` is still absent from plugin 0.9.2** — batch mix classified by hand. Fifth consecutive session.
3. **`lib/session-start-churn.mjs`, `lib/adr-allocation.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` all still absent from 0.9.2.** Each step was performed by hand or was moot. Notably `finalizeAdrNumbering` could not run, so **ADR 0040's number was verified by hand** against the real decisions directory (0001-0039 present, no collision, no gap). Fifth consecutive session.
4. **`lib/vision-conformance.mjs` is present but inapplicable to this project's vision shape** — see the Vision-conformance note above. Fourth consecutive session.
5. **The CWD-drift trap the skill warns about fired once, and the warning worked.** An early discovery command `cd`'d into `.agentheim/knowledge/decisions` and the persisted working directory silently broke the two following reads. Caught immediately because the skill names this failure mode explicitly; every subsequent tree-targeting command used `git -C <abs-path>` or a fresh `cd <repo-root>` prefix. No git write was ever issued from a drifted directory.
6. **Forward slashes in the `checkpoint` CLI's `fileList` JSON worked first try again on Windows** — confirming the documented workaround holds for a third session.

---

## 2026-08-01 10:18 -- Task verified and completed: integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap

**Type:** Work / Task completion
**Task:** integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap
**Summary:** Episode rows materialized before integration-007 wired up the still fetch now get their Jellyfin still backfilled on the next sync, via a new SeriesProjection.backfillEpisodeStill UPDATE path scoped to source=jellyfin AND still_ref IS NULL re-checked in the WHERE clause itself
**Duration:** 12m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 6
**ADRs written:** 0040

---

## 2026-08-01 10:05 -- Batch started: [integration-q7wv3]

**Type:** Work / Batch start
**Tasks:** integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap
**Parallel:** no (1 worker — the ready set was exactly one task; MAX_PARALLEL never bound and nothing was held back)

---

## 2026-08-01 10:03 -- Modeling / Promoted: integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-08-01 10:43 -- Modeling / Refined: integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo
**Summary:** Both shape questions the capture deliberately left open are now settled against the real code, and a third option the capture had not considered was found and rejected on evidence. **Where the backfill lives:** inside `materializeMissingEpisodes` as a widened skip predicate, not a separate sweep — the Jellyfin item id the fetch needs (`ep.Id`) is already in `seriesBatch`, whereas a query-driven sweep would have to re-resolve it through `jellyfin_episode`, a table `clearAll` wipes and Phase 1 only repopulates for TMDB-matched series; and the "leave `materializeMissingEpisodes` untouched" argument that held for `integration-007` (purely additive, numstat 33/0) does not hold here because the bug *is* that function's skip predicate. The missing UPDATE path becomes `SeriesProjection.backfillEpisodeStill`, repeating `source='jellyfin' AND still_ref IS NULL` **in the WHERE clause** so criteria 2 and 4 are enforced by the statement rather than only at candidate selection — a TMDB refresh landing between the SELECT and the UPDATE cannot be clobbered. **The refetch guard:** none — repetition is accepted and recorded, at the builder's decision. The candidate set is small by construction and drains on its own, because `SeriesRefresh`'s `INSERT OR REPLACE` omits the `source` column and so resets it to the `DEFAULT 'tmdb'`, releasing a row even when TMDB has no still either. **Sentinel `still_ref` was ruled out on hard evidence, not taste:** `("series_episodes", "still_ref")` is entry 8 of ADR 0025's `imageRefColumns` registry, whose `getReferencedImageRefs` collects every non-null value as a live ref — a sentinel would register as a reference to a nonexistent file inside a registry that documents itself as LOAD-BEARING and warns a stale entry "risks a purge deleting a still-referenced image". **Third option found and rejected:** `JellyfinBaseItem.PrimaryImageTag` is already parsed and sitting unused in `seriesBatch` — a zero-state guard for free — but whether `ImageTags` is populated on `/Shows/{id}/Episodes` is unverified, and the failure mode runs the wrong way: an unpopulated field makes the backfill skip everything and the bug survive silently, whereas an unconditional attempt at least wastes a visible 404. Acceptance criteria went 7 → 9 (two added for the WHERE-clause guard and a distinct `StillsBackfilled` counter); no split, no ADR — none of the four governing ADRs is challenged.
**Split into:** none
**ADRs written:** none

---

## 2026-08-01 10:42 -- Modeling / Captured: integration-q7wv3 - Episodes materialized before integration-007 never get a still — the backfill gap

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** backlog
**Summary:** The builder performed `integration-007`'s pending `[human-eye]` criterion 6 against the live Jellyfin server and found *Interview with the Vampire* S3 still showing the placeholder TV icon. Root-caused during this session against the running code and the live DB: `materializeMissingEpisodes` skips any `(season, episode)` already in `getExistingEpisodeKeys` — a query that ignores `still_ref` — so `fetchStill` is unreachable for the seven `source='jellyfin'`, `still_ref=NULL` rows an earlier sync created while the fetch was still ADR 0012's stub; and `materializeEpisode` is `INSERT OR IGNORE`, so no UPDATE path exists to fill the column even if the skip were lifted. Zero `*-jellyfin.jpg` files exist on disk — the fetch has never run in production, and every series synced before `366defb` is affected. `integration-007`'s code is correct; its scope never reached pre-existing rows. Captured to `backlog/` rather than `todo/`: the builder scoped the fix to Jellyfin-sourced rows only (keeping ADR 0012's supplement boundary intact) but deliberately left two shape questions for refinement — how to bound refetch attempts against an episode Jellyfin genuinely has no image for, and whether the backfill widens `materializeMissingEpisodes`' skip predicate or runs as a separate sweep.

---

## 2026-08-01 02:34 -- Work session ended

**Type:** Work / Session end
**Duration:** 28m (batch start 02:06 → session end 02:34)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-007: 2
**Commits:** 4 (1 batch start, 1 task integration, this session-end entry, plus the preceding modeling refine/promote pair which belong to the same builder request but ran under `modeling`, not this session)
**Parallelism:** batch of 1 — the ready set was exactly one task. `MAX_PARALLEL` never bound; nothing was held back, and the conflict pre-scan had nothing to order.
**Session-start churn reconciliation:** 0 recognized machine-shape commits, 0 human commits since the 2026-08-01 01:37 boundary. Both intervening commits (`7de08e8` the integration-007 refine, `07a3adf` the promote) carry `[integration-007]` subject trailers, so neither reads as untrailed. No governed-surface drift; nothing written to `whats-next.md` by this pass (the write below came from the vision-conformance pass instead). Done **by hand** — `lib/session-start-churn.mjs` is still absent from the installed plugin (0.9.2).
**Vision-conformance:** **integration-007: diverges from non-goal "Trakt.tv / Jellyfin sync (v2)"** (`vision.md` → `## Out of Scope (v1)`) — flagged, and `whats-next.md` written accordingly. The flag is honest but the drift is **not introduced by this task**: the Jellyfin sync arc has been under active construction for months (integration-001, -002, -003, integration-m4k7p, now -007; ADRs 0010, 0011, 0012, 0039), and integration-007 specifically closes a deferral ADR 0012 recorded by name — completion of already-shipped capability, not new scope. The document is stale, not the work. This is the same shape as the 2026-07-21 finding that led to the Admin Console being absorbed into `## Remaining v1 Work` as *recognized* v1 work at the builder's direction; the Jellyfin arc now sits in exactly that position. Recorded as a flag rather than waved through, because a vision that contradicts the codebase makes every future conformance pass re-report the same thing.

Note on mechanization: as on 2026-07-31 and 2026-08-01 01:37, `lib/vision-conformance.mjs`'s `extractVisionSections` could not drive this pass — it looks for `## What success looks like` / `## Non-goals`, and this project's `vision.md` carries neither (it uses `## Remaining v1 Work`, `## Out of Scope (v1)`, `## Design Principles`, and the Operability Boundary paragraph nested inside a roadmap section). Judged by hand against those surfaces instead. **Third consecutive session** recording this mismatch.
**Batch mix:** 100% product-facing (1 task). Classified **by hand** against `classifyTask`'s documented heuristic — `lib/vacuum-guard.mjs` is still absent from plugin 0.9.2, so `formatBatchMixLine` could not be invoked. `integration-007` is `type: feature`, which the heuristic classifies product-facing unconditionally; its touched files are server source, tests, one BC README, and two ADRs. **Fourth consecutive session** reporting this module absent.
**Carry-over:** left behind (user WIP, 4 files — `README.md`, `package.json`, `src/Desktop/Program.fs` all tracked-modified, and untracked `"Mediatheca Directions.html"`; all pre-existing and unchanged by this session, none under `.agentheim/`). No `.agentheim/`-owned stranded files. No git-registered non-main worktrees remain.
**Worktree husks:** none left. `.worktrees/integration-007` was torn down with its root `node_modules` junction unlinked **first** via a targeted `(Get-Item …).Delete()` guarded on `LinkType -eq 'Junction'` — never a recursive delete, which follows the junction into the shared real `node_modules` — with the shared copy verified intact (180 entries, `vite` present) both before and after.
**Board state after this session:** `todo/` empty, `doing/` empty. `backlog/` holds `integration-hebjs` and `games-status-vocabulary-reconcile`. The next session will hit Phase 2's vacuum guard unless the builder promotes something — and `vision.md` has no `## Open questions` section, so the guard will have no open item to surface either.

**Notes carried out of this run:**

1. **The verifier earned its keep again, this time on a decision that was never written down.** The worker shipped correct code with green tests and returned `ADRS_WRITTEN: none`, reasoning in the task's own `## Outcome` that the shape "was already fully resolved during this task's 2026-08-01 refinement and recorded in this task file's 'Resolved implementation shape' section". The verifier called that exactly what it was — task-file narration standing in for an ADR, the substitution check 6 forbids — and FAILed iteration 1 on it alone. It also caught the sharper consequence: **ADR 0012's Consequences section still told a maintainer that materialized stills are `NULL`, which is precisely the deferral this diff closed.** The ADR corpus was actively lying about the code. Iteration 2 wrote ADR 0039 and corrected 0012 in place; nothing about the production code changed between iterations.
2. **The load-bearing design decision was found during *refinement*, not during work, and it was a near-miss.** The task as originally captured said to store the still at TMDB's canonical `stills/{slug}-sXXeYY.jpg`. `SeriesRefresh.fs:99-110` short-circuits its own download on `ImageStore.imageExists` — so a Jellyfin file at that path would have made TMDB **skip its own download and keep the Jellyfin bytes permanently**, silently violating the task's own third acceptance criterion with no test-visible failure at the point of edit. The refinement caught it and moved to a distinct `-jellyfin.jpg` suffix, which needs zero changes to `SeriesRefresh`/`Tmdb`. ADR 0039 now records this so a future "tidy up the suffix" commit has something to trip over.
3. **The accepted-orphan tradeoff has a named home in another BC's machinery.** A `-jellyfin.jpg` file lingers unreferenced once TMDB enriches. The verifier required this be recorded rather than left implicit, and checked the claim against ADR 0025 directly: `series_episodes.still_ref` is in that ADR's fifteen-pair `imageRefColumns` registry, so an orphaned Jellyfin still is found by the existing orphan scan and reclaimable through the operator-triggered `/admin/images` purge — no new administration code, but manual rather than automatic.
4. **One honest coverage gap, judged non-blocking by the verifier:** acceptance criterion 4 ("the new adapter fetch reuses `Jellyfin.withReauthRetry`") has **no new executable test**. It was accepted on the inspectable single-expression delegation in `Jellyfin.fs` (`getPrimaryImageWithReauth` is structurally identical to `getEpisodesWithReauth`), plus the six pre-existing `JellyfinReauthTests.fs` cases that govern the shared policy, plus the new "download failure degrades to None" case covering the degrade half. Worth an assertion if that path ever becomes load-bearing.
5. **Test suites: Expecto 427 → 435 (8 added, all in the new `JellyfinStillTests.fs`); `npm run build` green (35.8s, no Fable errors).** The worker reports a genuine TDD cycle. Playwright e2e untouched at 14 flows (no client code changed).
6. **`[human-eye]` criterion left correctly unchecked, without needing a correction this time.** Unlike the 2026-08-01 01:37 session — where the worker marked a `[human-eye]` box `[x]` and the verifier had to route it back — this worker left criterion 6 unchecked on both iterations and made no proxy claim. **What still needs the builder's eyes:** a materialized episode (e.g. *Interview with the Vampire* S3) showing a real thumbnail instead of the placeholder TV icon, against a live Jellyfin server.

**Harness defects observed (in the installed `agentheim` plugin 0.9.2, not this project):**

1. **`checkpoint` still does not fold in the vacated lifecycle path** — ADR-0057 / `agentic-workflow-w2njd` specify that naming a task file's new location should add the moved-from path to `changed`. It did not, on either iteration; the conductor staged the vacated `doing/` path by hand both times, after which git recorded the transition as a clean rename. **Fifth consecutive session reporting this.**
2. **`lib/vacuum-guard.mjs` is still absent from plugin 0.9.2** — batch mix classified by hand. Fourth consecutive session.
3. **`lib/session-start-churn.mjs`, `lib/adr-allocation.mjs`, `lib/worktree-salvage.mjs`, `lib/index-entry-length.mjs` all still absent from 0.9.2.** Each step was performed by hand or was moot. Notably `finalizeAdrNumbering` could not run, so **ADR 0039's number was verified by hand** against the real decisions directory (0001-0038 present, no collision, no gap) — the verifier independently re-checked it. Fourth consecutive session.
4. **`lib/vision-conformance.mjs` is present but inapplicable to this project's vision shape** — see the Vision-conformance note above. Third consecutive session.
5. **The `checkpoint` CLI's Windows backslash / JSON-escape papercut was avoided rather than hit** — forward slashes in the `fileList` JSON worked first try on both iterations, as the 2026-08-01 01:37 session recorded. Confirming the workaround holds.

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

