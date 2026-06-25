# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

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

## 2026-05-28 -- Work session ended

**Type:** Work / Session end
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Commits:** 1 (plus 1 bookkeeping commit)
**Note:** Single-task batch — integration-004 (Steam same-day delta merge-on-conflict, regression test). 266/265 tests green. No ADR (one-line SQL semantic change mirrors existing `upsertManualPlaySession` pattern).

---

## 2026-05-28 -- Task verified and completed: integration-004 - Steam playtime sync silently drops same-day deltas

**Type:** Work / Task completion
**Task:** integration-004 - Steam playtime sync silently drops same-day deltas
**Summary:** Steam playtime sync no longer drops same-day deltas — `PlaytimeTracker.recordPlaySession` now merges minutes into the existing `(game_slug, date)` row via `ON CONFLICT DO UPDATE` instead of `INSERT OR IGNORE`, so two syncs that both land on the same gaming day (e.g. late-night session attributed via `rtime_last_played`) sum correctly and the projection total stays truthful.
**Verification:** PASS (iteration 1) — 266 tests green (was 265), one new regression test exercising the conflict path.
**Commit:** 009c156
**Files changed:** 2
**Tests added:** 1 (PlaytimeTrackerTests.fs: "Same-day Steam delta merges into the existing session row instead of being dropped")
**ADRs written:** none

---

## 2026-05-28 -- Batch started: [integration-004]

**Type:** Work / Batch start
**Tasks:** integration-004 - Steam playtime sync silently drops same-day deltas
**Parallel:** no (1 worker)

---

## 2026-05-28 -- Model / Captured: integration-004 - Steam playtime sync silently drops same-day deltas

**Type:** Model / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Bug filed straight to `todo/` — root cause and one-line fix already in hand. `runSync` correctly attributes a late-night session to the previous gaming day via `rtime_last_played`, but `recordPlaySession` uses `INSERT OR IGNORE` against `UNIQUE(game_slug, date)` (`src/Server/PlaytimeTracker.fs:108`), so a second same-day sync silently drops the delta and the minutes are lost forever (next sync sees delta=0). Fix: switch to `ON CONFLICT(game_slug, date) DO UPDATE SET minutes_played = minutes_played + excluded.minutes_played`, mirroring `upsertManualPlaySession`. All three call sites are safe with merge-on-conflict (initial-snapshot and reconciliation paths verified in acceptance criteria). Regression gap: existing "Manual sessions do not interfere with Steam delta tracking" test uses different dates per call, so the conflict path is uncovered — new test required.

---

## 2026-05-27 -- Work session ended

