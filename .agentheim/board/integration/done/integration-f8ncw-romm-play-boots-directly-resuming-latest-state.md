---
id: integration-f8ncw
title: RomM Play boots the game directly — the game page's Play button starts EmulatorJS via RomM's console-mode route and resumes the rom's newest save state, instead of landing on RomM's pre-game setup screen
status: done
type: feature
context: integration
created: 2026-09-28
completed:
depends_on: [design-system-001]
blocks: []
tags: [romm, games, emulation, game-detail]
related_adrs: [0088]
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: [integration-q748k, integration-jkbm1]
---

## Why
integration-q748k's Play button opens `{base}/rom/{id}/ejs`. In RomM 5.3.1 that page
(`frontend/src/views/Player/EmulatorJS/Base.vue`) always shows a setup screen first: core,
save and state pickers, and a Play button (`v-if="!gameRunning"`, started only by
`@click="onPlay"`). It reads no query parameter or setting that skips this screen. So Play in
Mediatheca takes you to a second Play button. The builder wants one click to start the game,
and to continue where they left off.

## What
RomM's **console mode** has a route that boots right away:
`/console/rom/:rom/play` (`frontend/src/console/views/Play.vue`, parent route `/console`). Its
`onMounted` calls `boot()` directly, with no setup screen. It reads optional `?save=<id>` and
`?state=<id>` query params and loads that save or state when the game starts. It does **not**
pick the latest save or state by itself. When you exit, it records a RomM play session
(`playSessionApi.ingestPlaySessions`), so the existing sync (integration-jkbm1) imports that
session afterwards.

1. **Resolve the Play target when Play is clicked, not when the page loads.** Add a plain Giraffe
   route on the Mediatheca server, e.g. `GET /api/romm/play/{romId}`. It must sit under `/api/`
   so the Vite dev proxy forwards it, and must not collide with Fable.Remoting's
   `/api/{TypeName}/{Method}` routes. The route answers with a **302** to the RomM URL.
   `GameDetail.RommPlayUrl` stays gated exactly as today (rom id + `romm_base_url` + playable
   platform). Its value becomes this Mediatheca endpoint instead of a RomM URL. This way, loading
   the game page never makes a live RomM call, and the state comes from RomM at click time.
   The API token stays on the server.
2. **EmulatorJS platforms** (`playerRouteFor = Some "ejs"`): call `GET /api/states?rom_id={id}`
   (new `RomM.getLatestState`-style adapter function). Pick the newest state by `updated_at`
   and redirect to `{base}/console/rom/{id}/play?state={stateId}`. If the rom has no states, or
   the RomM call fails or times out, redirect to `{base}/console/rom/{id}/play` (a fresh boot).
   A RomM problem must never block Play.
3. **Ruffle / js-dos / PICO-8**: keep the current `{base}/rom/{id}/{player}` routes. The console
   Play view runs EmulatorJS only (it checks `DISABLE_EMULATOR_JS`).
4. If the endpoint gets a rom id with no playable platform, or `romm_base_url` is missing, it
   returns 404. It never sends a partial redirect.

The client's anchor is unchanged: `target="_blank"`, `rel="noopener noreferrer"`. Only its
`href` value changes, and the server sets it.

## Acceptance criteria
- [ ] For a RomM-linked game on an EmulatorJS platform, `getGameDetail` returns a `RommPlayUrl`
      that points at the Mediatheca play endpoint for that rom. The existing `None` cases from
      `RomMPlayButtonTests.fs` still hold: no rom id, no base URL, unplayable platform, no
      platform row. Covered by Expecto tests.
- [ ] The play endpoint redirects (302) an EmulatorJS rom that has states to
      `{base}/console/rom/{id}/play?state={newestStateId}`. "Newest" means the latest
      `updated_at`, and the test uses at least two states. Covered by an Expecto test with a
      stubbed or recorded RomM states response.
- [ ] The endpoint redirects to `{base}/console/rom/{id}/play` with no `state` param when the rom
      has no states, and also when the states call returns an error or does not parse. Covered
      by Expecto tests.
- [ ] Ruffle, js-dos and PICO-8 roms still redirect to `{base}/rom/{id}/{player}` and trigger no
      states call. Covered by an Expecto test.
- [ ] The redirect `Location` never contains the RomM API token, and a base URL with a trailing
      slash produces no double slash. Covered by the tests above.
- [ ] The states response decoding is pinned against RomM 5.3.1's `StateSchema`. The RomM
      version and source path are recorded in a comment, as `playerRouteFor` does.
