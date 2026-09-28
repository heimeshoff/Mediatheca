---
id: integration-f8ncw
title: RomM Play boots the game directly — the game page's Play button starts EmulatorJS via RomM's console-mode route and resumes the rom's newest save state, instead of landing on RomM's pre-game setup screen
status: doing
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