**Type:** Work / Session end
**Completed:** 4 (first-try PASS: 4, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Commits:** 4 (plus 1 bookkeeping commit)
**Note:** Two parallel batches of 2. Batch 1: integration-002 (Jellyfin re-auth on 401/403, ADR 0011) + design-system-002 (design-check Source of Truth → styleguide.md). Batch 2: integration-003 (surface Jellyfin SyncFailed in Settings UI) + design-system-003 (ActionMenu specimen on live StyleGuide page). Both batches split per-BC to avoid two workers touching the same BC README. All four passed verification on the first try.

---

## 2026-05-27 -- Task verified and completed: design-system-003 - Add ActionMenu specimen to the live StyleGuide page

**Type:** Work / Task completion
**Task:** design-system-003 - Add ActionMenu specimen to the live StyleGuide page
**Summary:** Added an interactive ActionMenu specimen (view / heroView / heroViewSections, with real ActionMenuItem/ActionMenuSection records) to the Components section of the live StyleGuide page and updated styleguide.md § 4 to point at it (Views.fs:1191-1283), closing finding F-2.
**Verification:** PASS (iteration 1) — npm run build clean.
**Commit:** 0d61e34
**Files changed:** 2
**Tests added:** 0 (frontend specimen)
**ADRs written:** none

---

## 2026-05-27 -- Task verified and completed: integration-003 - Surface the persisted Jellyfin sync failure in the Settings UI

**Type:** Work / Task completion
**Task:** integration-003 - Surface the persisted Jellyfin sync failure in the Settings UI
**Summary:** The persisted Jellyfin SyncFailed result is now surfaced in Settings → Jellyfin as a glassmorphic error panel (DesignSystem.glassCard + error tint) showing the persisted error message and failed-run time; the Settings model retains the full JellyfinSyncStatus instead of dropping all but the timestamp. Closes the integration-001/ADR 0010 frontend follow-up.
**Verification:** PASS (iteration 1) — npm run build clean; glassmorphism gate satisfied.
**Commit:** 5b9921d
**Files changed:** 3
**Tests added:** 0 (frontend view)
**ADRs written:** none

---

## 2026-05-27 -- Batch started: [integration-003, design-system-003]

**Type:** Work / Batch start
**Tasks:** integration-003 - Surface the persisted Jellyfin sync failure in the Settings UI, design-system-003 - Add ActionMenu specimen to the live StyleGuide page
**Parallel:** yes (2 workers)

---

## 2026-05-27 -- Task verified and completed: design-system-002 - Point design-check's Source of Truth at styleguide.md

**Type:** Work / Task completion
**Task:** design-system-002 - Point design-check skill's "Source of Truth" at styleguide.md
**Summary:** The design-check skill's "Source of Truth" now names styleguide.md as the canonical design-system doc for intent and the frontend gate; index.css/DesignSystem.fs remain authoritative for concrete values. No rule category semantics changed.
**Verification:** PASS (iteration 1)
**Commit:** d335ab7
**Files changed:** 1
**Tests added:** 0 (docs-only chore)
**ADRs written:** none

---

## 2026-05-27 -- Task verified and completed: integration-002 - Re-authenticate Jellyfin and retry once on a 401/403

**Type:** Work / Task completion
**Task:** integration-002 - Re-authenticate Jellyfin and retry once on a 401/403 during sync
**Summary:** Jellyfin sync now self-heals a rejected token — a 401/403 on any fetch triggers exactly one re-auth with the stored credentials, persists the fresh token, and retries once; a second rejection, failed re-auth, or missing credentials surface a clear "re-authentication" SyncFailed instead of looping. `fetchJsonWithAuth` now returns FetchError instead of throwing on EnsureSuccessStatusCode.
**Verification:** PASS (iteration 1) — 265 tests green, build clean, non-vacuous 401-then-200 regression test confirmed.
**Commit:** 72bb9a5
**Files changed:** 6
**Tests added:** 1 (JellyfinReauthTests.fs: 401-then-200 re-auth, 401-twice no-loop, missing/failed credentials)
**ADRs written:** 0011-jellyfin-reauth-on-401.md

---

## 2026-05-27 -- Batch started: [integration-002, design-system-002]

**Type:** Work / Batch start
**Tasks:** integration-002 - Re-authenticate Jellyfin and retry once on a 401/403 during sync, design-system-002 - Point design-check skill's "Source of Truth" at styleguide.md
**Parallel:** yes (2 workers)

---

## 2026-05-27 -- Model / Promoted: design-system-002, design-system-003, integration-002

**Type:** Model / Promote
**BC:** design-system, integration
**From → To:** backlog → todo (all three)
**Summary:** Promoted the three remaining backlog items as-is — each had concrete acceptance criteria, clear scope, and satisfied dependencies. design-system-002 (point design-check at styleguide) and design-system-003 (ActionMenu specimen on StyleGuide page) both `depends_on: [design-system-001]`, which is signed off. integration-002 (Jellyfin re-auth on 401/403) has no deps; noted as preventive robustness hardening — auth currently works, so it is the lowest-priority of the queued tasks, with one small implementation choice (how to thread re-auth through `fetchJsonWithAuth`, which currently uses `EnsureSuccessStatusCode`) left to the worker.

---

## 2026-05-27 -- Model / Refined + Promoted: integration-003 - Surface the persisted Jellyfin sync failure in the Settings UI

**Type:** Model / Refine + Promote
**BC:** integration
**From → To:** backlog → todo
**Summary:** Grounded the task in the actual code. Confirmed the server side (getJellyfinSyncStatus + persisted result) already ships from integration-001 and the status already reaches the client. Found the real gap: the `Jellyfin_sync_status_loaded` handler (Settings/State.fs:469-476) discards the `SyncFailed` error message, keeping only the timestamp — so the task needs a model field to retain the full `JellyfinSyncStatus` plus a failure panel in `jellyfinDetail` (Settings/Views.fs:994), not a pure render. Added concrete file+line references, the DU shape (`SyncFailed of error: string * lastSyncTime: string option`), the glassmorphism gate criterion, and a build-clean criterion. Frontend gate dependency (design-system-001) already satisfied — signed off today — so promoted to todo.

---

## 2026-05-27 -- Live verification: integration-001 Jellyfin sync confirmed working

**Type:** Work / Live verification
**Task:** integration-001 - Jellyfin sync silently stopped writing episode watch history
**Result:** User ran a live sync against the Jellyfin server and confirmed it worked — the previously-missing episode watch history now flows into Mediatheca. Criteria 2 & 4 (live end-to-end verification) satisfied. integration-001 is fully closed; all acceptance criteria met.

---

## 2026-05-27 -- Sign-off: design-system-001 styleguide approved

**Type:** Work / Human gate
**Task:** design-system-001 - Formalize the existing styleguide as a reviewable document
**Sign-off:** User signed off on `.agentheim/contexts/design-system/styleguide.md` — criterion 5 satisfied. The frontend gate is now OPEN: frontend tasks in any BC may be promoted to `todo/` with `depends_on: [design-system-001]`.

---

## 2026-05-27 14:12 -- Work session ended

**Type:** Work / Session end
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Commits:** 2 (plus 1 bookkeeping commit)
**Note:** design-system-001 is implementation-complete but its criterion 5 (human sign-off on styleguide.md) is still pending — awaiting user review before any frontend task may be promoted to todo. integration-001 criteria 2 & 4 (live end-to-end Jellyfin verification) are pending — no live server access during the run.

---

## 2026-05-27 14:10 -- Task verified and completed: integration-001 - Jellyfin sync silently stopped writing episode watch history

**Type:** Work / Task completion
**Task:** integration-001 - Jellyfin sync silently stopped writing episode watch history
**Summary:** Made the Jellyfin sync observable (last result — counts + error list / failure message — persisted via SettingsStore, survives restart, reachable as SyncFailed) and fixed the structural failure mode by extracting series Phase 2 into the fault-isolating `JellyfinImport.syncSeriesWatchHistory` so one throwing series/episode no longer aborts the run.
**Verification:** PASS (iteration 1) — 259 tests green, build clean, non-vacuous regression test confirmed.
**Commit:** f5cd371
**Files changed:** 7
**Tests added:** 1 regression test (fault in one series → others still written + run reports failure)
**ADRs written:** 0010-jellyfin-sync-observability-fault-isolation.md
**Pending:** criteria 2 & 4 (live end-to-end verification against the Jellyfin server) — no live access this run.

---

## 2026-05-27 14:08 -- Task verified and completed: design-system-001 - Formalize the existing styleguide as a reviewable document

**Type:** Work / Task completion
**Task:** design-system-001 - Formalize the existing styleguide as a reviewable document
**Summary:** Formalized the design system into a single canonical, reviewable `styleguide.md` (tokens, typography, glassmorphism, component patterns, theme, review process) and resolved the source-of-truth question via verbatim-plus-pointer (ADR 0009); CLAUDE.md and the BC README now point at the styleguide as canonical.
**Verification:** PASS (iteration 1) — six sections present, file+line refs spot-checked and resolve, glassmorphism reproduced verbatim, design-check drift captured as backlog items.
**Commit:** 8b6f284
**Files changed:** 6
**Tests added:** 0 (documentation task)
**ADRs written:** 0009-styleguide-canonical-artifact.md
**Sign-off:** criterion 5 (human review of styleguide.md) PENDING — frontend gate stays closed until the user signs off.

---

## 2026-05-27 14:00 -- Batch started: [design-system-001, integration-001]

**Type:** Work / Batch start
**Tasks:** design-system-001 - Formalize the existing styleguide as a reviewable document, integration-001 - Jellyfin sync silently stopped writing episode watch history
**Parallel:** yes (2 workers)

---

## 2026-05-27 13:25 -- Model / Promoted: integration-001 - Jellyfin sync silently stopped writing episode watch history

**Type:** Model / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-05-27 13:20 -- Model / Refined: integration-001 - Jellyfin sync silently stopped writing episode watch history

**Type:** Model / Refine
**BC:** integration
**Status after:** backlog
**Summary:** Ran a live read-only diagnosis against the Jellyfin server (token from backup DB, temp creds deleted after). Disproved the "expired token" hypothesis — token valid, server reachable, movies fully synced. Confirmed the real failure is series-only: The Boys S5E5–E6 (played 2026-05-26) and Gen V S2E4–E8 (played 2026-05-21) are missing despite a sync running today and all write-preconditions being met (TMDB match, episodes exist, not deduped, no episode guard in `Mark_episode_watched`). Concluded the import aborts/errors partway through series Phase 2 and the failure is invisible because `runJellyfinImport` returns Ok-with-errors and `JellyfinSync` persists only the timestamp. Rewrote the task as observability-first-then-fix, downgraded the re-auth concern to a latent follow-up.

---

## 2026-05-27 13:00 -- Model / Captured: integration-001 - Jellyfin sync silently stopped writing watch history

**Type:** Model / Capture
**BC:** integration
**Filed to:** backlog
**Summary:** Jellyfin auto-sync keeps running (last_sync today) but has written no movie watch sessions since 2026-05-01 and no episode-watched events since 2026-05-13. Investigation of the backup DB + code identified two structural weaknesses: (1) the access token is never re-authenticated on rejection — only `testJellyfinConnection` ever writes it; (2) `runJellyfinImport` returns Ok with zero counts even when both library fetches fail, and `jellyfin_last_sync` is persisted regardless of outcome, hiding the failure. Captured with a diagnose-then-harden acceptance set; exact trigger still needs confirming against the live server.

---

## 2026-05-12 -- Brainstorm: Formalize bounded contexts for agentheim migration

**Type:** Brainstorm
**Outcome:** vision extended (BCs + context map + foundation ADRs backfilled)
**BCs identified:** movies, series, games, journal, friends, curation, intelligence, integration, administration, design-system
**Summary:** Vision was locked; extension session formalized ten bounded contexts grounded in the existing code (Movies/Series/Games as sibling core BCs, Journal + Intelligence as read-side BCs, Friends/Curation/design-system as supporting, Integration/Administration as generic). Produced `.agentheim/context-map.md` and a README per BC capturing ubiquitous language drawn from the event/command DUs.
**ADRs written:** 0001 (F# fullstack), 0002 (event sourcing + CQRS), 0003 (SQLite persistence), 0004 (Fable.Remoting), 0005 (Elmish MVU), 0006 (Tailwind+DaisyUI+glassmorphism), 0007 (single-user, no auth), 0008 (ten BCs)
**Foundation tasks emitted:** decision tasks skipped — ADRs backfilled directly for the mature project. Walking-skeleton spike skipped — the app already runs end-to-end. Styleguide: design-system-001-formalize-styleguide emitted to gate future frontend tasks.

---

## 2026-05-02 -- Task Completed: 055 - Poster Hover Zoom — Stop Visual Clipping at Container Edges

**Type:** Task Completion
**Task:** 055 - Poster Hover Zoom — Stop Visual Clipping at Container Edges
**Summary:** Replaced `pb-2` with `py-2 px-2` on all 10 horizontal poster rails in Dashboard/Views.fs and added `p-1` to the SearchModal poster grid so hover scale-up is no longer clipped at scroll-container edges. Build (32.38s) and all 255 tests pass.
**Files changed:** 2 files

---

## 2026-05-02 -- Task Started: 055 - Poster Hover Zoom — Stop Visual Clipping at Container Edges

**Type:** Task Start
**Task:** 055 - Poster Hover Zoom — Stop Visual Clipping at Container Edges
**Milestone:** --

---

## 2026-05-02 -- Idea Captured: Poster Hover Zoom — Stop Visual Clipping at Container Edges

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/055-poster-hover-zoom-not-clipped.md
**Summary:** Poster cards on dashboard rails and the SearchModal grid scale on hover, but the growth is clipped at the parent's overflow boundary (rails use `overflow-x-auto` which forces both axes to clip; modal grid sits inside `overflow-y-auto` scroll container). Fix is small CSS-padding tweak on each rail and the modal grid so the scale fits inside the scrollable viewport.

---

## 2026-05-01 15:31 -- Task Completed: 054 - Dashboard Hero — Move Episode Still Inset to Top-Left

**Type:** Task Completion
**Task:** 054 - Dashboard Hero — Move Episode Still Inset to Top-Left
**Summary:** Moved the dashboard hero episode-still inset from bottom-right to top-left and suppressed the "In Focus" glow indicator when the inset is rendered (they shared the top-left corner).
**Files changed:** 1 file (plus task file move)

---

## 2026-05-01 15:31 -- Task Completed: 053 - Make Mediatheca Installable as a PWA on Mobile

**Type:** Task Completion
**Task:** 053 - Make Mediatheca Installable as a PWA on Mobile
**Summary:** Added a minimum-viable PWA setup — manifest, no-op service worker, three PNG icons generated from the Mediatheca play-circle glyph (with sRGB-converted dim-theme colours), HTML wiring, and a Giraffe `.webmanifest` MIME mapping.
**Files changed:** 7 files (plus icon-generation script and task file move)

---

## 2026-05-01 15:27 -- Batch Started: [053, 054]

**Type:** Batch Start
**Tasks:** 053 - Make Mediatheca Installable as a PWA on Mobile, 054 - Dashboard Hero — Move Episode Still Inset to Top-Left
**Mode:** Parallel (batch of 2)

---

## 2026-05-01 15:23 -- Idea Captured: Dashboard Hero — Move Episode Still Inset to Top-Left

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/054-dashboard-hero-episode-still-top-left.md
**Summary:** Move the episode-still inset on the dashboard hero from `bottom-right` to `top-left`. Title block stays anchored at the bottom. Hide the "In Focus" glow indicator (also at top-left) whenever the inset is rendered to avoid the corner collision. Same size, same plain (no-glass) treatment.

---

## 2026-05-01 14:00 -- Idea Captured: Make Mediatheca Installable as a PWA on Mobile

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/053-installable-pwa-manifest.md
**Summary:** Add the minimum-viable PWA setup so Chrome on Android shows the Install prompt and Mediatheca launches standalone from the home screen. Manual setup (manifest + tiny no-op service worker + icon set generated from the existing play-circle glyph on the dim theme) — no vite-plugin-pwa, no offline caching, no iOS support.

---

## 2026-05-01 13:25 -- Task Completed: 051 - Dashboard Hero — Use Series Backdrop as Background, Episode Still as Inset

**Type:** Task Completion
**Task:** 051 - Dashboard Hero — Use Series Backdrop as Background, Episode Still as Inset
**Summary:** Dashboard hero now picks the high-res series backdrop as the full-bleed canvas and renders the lower-res episode still as a plain (non-glassmorphic) inset thumbnail in the bottom-right above the title block. All four availability cases (both / backdrop-only / still-only / neither) handled. Build clean, 255 tests pass.
**Files changed:** 1 file

---

## 2026-05-01 13:23 -- Batch Started: [051]

**Type:** Batch Start
**Tasks:** 051 - Dashboard Hero — Use Series Backdrop as Background, Episode Still as Inset
**Mode:** Parallel (batch of 1; only task in todo)

---

## 2026-05-01 13:22 -- Task Completed: 050 - Track Navigation History So Detail-Page Back Buttons Return to the Previous Page

**Type:** Task Completion
**Task:** 050 - Track Navigation History So Detail-Page Back Buttons Return to the Previous Page
**Summary:** Added a deduped, capped `NavigationHistory` stack in the root Elmish model + `Go_back` Msg + `onBack` callback threaded into all four detail views. Empty-stack fallback navigates Movie/Series/Game detail to Dashboard with the right tab active (via `PendingDashboardTab` field, chosen over a URL query param so it survives the `Dashboard.State.init ()` reset on URL change); Friend detail falls back to the Friend list. Build clean, 255 tests pass.
**Files changed:** 8 files

---

## 2026-05-01 13:25 -- Idea Captured: Dashboard Hero — Backdrop With Episode Still Inset

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/051-dashboard-hero-backdrop-with-episode-still-inset.md
**Summary:** Dashboard hero spotlight currently shows the next episode's still full-bleed, which looks soft at hero size. Flip to use the series backdrop as the full-bleed background with the episode still as a medium thumbnail inset in the bottom-right (above the title block), no glassmorphism. Fall back to whichever single image exists when only one is available.

---

## 2026-05-01 13:15 -- Batch Started: [050]

**Type:** Batch Start
**Tasks:** 050 - Track Navigation History So Detail-Page Back Buttons Return to the Previous Page
**Mode:** Parallel (batch of 1; final task in todo)

---

## 2026-05-01 13:14 -- Task Completed: 049 - Manual Refresh Controls for Steam Link & HLTB Data

**Type:** Task Completion
**Task:** 049 - Manual Refresh Controls for Steam Link & HLTB Data
**Summary:** Added a Steam re-link refresh icon next to the Steam Store link (reuses the existing Connect-with-Steam picker so a wrong auto-attach can be corrected) and an always-visible HLTB refresh button in the HowLongToBeat card header. Adjusted `Hltb_fetched (Ok None)` so a refresh returning no data doesn't wipe existing bars. Build clean, 255 tests pass.
**Files changed:** 3 files

---

## 2026-05-01 13:10 -- Batch Started: [049]

**Type:** Batch Start
**Tasks:** 049 - Manual Refresh Controls for Steam Link & HLTB Data
**Mode:** Parallel (batch of 1; 050 deferred — also touches GameDetail/Views.fs)

---

## 2026-05-01 13:08 -- Task Completed: 048 - Remove `Playing` Status; Auto-Promote to `InFocus` on Steam Play

**Type:** Task Completion
**Task:** 048 - Remove `Playing` Status; Auto-Promote to `InFocus` on Steam Play
**Summary:** Dropped the `Playing` case from `GameStatus` (legacy `"Playing"` payloads decode to `InFocus`; one-time DB migration converts existing rows). Added `promoteToInFocusIfNeeded` helper invoked whenever a play session is recorded — wired into all three Steam-sync branches AND the manual add/edit endpoints from task 046. New `GamesPromotedToFocus` counter surfaces in the startup-sync log line. Build clean, 255 tests pass (10 new).
**Files changed:** 12 files

---

## 2026-05-01 -- Idea Refined: Track Navigation History for Detail-Page Back Buttons

**Type:** Idea Refinement
**Idea:** tasks/todo/050-back-button-navigation-history.md (renamed from 050-remove-detail-page-back-buttons.md)
**Status:** Todo (rewritten)
**Summary:** Flipped 050 from "remove the in-page back buttons" to "keep them and make them work properly." Adds a navigation history stack in the root Elmish model that records every URL change; the back button pops the stack to return to the actual previous page (any prior scene — friend detail, catalog detail, search, etc.). Empty-stack fallback: Movie/Series/Game detail → Dashboard with the matching tab active; Friend detail → Friend list. Size bumped Small → Medium.

---

## 2026-05-01 13:00 -- Batch Started: [048]

**Type:** Batch Start
**Tasks:** 048 - Remove `Playing` Status; Auto-Promote to `InFocus` on Steam Play
**Mode:** Parallel (batch of 1; 049 and 050 deferred — both touch GameDetail/Views.fs which 048 also touches, and they conflict with each other on the same file)

---

## 2026-05-01 12:56 -- Task Completed: 047 - Date Pickers Persist on Enter or Blur, Not on Change

**Type:** Task Completion
**Task:** 047 - Date Pickers Persist on Enter or Blur, Not on Change
**Summary:** New reusable `EditableDateInput` Feliz component holds the draft in local React state and only commits on Enter or blur (Escape cancels, invalid/empty drafts close silently). Wired into the SeriesDetail episode-date editor and the MovieDetail watch-session editor; new `Cancel_edit_session_date` message added so MovieDetail can close without an API call. Build clean.
**Files changed:** 6 files

---

## 2026-05-01 12:55 -- Task Completed: 046 - Editable Play Sessions on Game Detail

**Type:** Task Completion
**Task:** 046 - Editable Play Sessions on Game Detail
**Summary:** End-to-end editable Play History on Game Detail: extended `PlaySessionDto` with `Id` + `Source`, added validated CRUD API (add-merge on date collision, edit-with-collision-merge, delete) that recomputes `TotalPlayTimeMinutes` from session sum, wired the inline editor + glassmorphic delete confirmation, added 12 Expecto tests. Build clean, 245 tests pass.
**Files changed:** 8 files

---

## 2026-05-01 -- Idea Captured: Remove detail-page back buttons

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/050-remove-detail-page-back-buttons.md
**Summary:** Remove the in-page "← Back" buttons from MovieDetail, SeriesDetail, GameDetail, and FriendDetail. The browser back button is sufficient and the in-page version always wrongly navigates to the section list. Error-state recovery links and the CatalogDetail breadcrumb are kept.

---

## 2026-05-01 13:00 -- Batch Started: [046, 047]

**Type:** Batch Start
**Tasks:** 046 - Editable Play Sessions on Game Detail, 047 - Date Pickers Persist on Enter or Blur, Not on Change
**Mode:** Parallel (batch of 2; 048 and 049 deferred — both touch GameDetail/Views.fs which 046 also touches)

---

## 2026-05-01 12:34 -- Idea Captured: Manual Refresh Controls for Steam Link & HLTB Data

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/049-manual-refresh-controls-steam-and-hltb.md
**Summary:** On the Game Detail page, expose two user-controlled refresh affordances. (1) Steam re-link: surface the existing Connect-with-Steam search/picker flow as a small refresh icon next to the Steam Store link, so games already linked to a Steam App ID can be re-matched (e.g. fixing a wrong auto-attach). Reuses existing `searchSteamForGame` / `attachSteamToGame` endpoints; `attachSteamToGameCore` already preserves user-edited descriptions. (2) HLTB refresh: add an always-visible refresh icon to the HLTB card header (today the fetch button only appears when no data exists). Reuses the existing `Fetch_hltb` flow which already overwrites via `Set_hltb_hours`. One small tweak in `State.fs` so a refresh-with-no-data response doesn't erase existing bars. Frontend-only — no backend changes.

---

## 2026-05-01 -- Idea Captured: Remove `Playing` Status; Auto-Promote to `InFocus` on Steam Play

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/048-remove-playing-status-and-auto-focus-on-steam-play.md
**Summary:** Collapse the redundant `Playing` game status into `InFocus`, with legacy events/rows mapped on read and a one-time `UPDATE` to migrate existing data. During the scheduled Steam sync, any game with a newly recorded play session is auto-promoted to `InFocus` if not already there — including `Completed`, `Abandoned`, and `Dismissed` games (replays surface back on the dashboard). Promotion only fires when a session is actually recorded (not on bare `rtime_last_played` refreshes). New sync-result counter `GamesPromotedToFocus` is logged per run.

---

## 2026-05-01 -- Idea Captured: Date Pickers Persist on Enter or Blur, Not on Change

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/047-date-picker-commit-on-enter-or-blur.md
**Summary:** Native `<input type="date">` editors on TV episodes and movie watch sessions currently dispatch save+close on every onChange, so typing a partial digit (e.g. `0`) clears the field and unmounts the editor. Fix: hold draft in local React state, persist only on Enter or blur outside, keep Escape as cancel. Apply across episodes, movie sessions, and the upcoming play-session editor (task 046).

---

## 2026-05-01 -- Idea Captured: Editable Play Sessions on Game Detail

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/046-editable-play-sessions.md
**Summary:** Make the Play History list on the Game Detail page fully editable — add/edit/delete sessions (date + minutes), merge on date collision, recompute the game's total playtime from session sum after every change. Steam delta sync remains unchanged (already cannot subtract; manual rows don't interfere).

---

## 2026-04-20 13:52 -- Task Completed: 045 - Connect with Steam (manual button + auto-attach on Add Game)

**Type:** Task Completion
**Task:** 045 - Connect with Steam (manual button + auto-attach on Add Game)
**Summary:** Implemented end-to-end: Steam name-search with cached app list + fuzzy matching + year boost; reusable attach helper emitting the same events as Steam library import; auto-attach during Add Game on high-confidence match; glassmorphic candidate picker rendered at view root to avoid nested backdrop-filter. Build succeeded; 233 tests pass.
**Files changed:** 6 files

---

## 2026-04-20 13:40 -- Batch Started: [045]

**Type:** Batch Start
**Tasks:** 045 - Connect with Steam (manual button + auto-attach on Add Game)
**Mode:** Parallel (batch of 1)

---

## 2026-04-20 13:40 -- Task Completed: 044 - Game Trailer Header Cleanup & Single-Trailer UX

**Type:** Task Completion
**Task:** 044 - Game Trailer Header Cleanup & Single-Trailer UX
**Summary:** Hid the thumbnail strip when a game has only one trailer, removed the hero Play Trailer button + spinner + full-screen modal, and cleaned up the related model/msg/state plus the now-unused `getGameTrailer` endpoint. Build succeeded; 233 tests pass.
**Files changed:** 5 files

---

## 2026-04-20 13:35 -- Batch Started: [044]

**Type:** Batch Start
**Tasks:** 044 - Game Trailer Header Cleanup & Single-Trailer UX
**Mode:** Parallel (batch of 1; 045 deferred due to file conflicts with 044 on GameDetail/Shared.fs/Api.fs)

---

## 2026-04-20 13:33 -- Idea Captured: Connect with Steam (manual button + auto-attach on Add Game)

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/045-connect-with-steam.md
**Summary:** Add a "Connect with Steam" button on the Game Detail page when a game has no Steam link, and auto-attach Steam data after RAWG import in Add Game. Steam search via GetAppList + fuzzy name/year match. Auto-attach only on high-confidence single match (score ≥ 0.95, no near-tie); manual button shows a glassmorphic candidate picker when ambiguous. Unlocks Steam trailers and descriptions for RAWG-only games.

---

## 2026-04-20 13:27 -- Idea Captured: Game Trailer Header Cleanup & Single-Trailer UX

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/044-game-trailer-header-cleanup.md
**Summary:** Polish task 043: skip the thumbnail strip when a game has only one trailer, remove the "Play Trailer" button from the hero header, and clean up the now-dead modal + its state/messages and the singular `getGameTrailer` API endpoint.

---

## 2026-04-20 12:33 -- Task Completed: 043 - Game Trailer Gallery in Overview

**Type:** Task Completion
**Task:** 043 - Game Trailer Gallery in Overview
**Summary:** Added a full-width scroll-snap trailer gallery to the GameDetail Overview. New `getGameTrailers` endpoint returns all Steam + RAWG trailers; glassmorphic cards show thumbnail + play icon, swap to inline `<video>` on click with one-at-a-time playback and silent failure. Build passes, 233 tests green.
**Files changed:** 7 files

---

## 2026-04-20 12:26 -- Batch Started: [043]

**Type:** Batch Start
**Tasks:** 043 - Game Trailer Gallery in Overview
**Mode:** Parallel (batch of 1)

---

## 2026-04-20 -- Idea Captured: Game Trailer Gallery in Overview

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/043-game-trailer-gallery.md
**Summary:** Full-width scrollable gallery of all Steam/RAWG trailers above the two-column Overview on the game detail page. Streams from CDN (no local download), click-to-play thumbnails (no autoplay), individual trailers hidden if `<video>` errors. Extends task 018's single-trailer infrastructure with a new `getGameTrailers` (plural) endpoint.

---

## 2026-04-16 -- Task Completed: 042 - Series Episode Refresh Sync

**Type:** Task Completion
**Task:** 042 - Series Episode Refresh Sync (Upcoming & New Episode Awareness)
**Summary:** Added a generic nightly `ScheduledJobs` runner (Steam sync migrated onto it) and a TMDB series refresh job emitting a `Series_refreshed` event. Manual "Refresh from TMDB" action surfaced on series detail; next-episode countdown on detail page, return-date on library cards, and a new glassmorphic "Returning Soon" dashboard card.
**Files changed:** 15 files

---

## 2026-04-16 -- Batch Started: [042]

**Type:** Batch Start
**Tasks:** 042 - Series Episode Refresh Sync (Upcoming & New Episode Awareness)
**Mode:** Parallel (batch of 1)

---

## 2026-04-16 -- Idea Captured: Series Episode Refresh Sync (Upcoming & New Episode Awareness)

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/042-series-episode-refresh-sync.md
**Summary:** Nightly scheduled refresh of returning/in-production TV series via TMDB, plus manual refresh from series detail context menu. Surfaces next-episode air dates and countdowns on the series detail page, library cards, and a new "Returning Soon" list card on the TV series dashboard. Generalizes the existing daily Steam-playtime timer into a system-wide scheduled-job runner so future nightly jobs can register into it. Newly-aired episodes repopulate existing Next Up sections quietly — no separate notifications.

---

## 2026-04-09 -- Task Completed: 041 - Friend Image Drag-and-Drop Upload with Crop Editor

**Type:** Task Completion
**Task:** 041 - Friend Image Drag-and-Drop Upload with Crop Editor
**Summary:** Added drag-and-drop image upload on friend avatar with glassmorphic crop modal (circular preview, drag-to-pan, scroll-to-zoom). Extended shared types, events, projection, and API with backward-compatible CropSettings. Built full client-side crop editor with visual drag-over feedback and re-crop button.
**Files changed:** 8 files

---

## 2026-04-09 -- Task Started: 041 - Friend Image Drag-and-Drop Upload with Crop Editor

**Type:** Task Start
**Task:** 041 - Friend Image Drag-and-Drop Upload with Crop Editor
**Milestone:** --

---

## 2026-04-09 -- Idea Captured: Friend Image Drag-and-Drop Upload with Crop Editor

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/041-friend-image-drag-drop-crop.md
**Summary:** Drag-and-drop image upload onto friend avatar with automatic circular crop modal. Crop settings (position, zoom) persist per friend via CSS-based rendering. Builds on existing upload API and drag-and-drop patterns.

---

## 2026-04-09 -- Task Completed: 040 - Deploy Skill (/deploy)

**Type:** Task Completion
**Task:** 040 - Deploy Skill (/deploy)
**Summary:** Created /deploy slash command skill with 5-step pipeline (test, build, upload, deploy, cleanup) including abort-on-failure and progress reporting.
**Files changed:** 2 files

---

## 2026-04-09 -- Task Started: 040 - Deploy Skill (/deploy)

**Type:** Task Start
**Task:** 040 - Deploy Skill (/deploy)
**Milestone:** --

---

## 2026-04-09 -- Idea Captured: Deploy Skill (/deploy)

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/040-deploy-skill.md
**Summary:** Create a /deploy slash command that automates the full test-build-upload-deploy-cleanup pipeline to the Linux production server via SSH.

---

## 2026-04-09 -- Task Completed: 039 - Refresh Button on Recently Played Card

**Type:** Task Completion
**Task:** 039 - Refresh Button on Recently Played Card
**Summary:** Added refresh button to Recently Played card header. Triggers triggerPlaytimeSync, shows spinning animation while syncing, re-fetches both Games and All tab data on completion.
**Files changed:** 4 files

---

## 2026-04-09 -- Task Started: 039 - Refresh Button on Recently Played Card

**Type:** Task Start
**Task:** 039 - Refresh Button on Recently Played Card
**Milestone:** --

---

## 2026-04-09 -- Idea Captured: Refresh Button on Recently Played Card

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/039-refresh-button-recently-played.md
**Summary:** Add a refresh button to the Recently Played card on the dashboard Games tab that triggers a Steam playtime sync and refreshes both Games and All tab data. Small task — client-side only, all backend APIs already exist.

---

## 2026-03-02 -- Task Completed: 038 - IPTorrents Search Button

**Type:** Task Completion
**Task:** 038 - IPTorrents Search Button
**Summary:** Added IPTorrents search button to movie detail (when no Jellyfin ID) and series detail (always shown) pages. Uses muted pill style with magnifying glass icon, linking to iptorrents.com with URL-encoded title and sorted by seeders.
**Files changed:** 3 files

---

## 2026-03-02 -- Task Started: 038 - IPTorrents Search Button

**Type:** Task Start
**Task:** 038 - IPTorrents Search Button
**Milestone:** --

---

## 2026-03-02 -- Task Promoted: 038 - IPTorrents Search Button

**Type:** Task Promotion
**From:** backlog
**To:** todo
**Summary:** Pure client-side feature: show "Search on IPTorrents" button on movie/series detail pages when no Jellyfin play button exists. URL pre-fills title and sorts by seeders. Well-specified, ready to implement.

---

## 2026-03-02 -- Task Completed: 037 - Jellyfin Auto-Sync on App Visit

**Type:** Task Completion
**Task:** 037 - Jellyfin Auto-Sync on App Visit
**Summary:** Implemented Jellyfin auto-sync on app visit with background sync (5-min cooldown), polling status indicator, toast notifications, dashboard refresh on completion, and "Last synced" display for Steam, Jellyfin, and Steam Family integrations in Settings. All 233 tests pass.
**Files changed:** 11 files

---

## 2026-03-02 -- Idea Captured: IPTorrents Search Button

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/backlog/038-iptorrents-search-button.md
**Summary:** When a movie/series has no Jellyfin play button, show an "IPTorrents" search button that opens iptorrents.com with the title pre-filled and categories pre-filtered. Pure client-side feature — no scraping, no server changes. Movies pre-filter to movie categories (72, 48, 20, 100), series to TV categories (73, 5, 22, 99). qBittorrent integration deferred to a future task.

---

## 2026-03-02 -- Task Started: 037 - Jellyfin Auto-Sync on App Visit

**Type:** Task Start
**Task:** 037 - Jellyfin Auto-Sync on App Visit
**Milestone:** --

---

## 2026-03-02 -- Idea Captured: Jellyfin Auto-Sync on App Visit

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/037-jellyfin-auto-sync.md
**Summary:** Automatically trigger a non-blocking Jellyfin import when the user visits Mediatheca. Syncs watch history, auto-adds new items, and refreshes play-link IDs. 5-minute cooldown prevents redundant syncs. Subtle spinner during sync, toast notification with summary on completion.

---

## 2026-02-25 -- Task Completed: 036 - Activity Heatmap Monday-First Weeks

**Type:** Task Completion
**Task:** 036 - Activity Heatmap Monday-First Weeks
**Summary:** Changed activity heatmap to Monday-first weeks (ISO 8601) by adjusting start-date alignment formula and shifting day-of-week label positions. Build verified.
**Files changed:** 1 file

---

## 2026-02-25 -- Task Started: 036 - Activity Heatmap Monday-First Weeks

**Type:** Task Start
**Task:** 036 - Activity Heatmap Monday-First Weeks
**Milestone:** --

---

## 2026-02-25 -- Idea Captured: Activity Heatmap Monday-First Weeks

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/036-heatmap-monday-first.md
**Summary:** Change activity heatmap weeks to start on Monday (ISO 8601) instead of Sunday. Client-only change in Views.fs.

---

## 2026-02-25 14:45 -- Task Completed: 035 - Movies to Watch Card

**Type:** Task Completion
**Task:** 035 - Movies to Watch Card
**Summary:** Renamed "Movies In Focus" to "Movies to Watch" across full stack. Server query now returns union of in-focus and Jellyfin-available unwatched movies via LEFT JOIN (fixing N+1), shared type renamed to DashboardMovieToWatch with InFocus bool, client shows conditional crosshair badge and always-visible ghost play button.
**Files changed:** 4 files

---

## 2026-02-25 14:30 -- Task Started: 035 - Movies to Watch Card

**Type:** Task Start
**Task:** 035 - Movies to Watch Card
**Milestone:** --

---

## 2026-02-25 14:00 -- Idea Captured: Movies to Watch Card

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/035-movies-to-watch-card.md
**Summary:** Expand "Movies In Focus" card to "Movies to Watch" — showing the union of in-focus movies and unwatched Jellyfin-available movies, with always-visible ghost play buttons for Jellyfin items, crosshair badge only for in-focus ones, ordered by in-focus first then Jellyfin-only, secondary by recency.

---

## 2026-02-25 12:32 -- Task Completed: 034 - Games Tab Overhaul

**Type:** Task Completion
**Task:** 034 - Games Tab Overhaul
**Summary:** Added In-Focus Estimate hero card with clamped HLTB remaining time, converted poster scrollers excluding dismissed games, added pie chart for Status Distribution, new spider/radar chart for Genre Breakdown, per-game color-coded Monthly Play Time stacked bars, and achievements in 2fr/1fr grid. All 233 tests pass.
**Files changed:** 5 files

---

## 2026-02-25 12:23 -- Task Started: 034 - Games Tab Overhaul

**Type:** Task Start
**Task:** 034 - Games Tab Overhaul
**Milestone:** M5 (Dashboard V3)

---

## 2026-02-25 12:22 -- Task Completed: 033 - TV Series Tab Overhaul

**Type:** Task Completion
**Task:** 033 - TV Series Tab Overhaul
**Summary:** Removed Episode Activity card, converted Next Up to poster scroller excluding abandoned series, placed Recently Finished/Abandoned side-by-side, arranged Monthly Activity/Ratings/Genre in 3-column row with shared donut chart, updated backend sorting. All 233 tests pass.
**Files changed:** 4 files

---

## 2026-02-25 12:17 -- Task Started: 033 - TV Series Tab Overhaul

**Type:** Task Start
**Task:** 033 - TV Series Tab Overhaul
**Milestone:** M5 (Dashboard V3)

---

## 2026-02-25 12:16 -- Task Completed: 032 - Movies Tab Overhaul

**Type:** Task Completion
**Task:** 032 - Movies Tab Overhaul
**Summary:** Restructured Movies tab into 5-row layout with poster scrollers, added movie_crew table with director tracking from TMDB imports and startup backfill, created reusable donut chart component in Charts.fs. All 233 tests pass.
**Files changed:** 7 files

---

## 2026-02-25 12:08 -- Task Started: 032 - Movies Tab Overhaul

**Type:** Task Start
**Task:** 032 - Movies Tab Overhaul
**Milestone:** M5 (Dashboard V3)

---

## 2026-02-25 12:07 -- Task Completed: 031 - All Tab Overhaul

**Type:** Task Completion
**Task:** 031 - All Tab Overhaul
**Summary:** Removed Media Overview card and weekly summary text, merged activity heatmap and monthly breakdown into a responsive side-by-side section (no card chrome), added totals to legend, excluded abandoned series from Next Up via SQL filter, increased Next Up limit to 10. Build and 233 tests pass.
**Files changed:** 3 files

---

## 2026-02-25 12:00 -- Task Started: 031 - All Tab Overhaul

**Type:** Task Start
**Task:** 031 - All Tab Overhaul
**Milestone:** M5 (Dashboard V3)

---

## 2026-02-25 10:00 -- Idea Captured: Dashboard Overhaul V3

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/030-dashboard-overhaul-v3.md
**Summary:** Full redesign of all four dashboard tabs. All tab: remove Media Overview card, merge Monthly Breakdown into Activity heatmap section, mobile-first layout, show 10 series in Next Up (no abandoned). Movies tab: horizontal poster scrollers, new director tracking from TMDB, pie chart genre breakdown. TV Series tab: remove Episode Activity, poster scroller Next Up, Recently Finished/Abandoned side-by-side sorted by last watch date, pie chart genre. Games tab: In-Focus estimate (replaces backlog), per-game color-coded monthly play time, spider graph genres, pie chart status distribution, exclude dismissed games. Broken into subtasks 031-034.

---

## 2026-02-24 18:00 -- Task Completed: 027 - Dashboard All Tab Enhancements

**Type:** Task Completion
**Task:** 027 - Dashboard All Tab Enhancements
**Summary:** Enhanced Dashboard All tab with cross-media hero stats, adaptive weekly activity summary, GitHub-style 365-day activity heatmap with per-day tooltips, and cross-media monthly stacked bar chart — all backed by new backend queries across all three projections.
**Files changed:** 7 files

---

## 2026-02-24 17:50 -- Batch Started: [027]

**Type:** Batch Start
**Tasks:** 027 - Dashboard All Tab Enhancements
**Mode:** Sequential (last remaining task)

---

## 2026-02-24 17:45 -- Task Completed: 026 - Games Dashboard Stats & Visualizations

**Type:** Task Completion
**Task:** 026 - Games Dashboard Stats & Visualizations
**Summary:** Expanded Games dashboard tab with status distribution stacked bar, backlog time estimate hero card, ratings distribution, genre breakdown, monthly play time trend, HLTB comparison grouped bars, games completed per year, and 3 new stats badges. Platform/store breakdown skipped (no game_store table exists).
**Files changed:** 4 files

---

## 2026-02-24 17:35 -- Batch Started: [026]

**Type:** Batch Start
**Tasks:** 026 - Games Dashboard Stats & Visualizations
**Mode:** Sequential (027 depends on 026)

---

## 2026-02-24 17:30 -- Task Completed: 025 - TV Series Dashboard Stats & Visualizations

**Type:** Task Completion
**Task:** 025 - TV Series Dashboard Stats & Visualizations
**Summary:** Expanded TV Series dashboard tab with new stat badges (Currently Watching, Average Rating, Completion Rate), per-series progress bars and time remaining on Next Up, 14-day episode activity chart with binge detection, monthly episode activity, ratings distribution, genre breakdown, and most watched with friends. Upcoming Episodes deferred (requires TMDB air date infrastructure).
**Files changed:** 4 files

---

## 2026-02-24 17:20 -- Batch Started: [025]

**Type:** Batch Start
**Tasks:** 025 - TV Series Dashboard Stats & Visualizations
**Mode:** Sequential (conflicts with 026 on Shared.fs, Api.fs, Dashboard/Views.fs)

---

## 2026-02-24 17:15 -- Task Completed: 029 - List Page Fuzzy Search

**Type:** Task Completion
**Task:** 029 - List Page Fuzzy Search
**Summary:** Replaced naive `.Contains()` substring search on Movies, Series, and Games list pages with fuzzy matching using `FuzzyMatch.fuzzyFilter` and `FuzzyMatch.extractYear`, providing typo tolerance and year filtering while preserving original sort order.
**Files changed:** 4 files

---

## 2026-02-24 17:10 -- Task Completed: 028 - Search Hover Preview UX Fix

**Type:** Task Completion
**Task:** 028 - Search Hover Preview UX Fix
**Summary:** Removed keyboard-triggered hover preview and changed preview popover from flex sibling to fixed-position cursor-following overlay with viewport edge detection, preventing modal shrinkage and layout shifts.
**Files changed:** 1 file

---

## 2026-02-24 17:08 -- Task Completed: 024 - Movies Dashboard Stats & Visualizations

**Type:** Task Completion
**Task:** 024 - Movies Dashboard Stats & Visualizations
**Summary:** Expanded Movies dashboard tab with 9 new sections: ratings distribution bar chart, genre breakdown horizontal bars, monthly watch activity, most watched actors/directors, most watched with friends, country distribution, and recently watched — all backed by new server-side queries with graceful empty states.
**Files changed:** 5 files

---

## 2026-02-24 17:00 -- Batch Started: [024, 028, 029]

**Type:** Batch Start
**Tasks:** 024 - Movies Dashboard Stats & Visualizations, 028 - Search Hover Preview UX Fix, 029 - List Page Fuzzy Search
**Mode:** Parallel (batch of 3)

---

## 2026-02-24 16:00 -- Idea Captured: Unify List Page Search with Fuzzy Matching

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/029-list-page-fuzzy-search.md
**Summary:** Replace naive `.Contains()` substring search on Movies, Series, and Games list pages with the FuzzyMatch algorithm from task 021. Adds typo tolerance and year filtering. Filter-only mode — preserves existing sort order.

---

## 2026-02-24 15:30 -- Idea Captured: Search Hover Preview UX Fix

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/028-search-hover-preview-ux-fix.md
**Summary:** Fix two UX issues from task 022: (1) keyboard arrow navigation should not trigger the hover preview — mouse-only, (2) preview popover should float as a fixed overlay following the cursor instead of being a flex sibling that shrinks the modal.

---

## 2026-02-24 15:00 -- Idea Captured: Dashboard Stats & Visualizations (4 tasks)

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/024-movies-dashboard-stats.md, tasks/todo/025-series-dashboard-stats.md, tasks/todo/026-games-dashboard-stats.md, tasks/todo/027-dashboard-all-tab-enhancements.md
**Summary:** Based on research across 18+ media tracking apps, created 4 tasks to expand all dashboard tabs with charts, stats, and visualizations. Movies: ratings chart, genre bars, actor/director stats, world map. TV Series: progress bars, episode activity chart, upcoming air dates, binge detection. Games: status donut, backlog time estimate, HLTB comparison, platform breakdown. All tab: cross-media hero stats, activity heatmap, monthly stacked bars.

---

## 2026-02-24 14:30 -- Research: Dashboard Content for Movies, TV Series, and Games

**Type:** Research
**Topic:** What should movie, TV series, and games dashboards show? Industry standards across 18+ media tracking apps.
**File:** research/dashboard-content-research.md
**Key findings:**
- Ratings distribution charts, genre breakdowns, and activity heatmaps are the highest-impact additions — present in every major competitor (Letterboxd, Trakt, Backloggd)
- Time-based analytics (monthly trends, day-of-week patterns) are consistently the most engaging visualizations and most requested by users
- Mediatheca's cross-media unified tracking is its unique differentiator — the #1 user request across all community discussions
- Quick wins: ratings bar chart, genre horizontal bars, per-series progress bars, game status donut chart, backlog time estimate
- Year-in-Review / Wrapped summaries are the single biggest engagement feature across all platforms (Letterboxd, Trakt, Steam, PlayStation, Xbox)

---

## 2026-02-24 -- Idea Captured: Show Finished Series in Next Up for 7 Days

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/023-finished-series-7day-nextup.md
**Summary:** Finished TV series (not InFocus, not abandoned) should stay in dashboard Next Up for 7 days after last watch date. Single SQL WHERE clause change in `getDashboardSeriesNextUp` — no client, shared type, or API changes needed. The `last_watched_date` subselect and `IsFinished` flag already exist in the query and DTO.

---

## 2026-02-24 -- Task Completed: 023 - Finished Series 7-Day Next Up

**Type:** Task Completion
**Task:** 023 - Show Finished Series in Next Up for 7 Days
**Summary:** Extended `getDashboardSeriesNextUp` SQL WHERE clause in SeriesProjection.fs to include finished (non-abandoned) series whose last watched date is within 7 days. All 233 tests pass.
**Files changed:** 1 file

---

## 2026-02-24 -- Task Completed: 022 - Search Poster Grid with Hover Preview

**Type:** Task Completion
**Task:** 022 - Search Poster Grid with Hover Preview
**Summary:** Redesigned Ctrl+K search modal from text lists to a 4-column poster grid with 500ms hover preview popovers. Added 3 new API endpoints (previewTmdbMovie, previewTmdbSeries, previewRawgGame) with server-side caching, glassmorphic preview popover as sibling element, grid keyboard navigation, and in-memory client-side preview cache. All 233 tests pass.
**Files changed:** 6 files

---

## 2026-02-24 -- Batch Started: [022, 023]

**Type:** Batch Start
**Tasks:** 022 - Search Poster Grid with Hover Preview, 023 - Finished Series 7-Day Next Up
**Mode:** Parallel (batch of 2, no file conflicts)

---

## 2026-02-24 -- Task Completed: 021 - Fuzzy Search

**Type:** Task Completion
**Task:** 021 - Fuzzy Search
**Summary:** Implemented fuzzy search with Levenshtein distance-based matching for local library search in the Ctrl+K modal, plus year extraction for TMDB/RAWG external API queries. New FuzzyMatch.fs client module, updated API contracts to accept optional year parameter. All 233 tests pass.
**Files changed:** 8 files

---

## 2026-02-24 -- Batch Started: [021]

**Type:** Batch Start
**Tasks:** 021 - Fuzzy Search
**Mode:** Sequential (conflicts with 022 on SearchModal.fs, Shared.fs, Tmdb.fs, Rawg.fs, Api.fs, State.fs)

---

## 2026-02-24 -- Task Completed: 020 - Game "Dismissed" Status

**Type:** Task Completion
**Task:** 020 - Game "Dismissed" Status
**Summary:** Added `Dismissed` status to `GameStatus` DU across all layers (shared types, server domain, projection, event formatting, both client views) with badge-neutral styling and default-hidden filter behavior. All 233 tests pass.
**Files changed:** 7 files

---

## 2026-02-24 -- Batch Started: [020]

**Type:** Batch Start
**Tasks:** 020 - Game "Dismissed" Status
**Mode:** Sequential (conflicts with 021, 022 on Shared.fs)

---

## 2026-02-24 -- Idea Captured: Search Poster Grid with Hover Preview

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/022-search-poster-grid-hover-preview.md
**Summary:** Redesign Ctrl+K search modal from text lists to a 4-column poster grid. Hovering a poster for 500ms shows a glassmorphic preview popover with rich details: library items fetch full detail from local DB (overview, cast, description); Movies/Series tab fetches from TMDB API (`/3/movie/{id}?append_to_response=credits`); Games tab fetches from RAWG (`/api/games/{id}` + screenshots). All hover fetches are read-only -- nothing written to DB. New shared preview types, 3 new API endpoints, in-memory preview cache, grid keyboard navigation (←→↑↓).

---

## 2026-02-24 -- Idea Captured: Fuzzy Search

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/021-fuzzy-search.md
**Summary:** Make Ctrl+K search typo-tolerant using Levenshtein distance for local library matching, year extraction from queries (e.g., "inception 2010") passed to TMDB's `year` and RAWG's `dates` parameters. RAWG already has native fuzzy search. TMDB has no fuzziness but benefits from year filtering. New FuzzyMatch.fs client module, updated API contract to accept optional year parameter.

---

## 2026-02-24 -- Idea Captured: Game "Dismissed" Status

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/020-game-dismissed-status.md
**Summary:** Add `Dismissed` status to GameStatus DU for games the user isn't interested in. Uses `badge-neutral` (solid grey) pill. Dismissed games hidden from default game list — only visible when the "Dismissed" filter is explicitly selected. Small task touching 7 files: shared types, server encode/decode, projection, event formatting, both client views, and tests.

---

## 2026-02-24 -- Task Completed: 019 - Fix HLTB Auth Token Endpoint

**Type:** Task Completion
**Task:** 019 - Fix HLTB Auth Token Endpoint
**Summary:** Fixed HLTB auth token endpoint by making `fetchAuthToken` accept the discovered search endpoint and deriving the token URL as `{searchEndpoint}/init?t=...` instead of hardcoded `/api/search/init?t=...`. All 232 tests pass.
**Files changed:** 1 file

---

## 2026-02-24 -- Task Started: 019 - Fix HLTB Auth Token Endpoint

**Type:** Task Start
**Task:** 019 - Fix HLTB Auth Token Endpoint
**Milestone:** M4 - HowLongToBeat Integration

---

## 2026-02-24 -- Idea Captured: Fix HLTB Auth Token Endpoint

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/019-fix-hltb-auth-token.md
**Summary:** HLTB integration broken for all games — auth token endpoint hardcoded to `/api/search/init` which now returns 404. Root cause: token URL must be relative to the discovered search endpoint (e.g., `/api/finder/init`). Fix is small — change `fetchAuthToken` to accept the search endpoint and derive the token URL from it. Identified by comparing with Python `howlongtobeatpy` library.

---

## 2026-02-24 -- Task Completed: 018 - Game Trailer Playback

**Type:** Task Completion
**Task:** 018 - Game Trailer Playback
**Summary:** Implemented game trailer playback with Steam Store API (primary) and RAWG API (fallback) trailer fetching, new `getGameTrailer` API endpoint, and "Play Trailer" button with HTML5 video modal overlay on the game detail page. All 232 tests pass, `npm run build` succeeds.
**Files changed:** 8 files

---

## 2026-02-24 -- Task Started: 018 - Game Trailer Playback

**Type:** Task Start
**Task:** 018 - Game Trailer Playback
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Game Trailer Playback

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/018-game-trailers.md
**Summary:** Add "Play Trailer" to game detail pages using Steam Store API (primary, direct MP4/WebM URLs) with RAWG API fallback. HTML5 `<video>` modal overlay matching movie trailer UX. Includes new shared `GameTrailerInfo` type, `getGameTrailer` API endpoint, and full Elmish state management.

---

## 2026-02-24 -- Task Completed: 017 - Jellyfin Play Button

**Type:** Task Completion
**Task:** 017 - Jellyfin Play Button
**Summary:** Added Jellyfin play buttons throughout the app -- backend persists Jellyfin item IDs (movie, series, episode-level) during scan/import into new DB columns and a mapping table; frontend shows glassmorphism play buttons on dashboard hero spotlight, series poster cards, movie in-focus poster cards, and movie detail page, all opening the Jellyfin web UI in a new tab. All 232 tests pass.
**Files changed:** 9 files

---

## 2026-02-24 -- Task Started: 017 - Jellyfin Play Button

**Type:** Task Start
**Task:** 017 - Jellyfin Play Button
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Jellyfin Play Button

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/017-jellyfin-play-button.md
**Summary:** Add "Play in Jellyfin" buttons linking to Jellyfin's web UI for direct playback. Requires persisting Jellyfin item IDs (movies, series, episodes) during library scan. Play buttons appear on: dashboard hero (next-up episode), dashboard series poster cards (next episode), and movie detail pages. Items without a Jellyfin match show no button. Embedded HLS player deferred as future enhancement.

---

## 2026-02-24 -- Task Completed: 016 - Dashboard "All" Tab Overhaul V2

**Type:** Task Completion
**Task:** 016 - Dashboard "All" Tab Overhaul V2
**Summary:** Implemented Dashboard "All" tab overhaul V2 with hero episode spotlight (episode still preferred, series backdrop fallback), open-section Next Up without card chrome, poster-style Games & Focus cards, Recently Played summary stats (total hours + sessions), New Games card with family owner badges, and live Steam achievements card with 5-minute TTL cache and error state handling. All 16 acceptance criteria met.
**Files changed:** 9 files

---

## 2026-02-24 -- Task Started: 016 - Dashboard "All" Tab Overhaul V2

**Type:** Task Start
**Task:** 016 - Dashboard "All" Tab Overhaul V2
**Milestone:** --

---

## 2026-02-24 -- Idea Captured: Dashboard "All" Tab Overhaul V2

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/016-dashboard-overhaul-v2.md
**Summary:** Major dashboard redesign: hero episode spotlight with cinematic backdrop and episode description at top-left, card-less Next Up section, poster-style Games & Focus cards, "New Games" card with family ownership badges, Recently Played stats (total hours + sessions), and live Steam achievements card with error handling. Extends 2-column layout from task 015.

---

## 2026-02-20 -- Task Completed: 015 - Dashboard "All" Tab Visual Overhaul

**Type:** Task Completion
**Task:** 015 - Dashboard "All" Tab Visual Overhaul
**Summary:** Redesigned Dashboard "All" tab with 2-column grid layout (2/3 + 1/3), TV Series poster card horizontal scroller with shine/shadow effects and In Focus badges, pure CSS stacked bar chart for game play sessions (last 14 days, 8-color palette, clickable legend), Games In Focus below the chart, and full-width Movies In Focus poster scroller. New DashboardPlaySession shared type and server-side query.
**Files changed:** 6 files

---

## 2026-02-20 -- Batch Started: [015]

**Type:** Batch Start
**Tasks:** 015 - Dashboard "All" Tab Visual Overhaul
**Mode:** Sequential (after 014 completed)

---

## 2026-02-20 -- Task Completed: 014 - Event History Viewer on Detail Pages

**Type:** Task Completion
**Task:** 014 - Event History Viewer on Detail Pages
**Summary:** Implemented event history viewer across all 5 entity detail pages with a new backend API for human-readable event formatting, two shared frontend components (ActionMenu with glassmorphism dropdown, EventHistoryModal with date-grouped timeline and category icons), and per-page integration that replaces standalone delete buttons with hover-reveal action menus. 232 tests passing.
**Files changed:** 24 files

---

## 2026-02-20 -- Batch Started: [014]

**Type:** Batch Start
**Tasks:** 014 - Event History Viewer on Detail Pages
**Mode:** Sequential (conflicts with 015 on Shared.fs and Api.fs)

---

## 2026-02-20 -- Idea Captured: Dashboard Visual Overhaul

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/015-dashboard-visual-overhaul.md
**Summary:** Redesign the dashboard "All" tab from stacked lists to a spatially varied 2-column layout. TV Series Next Up becomes a Netflix-style horizontal poster scroller (top-left, 2/3 width). Games Recently Played gets a stacked bar chart of the last 14 days (top-right, 1/3 width) with Games In Focus below it. Movies In Focus spans the full bottom row. New API endpoint for cross-game daily play sessions. Pure CSS bar chart, no charting library.

---

## 2026-02-20 -- Idea Captured: Event History Viewer

**Type:** Idea Capture
**Mode:** Deep
**Filed to:** tasks/todo/014-event-history-viewer.md
**Summary:** Event history viewer on every detail page (Movies, Series, Games, Friends, Catalogues). Hover-reveal action menu replaces standalone Remove/Delete buttons. "Event Log" opens a glassmorphism modal with a polished timeline of human-readable events grouped by date with icons. ContentBlocks streams merged into entity timelines. New shared `ActionMenu` and `EventHistoryModal` components.

---

## 2026-02-19 — Task Completed: 010 - Dashboard Games tab

**Type:** Task Completion
**Task:** 010 - Dashboard Games tab
**Summary:** Implemented Games tab with stats row (total games, play time, completed, in progress), recently added games section, and recently played section with HLTB comparison display.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 009 - Dashboard Series tab

**Type:** Task Completion
**Task:** 009 - Dashboard Series tab
**Summary:** Implemented TV Series tab with stats row (series, episodes, watch time), full next-up list (In Focus sorted first), recently finished section (green badges), and recently abandoned section (red badges). Refactored statBadge for reuse.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 008 - Dashboard Movies tab

**Type:** Task Completion
**Task:** 008 - Dashboard Movies tab
**Summary:** Implemented Movies tab with stats row (movies, sessions, watch time) and recently added unwatched movies as compact clickable rows with poster thumbnails.
**Files changed:** 1 file (Dashboard/Views.fs)

---

## 2026-02-19 — Batch Started: [008, 009, 010]

**Type:** Batch Start
**Tasks:** 008 - Dashboard Movies tab, 009 - Dashboard Series tab, 010 - Dashboard Games tab
**Mode:** Sequential (same file conflicts — Dashboard/Views.fs)

---

## 2026-02-19 — Task Completed: 007 - Dashboard client — tab structure + All tab

**Type:** Task Completion
**Task:** 007 - Dashboard client — tab structure + All tab
**Summary:** Complete dashboard rewrite. Replaced old hero/stats/activity layout with tabbed dashboard (All/Movies/Series/Games). All tab shows four glass card sections: TV Series Next Up (with In Focus pinning, friend pills, finished/abandoned badges), Movies In Focus, Games In Focus, Games Recently Played (with HLTB progress). Placeholder tabs for individual media types. Updated root State.fs for new init/navigation.
**Files changed:** 4 files (Dashboard/Types.fs, Dashboard/State.fs, Dashboard/Views.fs, root State.fs)

---

## 2026-02-19 — Batch Started: [007]

**Type:** Batch Start
**Tasks:** 007 - Dashboard client — tab structure + All tab
**Mode:** Sequential (single task, blocks 008-010)

---

## 2026-02-19 — Task Completed: 013 - HowLongToBeat display

**Type:** Task Completion
**Task:** 013 - HowLongToBeat display
**Summary:** Added fetchHltbData API endpoint, HLTB section on game detail page with progress bar comparison (play time vs HLTB average), "Fetch from HowLongToBeat" button for games without data, graceful "no data" state.
**Files changed:** 5 files (Shared.fs, Api.fs, GameDetail/Types.fs, GameDetail/State.fs, GameDetail/Views.fs)

---

## 2026-02-19 — Task Completed: 006 - Dashboard API

**Type:** Task Completion
**Task:** 006 - Dashboard API
**Summary:** Added 11 shared types and 4 API endpoints for unified dashboard tabs (All/Movies/Series/Games). Implemented query functions across MovieProjection, SeriesProjection, and GameProjection for next-up, in-focus, recently played, recently added, and stats data.
**Files changed:** 5 files (Shared.fs, Api.fs, MovieProjection.fs, SeriesProjection.fs, GameProjection.fs)

---

## 2026-02-19 — Batch Started: [006, 013]

**Type:** Batch Start
**Tasks:** 006 - Dashboard API, 013 - HowLongToBeat display
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Task Completed: 011 - Steam description backfill

**Type:** Task Completion
**Task:** 011 - Steam description backfill
**Summary:** Added description backfill phase to Steam library import. New Game_description_set event. After main import loop, queries games with empty descriptions + steam_app_id, fetches from Steam Store API with 300ms rate limiting, sets description/short_description/website/play_modes.
**Files changed:** 3 files (Games.fs, GameProjection.fs, Api.fs)

---

## 2026-02-19 — Task Completed: 005 - Game InFocus status

**Type:** Task Completion
**Task:** 005 - Game InFocus status
**Summary:** Added InFocus to GameStatus DU (Backlog → InFocus → Playing → ...) in Shared, Server, and Client. Updated filter badges, status selectors, serialization. 3 new tests, 232 total passing.
**Files changed:** 7 files (Shared.fs, Games.fs, GameProjection.fs, Games/Views.fs, GameDetail/Views.fs, GamesTests.fs)

---

## 2026-02-19 — Task Completed: 004 - Series In Focus UI

**Type:** Task Completion
**Task:** 004 - Series In Focus UI
**Summary:** Added In Focus toggle button (crosshair icon) to series detail hero section and circular badge overlay on series list poster cards. Mirrors Movie In Focus UI exactly.
**Files changed:** 4 files (SeriesDetail/Types.fs, SeriesDetail/State.fs, SeriesDetail/Views.fs, Series/Views.fs)

---

## 2026-02-19 — Batch Started: [004, 005, 011]

**Type:** Batch Start
**Tasks:** 004 - Series In Focus UI, 005 - Game InFocus status, 011 - Steam description backfill
**Mode:** Parallel (batch of 3)

---

## 2026-02-19 — Task Completed: 003 - Series In Focus backend

**Type:** Task Completion
**Task:** 003 - Series In Focus backend
**Summary:** Added Series_in_focus_set/cleared events, InFocus flag on ActiveSeries, auto-clear on episode/season/episodes-up-to watched, projection columns, shared DTOs, API endpoint, and 11 new tests. 229 tests passing.
**Files changed:** 5 files (Series.fs, SeriesProjection.fs, Shared.fs, Api.fs, SeriesTests.fs)

---

## 2026-02-19 — Task Completed: 002 - Movie In Focus UI

**Type:** Task Completion
**Task:** 002 - Movie In Focus UI
**Summary:** Added In Focus toggle button (crosshair icon) to movie detail hero section and circular badge overlay on movie list poster cards. New crosshair icons (filled, outline, small) in Icons module.
**Files changed:** 5 files (Icons.fs, MovieDetail/Types.fs, MovieDetail/State.fs, MovieDetail/Views.fs, Movies/Views.fs)

---

## 2026-02-19 — Batch Started: [002, 003]

**Type:** Batch Start
**Tasks:** 002 - Movie In Focus UI, 003 - Series In Focus backend
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Task Completed: 012 - HowLongToBeat API client

**Type:** Task Completion
**Task:** 012 - HowLongToBeat API client
**Summary:** Created HowLongToBeat.fs module with searchGame function. Implements 3-step API flow (endpoint discovery from Next.js bundles, auth token fetch, search POST). Includes caching, Jaccard similarity matching, graceful degradation, and 403 retry logic.
**Files changed:** 2 files (HowLongToBeat.fs new, Server.fsproj modified)

---

## 2026-02-19 — Task Completed: 001 - Movie In Focus backend

**Type:** Task Completion
**Task:** 001 - Movie In Focus backend
**Summary:** Added Movie_in_focus_set/cleared events, InFocus flag on ActiveMovie state, auto-clear on watch session recording, projection columns, shared DTO fields, API endpoint, and 9 new tests. 218 tests passing.
**Files changed:** 5 files (Movies.fs, MovieProjection.fs, Shared.fs, Api.fs, MoviesTests.fs)

---

## 2026-02-19 — Batch Started: [001, 012]

**Type:** Batch Start
**Tasks:** 001 - Movie In Focus backend, 012 - HowLongToBeat API client
**Mode:** Parallel (batch of 2)

---

## 2026-02-19 — Planning: v1 Finish Line — 4 Milestones, 13 Tasks

**Type:** Planning
**Summary:** Broke down the v1 finish line vision into 4 milestones and 13 concrete tasks. M1 (In Focus) covers the cross-cutting In Focus concept across Movies, Series, and Games. M2 (Unified Dashboard) reworks the landing page into a tabbed layout with All/Movies/Series/Games tabs. M3 (Steam Description Backfill) adds description enrichment during Steam import. M4 (HowLongToBeat) integrates HLTB completion times via reverse-engineered internal API. HLTB research confirmed no official API — requires dynamic endpoint discovery, auth tokens, and graceful degradation.
**Milestones created/updated:** M1 (In Focus), M2 (Unified Dashboard), M3 (Steam Description Backfill), M4 (HowLongToBeat Integration)
**Tasks created:** 001-movie-in-focus-backend, 002-movie-in-focus-ui, 003-series-in-focus-backend, 004-series-in-focus-ui, 005-game-in-focus-status, 006-dashboard-api, 007-dashboard-all-tab, 008-dashboard-movies-tab, 009-dashboard-series-tab, 010-dashboard-games-tab, 011-steam-description-backfill, 012-hltb-api-client, 013-hltb-display
**Tasks moved to backlog:** None (all in todo)
**Ideas incorporated:** None

---

## 2026-02-19 — Brainstorm: v1 Finish Line — Unified Dashboard + In Focus

**Type:** Brainstorm
**Summary:** Defined the v1 finish line around a unified tabbed dashboard (All/Movies/TV Series/Games) and the cross-cutting "In Focus" concept. In Focus is a toggle flag for Movies and TV Series (auto-clearing on consumption) and a lifecycle status for Games (Backlog → InFocus → Playing → ...). Dashboard All tab shows intent-driven sections: TV Series Next Up (In Focus pinned to top, then by recency), Movies In Focus, Games In Focus, Games Recently Played. REQ-207 replaced with unified dashboard, REQ-208 narrowed to description backfill on Steam import, REQ-209 (HowLongToBeat) unchanged.
**Vision updated:** Yes
**Key decisions:**
- In Focus is a toggle flag for Movies (auto-clears on watch session) and TV Series (auto-clears on first episode watched)
- In Focus is a status in the Game lifecycle between Backlog and Playing
- TV Series dashboard sorting: In Focus pinned to top, then by most recent watch activity
- No separate Games Dashboard — unified tabbed dashboard replaces REQ-207
- REQ-208 narrowed: Steam import already works, just add description backfill for existing games
- Individual dashboard tabs (Movies/Series/Games) will grow over time with stats and intelligence

---