- [ ] On harbour, clicking Play on a real NES/SNES/GBA-class game opens a new tab and the game
      is running with no second Play click. If a save state exists, the game resumes from the
      newest one. [human-eye]

## Notes
- **Builder decision (2026-09-28):** resume the latest state, not only a fresh boot.
- **States belong to one user.** RomM's `/api/states` returns only the states of the user who
  owns the API token. This is a single-user app, so we assume the token user and the browser's
  RomM login are the same account. If they are not, the endpoint finds no states and does a
  fresh boot. That is acceptable.
- **Check that the core matches (worker).** Save states depend on the core. Check how
  `console/views/Play.vue` picks its core, and whether `StateSchema` has an `emulator`/core
  field. If a state saved with a different core would fail to load, only pick states made with
  the core that Play.vue will boot. If you can't pin this down, pick the newest state as
  specified and say so in the Outcome.
- Save **files** (`?save=`, battery/SRAM saves) are out of scope. EmulatorJS loads the rom's
  latest save file itself in the normal flow. This task only picks a **state**.
- Leaving the game: console mode's exit prompt goes back to RomM's console-mode game page, not to
  Mediatheca. That is fine, because the game runs in its own tab.
- The playtime loop closes: console Play records a RomM play session when you exit, and the
  next RomM sync imports it. This also answers integration-q748k's open question for this
  route.
- Source checked 2026-09-28 against `rommapp/romm` tag `5.3.1`: `frontend/src/plugins/router.ts`,
  `frontend/src/views/Player/EmulatorJS/Base.vue`, `frontend/src/console/views/Play.vue`,
  `backend/endpoints/states.py`.

## Verifier note (iteration 1)

**REASONS:**
- Check 5 (README sync) failed: `blocks.readmeDelta[0].section` is `"Bounded context"`, but the integration README has no such section (headings: Purpose, Classification, Actors, Ubiquitous language, Aggregates, Key events, Key commands, Relationships with other contexts, Frontend gate, Open questions). The `RomM Play button` anchor bullet sits under `## Ubiquitous language` (line 37). The op's `expected` text matches that bullet byte-for-byte; only the section name is wrong. With the section missing, `applyReadmeDelta` falls back to `appended-fallback` — the new body is appended as an extra bullet and the old one (still saying `RommPlayUrl` is `Some "{romm_base_url}/rom/{romId}/{player}"`) stays, leaving the README self-contradictory.
- Secondary: the replacement `body` drops still-true facts without relocating them — `RomMSync` upserts `platform_slug` into `romm_rom_platform` from `getRomDetail`; the table is `MetadataCache`-shaped (no events, not checkpoint-tracked, classified `Cache` in `Administration.tableRegistry`); owned by `GameProjection.fs` so every `handler.Init` caller gets it; and the RomM frontend source commit pin `95599dad…`.
- Checks 1–4 passed: every machine-checkable criterion is covered by Expecto tests; suite exit 0; `npm run build` exit 0; route `routef "/api/romm/play/%i"` ahead of `remotingHandler`, 302 via `redirectTo false`, `TrimEnd('/')` on base, token only in the Authorization header. Harbour criterion is [human-eye].

**SUGGESTED_FIX:** Resubmit the README_DELTA with `section: "Ubiquitous language"`, same anchor and `expected`, so it replaces rather than appends. Restore the dropped `romm_rom_platform` facts (RomMSync upsert, Cache classification, GameProjection ownership, commit pin) in the new body. No code changes needed.

**ITERATION_HINT:** likely-fixable

## Outcome

`GameDetail.RommPlayUrl` (`GameProjection.getBySlug`, `src/Server/GameProjection.fs`) now
returns `Some "/api/romm/play/{romId}"` — a Mediatheca endpoint — instead of a raw RomM URL,
for any RomM-linked game whose platform has an in-browser player configured. Gating is
unchanged (`romm_rom_id` + `romm_base_url` + `RomM.playerRouteFor`); only the produced string
changed. The client (`src/Client/Pages/GameDetail/Views.fs`) needed no change — it renders
`game.RommPlayUrl` as an opaque `prop.href`, and a relative URL resolves correctly for a
`target="_blank"` anchor.

