# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---
## 2026-09-06 12:10 -- Modeling / Captured: design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** Builder asked for the Dashboard scrollbars under episodes and movies to be invisible. Investigation found the cause is dead code, not missing styling: all six horizontal poster rails in `Dashboard/Views.fs` carry `scrollbar-thin/-thumb/-track` utilities from the `tailwind-scrollbar` plugin, which is absent from both `package.json` and `index.css`'s `@plugin` directives — so Tailwind 4 emits nothing and the native scrollbar renders. Routed to design-system (scrollbar chrome is cross-cutting visual language) as a `.scrollbar-hidden` / `DesignSystem.scrollbarHidden` primitive covering Chromium and WebKitGTK, adopted at all six rails, with no new plugin dependency. Filed straight to todo — concrete and unambiguous. No orchestrator round, no ADR, no prior art (nothing in any BC has touched scrollbars before).

---

## 2026-09-06 12:18 -- Batch started: [design-system-hs4vm]

**Type:** Work / Batch start
**Tasks:** design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail
**Parallel:** no (1 worker — design-system-hs4vm is the only ready task across every BC after the intelligence-c3vqm re-scan; it was captured to todo by a concurrent modeling session mid-run)

---

## 2026-09-06 12:17 -- Task verified and completed: intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair

**Type:** Work / Task completion
**Task:** intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair
**Summary:** Capped All-tab game poster tiles to a 130px auto-fill grid track (matching the movie filmstrip's fixed 196px height via the existing 2/3 aspect ratio) and dropped the redundant unconditional crosshair badge from game poster cards.
**Duration:** 12m
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 12:10 -- Modeling / Captured: intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Two builder complaints about the All tab's Games section, captured as one task after verifying both in code. (1) Game posters are a `grid-cols-2 sm:grid-cols-3` of `1fr` tracks over an `aspect-ratio: 2/3` container, so they scale with the section width and swing hard between `sm` and `xl` (the Games/Books split stays single-column until `xl`) — where movie posters are pinned to the filmstrip's fixed `h-[196px]`. Fix is a hard 130×196 cap. (2) `gameInFocusPosterCard` renders the crosshair badge unconditionally — for games `InFocus` *is* the status and the section lists only InFocus games, so it distinguishes nothing; same reasoning `intelligence-f6cfv` applied to the Next-episode card. The conditional badge on `movieToWatchPosterCard` stays. Filed straight to todo — one file, code facts verified, no orchestrator round needed.

---
## 2026-09-06 11:50 -- Work session ended

**Type:** Work / Session end
**Duration:** 13m (first "Batch started" 11:37 → 11:50)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-p4t7k: 1
**Commits:** 3 (1 batch start, 1 task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. intelligence-p4t7k is a pure server-side deletion of a payload the builder already retired (via intelligence-wecjh); it removes per-load work from the Unified Dashboard's All-tab endpoint and touches nothing named under "Out of Scope (v1)". vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task, `type: refactor` in intelligence) — hand-classified.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Verifier evidence beyond the worker's claim:** re-ran `npm test` (685 passed) and `npm run build` (clean Fable compile) from the worktree, confirmed `resolveFriendRefs` survives (GameProjection.fs:527, four call sites in the game-detail projection at 763–766), and confirmed the tree-wide grep for `NewGames|DashboardNewGame|getDashboardNewGames` is empty across `src/` and `tests/`.

**Harness notes:** (1) Worktree again needed the root `node_modules` junctioned from the main tree (`mklink /J`, removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries). (2) The `checkpoint` verb's opts JSON was written to a scratch file with forward-slash paths and read back, as in prior sessions. (3) The `work` SKILL.md references `skills/work/references/worker-return-format.md`; in plugin 0.9.2 the file actually lives at the plugin root `references/worker-return-format.md`. (4) Board is empty after this session — no todo or backlog work remains in any BC's `todo/`.

---
## 2026-09-06 11:05 -- Modeling / Refined: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** backlog
**Summary:** Code-fact refinement, no orchestrator round needed. Corrected the record name — the dead field lives on `DashboardAllTab` (the All-tab payload), not `DashboardGameStats` (the Games-tab stats block, untouched). Verified `DashboardNewGame` has only two references (definition + mapper) so it is deleted too; `resolveFriendRefs` stays (still used by the game-detail projection); no test references `DashboardAllTab`; no client code reads `NewGames`. Added the README "Retired" note correction to scope and a grep-clean criterion. No split, no ADR.
**Split into:** none
**ADRs written:** none

---

## 2026-09-06 12:05 -- Batch started: [intelligence-c3vqm]

**Type:** Work / Batch start
**Tasks:** intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair
**Parallel:** no (1 worker — intelligence-c3vqm is the only ready task across every BC, nothing held back)

---

## 2026-09-06 11:49 -- Task verified and completed: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Work / Task completion
**Task:** intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it
**Summary:** Pruned the dead New Games dashboard payload end to end — removed Shared.DashboardNewGame and DashboardAllTab.NewGames, deleted GameProjection.getDashboardNewGames, removed its call and assignment in Api.fs getDashboardAllTab, and corrected the intelligence README Retired note to name DashboardAllTab and state the prune is complete.
**Duration:** 10m
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 11:38 -- Batch started: [intelligence-p4t7k]

**Type:** Work / Batch start
**Tasks:** intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it
**Parallel:** no (1 worker — intelligence-p4t7k is the only ready task across every BC, nothing held back)

---

## 2026-09-06 11:28 -- Modeling / Promoted: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Modeling / Promote
**BC:** intelligence
**From → To:** backlog → todo

---

## 2026-09-06 10:37 -- Work session ended

**Type:** Work / Session end
**Duration:** 49m (first "Batch started" 09:48 → 10:37)
**Completed:** 3 (first-try PASS: 3, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-encn4: 1, intelligence-wecjh: 1, intelligence-f6cfv: 1
**Commits:** 7 (3 batch starts, 3 task completions, this entry) — plus one concurrent `modeling` capture commit (a6ba85a, intelligence-f6cfv) that landed on `main` mid-run and was picked up as a new ready task after intelligence-wecjh completed
**Vision-conformance:** none — batch aligns with vision. All three tasks sit inside the Unified Dashboard workstream (All-tab section chrome, Dashboard/Views.fs code health, Next episode card). The All-tab semantics the vision names survive: the Games section still shows only In Focus games (heading shortened, content unchanged), the Next episode row still shows watch-with friends (as avatars instead of name pills), and the In focus badge drop removes a duplicate signal, not the In Focus sort. The Books placeholder chrome change is a visual stub already introduced by intelligence-dq8rk, not Books (v2) scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (3 tasks, all `type: refactor` in intelligence) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Builder decision recorded mid-run:** intelligence-wecjh's open question (re-wire `newGamesSection` on the Games tab vs. confirm the drop) was put to the builder before dispatch; answer: **confirm the drop**. Both helpers are deleted, the retirement is recorded in the intelligence README, and the worker filed `intelligence-p4t7k` (backlog) for the server-side `NewGames` payload prune.

**Browser smoke check (conductor, Chrome DevTools MCP, served from `main` via `dotnet src/Server/bin/Debug/net9.0/Server.dll` run from the repo root):** after intelligence-wecjh, all four dashboard tabs (All / Movies / TV Series / Games) rendered with their expected sections and empty states and **zero console errors or warnings**; after intelligence-f6cfv, the rebuilt bundle loaded with zero console errors and the All tab's Books column renders as `.section-open` with no `.velvet-card` in `main` (intelligence-encn4 confirmed live). **Caveat:** the local dev database (`~/app/mediatheca/mediatheca.db`) is empty — `getMovies`/`getSeries`/`getGames` all return `[]` — so populated render paths (poster grids, Next episode cards, the new avatar stack) were not exercised in the browser. wecjh's identical-render criterion rests on the verifier's pure-deletion proof (`--diff-algorithm=patience` → 0 added lines, all 54 surviving definitions byte-identical); f6cfv's avatar look is unverified in a browser and worth a glance on the deployed build.

**Verifier observation, not acted on (scope):** `friend.Name.Substring(0, 1)` in `DesignSystem.nextEpisodeHeroCard` throws on an empty friend name; `Friend_added` has no server-side non-empty-name guard. Not reachable via the normal add-friend flow — a candidate for a small `modeling` capture (guard the initial fallback, or validate the name at the aggregate).

**Harness notes:** (1) `dotnet run --project src/Server/Server.fsproj` sets CWD to `src/Server`, and `Composition.fs` resolves static files from `CWD/deploy/public` — the SPA 404s unless the server is launched from the repo root. (2) Each worktree again needed the root `node_modules` junctioned from the main tree (`mklink /J`, removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries, three times). (3) The `checkpoint` verb's opts JSON was written to a scratch file with forward-slash paths and read back, as last session. (4) Myers diff of a ~2000-line deletion showed 166 spurious `+` lines; `--diff-algorithm=patience` gave the true pure-deletion view and was handed to the verifier. (5) Board is empty after this session — `intelligence-p4t7k` sits in backlog, unrefined.

---


## 2026-09-06 10:34 -- Task verified and completed: intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped

**Type:** Work / Task completion
**Task:** intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped
**Summary:** Reworked the Dashboard Next episode hero card so watched-with friends render as an overlapping circular avatar stack pinned top-left (image or initial fallback, click-through to the friend page preserved), and dropped the In-focus badge from the card and its props; ring-white/30 recorded as a card-local adaptation in the intelligence README.
**Duration:** 17m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 10:21 -- Batch started: [intelligence-f6cfv]

**Type:** Work / Batch start
**Tasks:** intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped
**Parallel:** no (1 worker — intelligence-f6cfv was promoted mid-run by a concurrent modeling session and became ready when intelligence-wecjh landed; it is the only ready task across every BC, nothing held back)

---

## 2026-09-06 10:20 -- Task verified and completed: intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild

**Type:** Work / Task completion
**Task:** intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild
**Summary:** Deleted all 43 unreachable view helpers (including newGamesSection/newGameItem, confirmed retired by the builder) from Dashboard/Views.fs, shrinking it from 4418 to 2427 lines with a zero-unreferenced-definitions scan, and recorded the New Games retirement in the intelligence BC README; follow-up intelligence-p4t7k captures the server-side NewGames payload prune.
**Duration:** 21m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 10:12 -- Modeling / Captured: intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** The hero card built by intelligence-h7v2q renders watched-with friends as name pills at the *bottom* of the scrim, last in a four-item stack — the people you watched with end up the least prominent thing on the card while the tall overlay eats the backdrop. They move to the card's top-left as avatar-only circles (~40px, matching the Jellyfin play button opposite them), overlapping stack with a ring separator per the styleguide `heroCard` pattern, image where available and the uppercased first letter otherwise; the name drops out of the visual and survives as `title`/`alt`, and the friend-link click semantics (preventDefault + stopPropagation, no card click-through) are preserved verbatim. Builder decided in the modeling session that the **In focus badge is no longer needed on this card** — In Focus already earns those series their position through sorting — so the badge block and the `InFocus` prop are deleted outright, which is what frees the corner; `statusBadge` itself stays for the styleguide `heroCard`. Concrete enough for todo directly, orchestrator skipped. Sequenced behind `intelligence-wecjh` (in flight, deletes ~2000 line-referenced lines of the same `Dashboard/Views.fs` this task edits); Series-tab list rows and their page-local `friendPill` are explicitly out of scope.

---

## 2026-09-06 09:57 -- Batch started: [intelligence-wecjh]

**Type:** Work / Batch start
**Tasks:** intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild
**Parallel:** no (1 worker — intelligence-wecjh became ready when intelligence-encn4 landed; it is the only remaining task across every BC, nothing held back)

---

## 2026-09-06 09:57 -- Task verified and completed: intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"

**Type:** Work / Task completion
**Task:** intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"
**Summary:** On the Dashboard All tab, the Games and Books columns now render as open sections (no card chrome) matching the Next episode / Movies to Watch rows above them, and the Games heading was shortened from "Games In Focus" to "Games"; the Games tab keeps its card chrome.
**Duration:** 8m20s
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 09:48 -- Batch started: [intelligence-encn4]

**Type:** Work / Batch start
**Tasks:** intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"
**Parallel:** no (1 worker — intelligence-encn4 is the only ready task across every BC; intelligence-wecjh is blocked behind it via depends_on and is picked up next wave)

---

## 2026-09-06 09:31 -- Modeling / Captured: intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Follow-up to the dead `gamesInFocusSection` spotted while scoping intelligence-encn4 — a transitive-closure scan of `Dashboard/Views.fs` found the single dead helper is one of **43 unreferenced top-level definitions, ~2000 of the file's 4419 lines (45%)**, residue from intelligence-dq8rk's 3a rebuild changing call sites without deleting definitions (F# does not warn on unused module-private lets, so `npm run build` never flagged it). Task carries the full verified inventory with line ranges and a bottom-up deletion order. Two findings surfaced while scoping and recorded rather than acted on: (1) `newGamesSection` is a **latent regression** — dq8rk explicitly kept New Games on the Games tab but `gamesTabView` never calls it, so the worker must ask the builder whether to re-wire or confirm the drop; (2) the server still projects and ships the unrendered `NewGames` payload (`Api.fs:2162`), left out of scope as a follow-up since it touches the `IMediathecaApi` contract. Sequenced behind intelligence-encn4, whose edits are line-referenced into the same file.

---


## 2026-09-06 09:12 -- Modeling / Captured: intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** The All tab's Games and Books columns still carry velvet-card chrome from the original 3a build while the TV and Movies rows above them are open sections, so the landing page reads as two flat rows followed by two boxes; both columns swap `sectionCard` for the existing `sectionOpen` helper and the Games heading loses its now-redundant "In Focus" qualifier (the All tab shows nothing but In Focus games). Two-line composition change in `Dashboard/Views.fs`, no CSS and no new helper — concrete enough for todo directly, orchestrator skipped. Noted in the task: `gamesInFocusSection` (the list-row variant) is dead code with no callers, deliberately left out of scope.

---

## 2026-09-06 00:28 -- Work session ended

**Type:** Work / Session end
**Duration:** 33m (first "Batch started" 23:54 → 00:28)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-v0xmv: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is an integration refactor that removes the Steam login/token-mint path and keeps the Steam Family import alive on a user-pasted browser token — it protects the account the whole Steam Import workstream depends on, touches no Out-of-Scope (v1) item and adds no admin-console scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: refactor` → product-facing.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed; the now-empty `spikes/` directory left by the squash-merge's file deletions is gone too.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-04 10:55 boundary — the single commit (85f103d, the integration-v0xmv capture) carries its task trailer. Nothing to re-align.

**Builder-side, outside the task (from integration-v0xmv Notes):** (1) at `store.steampowered.com/twofactor/manage` remove the "Mediatheca" / "… (SteamKit2)" authorized devices; (2) deploy this change **before** the next family import; (3) fetch a fresh `webapi_token` from your own logged-in browser (`store.steampowered.com/pointssummary/ajaxgetasyncconfig`) and paste it in Settings → Steam Family — never script that step; (4) do not press any Steam-related button on the old deployed build in the meantime. First startup of the new build deletes the stored `steam_family_refresh_token`.

**Harness notes:** (1) the `checkpoint` verb's opts JSON had to be written to a scratch file and read back — Windows backslash paths in an inline bash argument fail its JSON parse; forward-slash paths work. (2) The worktree again needed the root `node_modules` junctioned from the main tree for `npm run build` (removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries). (3) Board is empty after this task — no ready work in any BC.

---

## 2026-09-06 00:27 -- Task verified and completed: integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again

**Type:** Work / Task completion
**Task:** integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again
**Summary:** Removed the Steam Connect QR login and the refresh-token mint path end to end (SteamConnect.fs, SteamKit2/QRCoder deps, the spike scripts, the mint-and-retry seam) so the Steam Family import runs only on a browser-obtained access token pasted by the user in Settings; wrote ADR-0070, superseded ADR-0061, and amended ADR-0019/ADR-0067 in place.
**Duration:** 32m13s
**Verification:** PASS (iteration 1)
**Files changed:** 25
**Tests added:** 11
**ADRs written:** 0070 (new); 0019, 0061, 0067 amended in place

---

## 2026-09-05 23:54 -- Batch started: [integration-v0xmv]

**Type:** Work / Batch start
**Tasks:** integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again
**Parallel:** no (1 worker — integration-v0xmv is the only ready task across every BC; nothing held back)

---

## 2026-09-05 23:50 -- Modeling / Captured: integration-v0xmv - Remove the Steam Connect QR login and refresh-token mint path; Steam Family import runs only on a browser-obtained access token pasted in Settings

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Valve escalated from three hijack alerts to a permanent-ban threat for further misuse of the login API (2026-09-05). ADR-0067's amendment had already pinned the third alert to the SteamKit2 QR ceremony, so the task deletes `SteamConnect.fs`, the `/api/stream/steam-connect` route, the SteamKit2/QRCoder packages, the refresh-token mint path (`mintFamilyAccessToken`, `withTokenRefresh`, `*WithRefresh`), the stored `steam_family_refresh_token` and the spike scripts, and re-promotes the paste-a-token flow (with `ajaxgetasyncconfig` → `webapi_token` instructions) as the only way in. Compliance check of the remaining import shape against the Steam Web API Terms of Use recorded in the task: 2–3 read-only `IFamilyGroupsService` GETs on the user's own browser token, key-authenticated `GetOwnedGames`, throttled public `appdetails`; no login API call of any kind. Worker writes an ADR superseding 0061 and amending 0019/0067 (ladder retired; browser-retrieval fallback closed as will-not-build). Concrete enough for todo directly; orchestrator skipped — the removal seam was fully inventoried from source during capture.

---

## 2026-09-04 10:55 -- Work session ended

**Type:** Work / Session end
**Duration:** 19m (first "Batch started" 10:36 → 10:55)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-zwnh4: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is an integration bug fix (Steam Connect QR login sends a fixed device identity; ADR-0067 amended with the corrected home-IP hypothesis) — it serves the Steam Import workstream, touches no Out-of-Scope (v1) item and adds no admin-console scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)".
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `formatBatchMixLine` from the source-repo `lib/vacuum-guard.mjs` (installed plugin 0.9.2 carries no such module).
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-03 20:14 boundary — the single commit (02ad2a9, the integration-zwnh4 capture) carries its task trailer. Nothing to re-align.

**Verifier observation, not acted on (scope):** ADR-0019 points 2 and 4 still carry the now-retracted "MobileApp-from-datacenter-IP" framing and call browser retrieval "escalation-ladder step 2" (it is step 3 after the ADR-0067 amendment). ADR-0061 Consequences has the same datacenter wording. Neither was in integration-zwnh4 sync list — a candidate for a small follow-up capture via `modeling`.

**Builder-side, outside the task (from integration-zwnh4 Notes):** after Steam recovery, check `store.steampowered.com/twofactor/manage` for the `(SteamKit2)` devices and `steamcommunity.com/dev/apikey` for an unrecognised key; deploy this change before the next Connect Steam; do not click Connect Steam unless an import reports "reconnect required". A fourth alert after this is live for one ordinary usage cycle is the trigger to climb the amended ladder.

**Harness notes:** (1) cached plugin 0.9.2 `checkpoint` again omitted the vacated `doing/` path from its manifest — staged explicitly, git recorded the move as a rename. (2) The worktree needed the root `node_modules` junctioned from the main tree for `npm run build` (removed via `rmdir` before `git worktree remove`; main copy verified intact). (3) Board is empty after this task — no ready work in any BC.

---

## 2026-09-04 10:53 -- Task verified and completed: integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert

**Type:** Work / Task completion
**Task:** integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert
**Summary:** The Steam Connect QR login now sends a fixed, honest device identity (a "Mediatheca" device name, STEAM_DEVICE_NAME-overridable, and a Mobile website id matching the MobileApp platform) instead of SteamKit2 per-deploy container-hostname defaults, and ADR-0067 is amended to retract the disproven datacenter-IP hypothesis in favor of the device-fingerprint one after the third Valve alert.
**Duration:** 18m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 8
**ADRs written:** none

---

## 2026-09-04 10:36 -- Batch started: [integration-zwnh4]

**Type:** Work / Batch start
**Tasks:** integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert
**Parallel:** no (1 worker — integration-zwnh4 is the only ready task across every BC; nothing held back)

---

## 2026-09-04 10:35 -- Modeling / Captured: integration-zwnh4 - Steam Connect QR login gets a stable, honest device identity; ADR-0067 amended after the third Valve alert

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** The third Valve account alert (2026-09-03 18:54 CEST, Muenster DE) was traced in production container logs to the Connect Steam QR ceremony at 16:54:30 UTC on a container redeployed at 16:39 UTC — not to the import. The alert location proves the production host is residential, disproving ADR-0067's datacenter-IP hypothesis; the enumeration fixes (ADR-0066/0069) were already live. What SteamKit2 sends by default is the real fingerprint: DeviceFriendlyName = "{MachineName} (SteamKit2)" (the random container id per deploy), WebsiteID "Client" on a MobileApp/Android9 session. The task fixes SteamConnect.fs (fixed name, "Mobile" website id, fixed OS type, pure + tested), amends ADR-0067 in place (retract the IP half, discharge ladder step 1, insert this as the cheapest rung before browser retrieval, add the reconnect-loop note), and syncs the README/concept prose. Concrete enough for todo directly.

---

## 2026-09-03 20:14 -- Work session ended

**Type:** Work / Session end
**Duration:** 36m (first "Batch started" 19:38 → 20:14)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-t69rb: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is a Games detail-page UX change (persistent card column, Journal-first default tab) — media-experience work; it touches no Out-of-Scope (v1) item (no trailer playback added, no Books/Trakt/intelligence scope) and adds no admin-console scope.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: feature` → product-facing.
**Carry-over:** none — working tree clean, no registered worktrees remain. A stray unregistered `.worktrees/infrastructure-r8kqt/src/Server/obj/` residue (three dotnet build files locked during that session's teardown, 17:36) was found and deleted; `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 18:00 boundary — all six commits carry task trailers (infrastructure-r8kqt runbook records, administration-b3xqf rotation, games-t69rb capture). Nothing to re-align.

**Human-eye pending (games-t69rb):** "switching tabs does not visibly reflow or flash the card column" is unticked in the task file — builder check on a real game page. The Playwright spec `tests/e2e/game-detail-persistent-cards.spec.ts` was written but not executed by the verifier (build + Expecto 685/685 + Vitest 16/16 were).

**Harness notes:** (1) plugin 0.9.2's `checkpoint` manifest again did not fold in the vacated `doing/` path — staged explicitly, git recorded the move as a rename. (2) Worker's `FILES_CHANGED: 20` undercounted its own 22-path FILE_LIST (13 of them one-line `GameJournal.initialize` additions to server test fixtures, mechanical from the new DTO field). (3) Board is empty after this task — no ready work in any BC; vision.md has no "Open questions" section for the vacuum guard to surface.

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

