# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

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