The new `GET /api/romm/play/{romId}` route (`Api.rommPlayHandler`, `src/Server/Api.fs`) is
wired into `Composition.fs`'s Giraffe `choose` list as a plain three-segment route ahead of
`remotingHandler`, so it never collides with Fable.Remoting's two-segment
`/api/{TypeName}/{Method}` routes. It looks up the rom's recorded platform slug
(`GameProjection.getRommRomPlatformSlug`) and `romm_base_url` (via the existing
`getRomMConfig`), then:
- **EmulatorJS** (`playerRouteFor = Some "ejs"`): calls the new `RomM.getLatestState`
  (`src/Server/RomM.fs`, `GET /api/states?rom_id={id}`, decoding RomM 5.3.1's `StateSchema`)
  and 302s to RomM's CONSOLE-mode route, `{base}/console/rom/{id}/play`, appending
  `?state={newestId}` (picked by the latest `updated_at`) when a state exists. An empty states
  list, a failed call, or an unparsable body all degrade to the same route with no `state`
  param — a fresh boot, never blocking Play.
- **Ruffle/js-dos/PICO-8**: redirects straight to the existing `{base}/rom/{id}/{player}` route
  with no states call at all — unchanged from integration-q748k.
- **No playable platform, no recorded platform row, or no `romm_base_url`**: 404, never a
  partial redirect.

**Core-matching (task Notes, "Check that the core matches"):** checked RomM 5.3.1 source
(`backend/endpoints/responses/assets.py`'s `StateSchema` — confirms an `emulator: str | None`
field exists — and `frontend/src/console/views/Play.vue`'s `boot()`). `StateSchema.emulator` is
decoded onto `RomM.RomMState` but deliberately **not** used to filter candidates: `boot()`
picks its EmulatorJS core from the BROWSER's own `localStorage`
(`player:{romId}:core`/`player:{platformSlug}:core`), never from the state it applies via
`gameManager.loadState`. The server has no visibility into that per-browser value, so there is
no core to match a state against from here — this is the "can't pin it down" case the task's
Notes explicitly allow for, and the newest state (by `updated_at`) is picked regardless of
core. Documented in `RomM.RomMState`'s doc comment and the play-endpoint's own doc comment.

**Tests (TDD, red-then-green verified):**
- `tests/Server.Tests/RomMTests.fs` — 3 new cases for `RomM.getLatestState`: newest-by-
  `updated_at` selection (hand-authored fixture pinned against `StateSchema`, 2 states),
  `Ok None` for an empty states array, `Error (RomMOtherFailure _)` for an unparsable body.
  Verified the "newest" test is load-bearing by temporarily flipping the sort order (`sortBy`
  instead of `sortByDescending`) — confirmed it fails for the right reason — then reverted.
- `tests/Server.Tests/RomMPlayButtonTests.fs` — updated the two existing tests whose expected
  `RommPlayUrl` value referenced a raw RomM URL to expect `/api/romm/play/{romId}` instead
  (confirmed genuinely red against the un-updated production code first). The other five
  existing `None`-case tests (no rom id, no base URL, unplayable platform, no platform row,
  token-safety) needed no change and still pass.
- `tests/Server.Tests/RomMPlayEndpointTests.fs` (new) — 12 cases against a minimal `TestServer`
  (mirrors `PdfServingTests.fs`'s shape) wiring `Api.rommPlayHandler` through the same
  `routef`/`choose` pattern `Composition.fs` uses, with a stubbed RomM `HttpMessageHandler`:
  newest-state redirect, no-states fallback, RomM-error fallback, unparsable-body fallback,
  trailing-slash-safe base URL, token never in the redirect `Location`, Ruffle/js-dos/PICO-8
  each redirecting directly with an `HttpClient` whose handler asserts it is never called, and
  three 404 cases (unplayable platform, no platform row, no `romm_base_url`).
- `src/Shared/Shared.fs`'s `GameDetail.RommPlayUrl` doc comment updated to describe the new
  Mediatheca-endpoint value instead of the retired raw-RomM-URL shape.

Full Expecto suite: 1121/1121 passing (`dotnet run --project tests/Server.Tests/
Server.Tests.fsproj`). `npm run build` (Fable/Vite) succeeds with the `Shared.fs` comment
change included.

**Not verified — [human-eye] on harbour:** the task's final acceptance criterion ("clicking
Play on a real NES/SNES/GBA-class game opens a new tab and the game is running with no second
Play click; if a save state exists, the game resumes from the newest one") requires a live
RomM instance and a real browser session and was not exercised by this worker — flagged
per the task's own instructions.

**Iteration 2:** bookkeeping-only fix — the `README_DELTA`'s `section` corrected from "Bounded
context" to "Ubiquitous language" (where the RomM Play button bullet actually lives) and its
`body` restored the facts the iteration-1 replacement had dropped (the `romm_rom_platform`
upsert from `getRomDetail`, its `MetadataCache`-shaped/`Cache`-classified/non-checkpointed
nature, its ownership in `GameProjection.fs`, and the RomM tag/commit pin); no source or test
file changed.
