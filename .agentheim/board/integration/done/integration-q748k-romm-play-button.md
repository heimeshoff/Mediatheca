---
id: integration-q748k
title: RomM play button — a RomM-linked game whose platform RomM can play in the browser shows a Play button in the game page hero that opens RomM's web player for that rom in a new browser tab
status: done
type: feature
context: integration
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [romm, games, emulation, game-detail]
related_adrs: [0088]
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: [integration-jkbm1]
---

## Why
Games played through RomM enter Mediatheca through the sync (integration-jkbm1). To play one
again you still have to go to RomM and find the rom. The game page already knows the RomM rom
id, and RomM has a player in the browser, so one click could start the game.

## What
A **Play** button in the game detail hero (`src/Client/Pages/GameDetail/Views.fs`, in the hero's
title/meta block). Clicking it opens RomM's web player for the game's linked rom in a **new
browser tab** (`target="_blank"`, `rel="noopener noreferrer"`).

The server builds the URL, so the client never assembles RomM URLs or knows the platform rules.
`GameDetail` in `src/Shared/Shared.fs` gets a new optional field (e.g.
`RommPlayUrl: string option`). It is `Some url` only when **all** of these hold:

1. the game has a RomM rom id (set by the sync's `Set_romm_rom_id`; there is no manual linking
   in this task),
2. `romm_base_url` is configured in `SettingsStore`,
3. the rom's platform is one RomM's web player supports.

The URL is `{romm_base_url}/rom/{romId}/{player}`. `{player}` depends on the platform and uses
RomM's own route names from `frontend/src/plugins/router.ts` (checked 2026-09-27):
- `ejs` for EmulatorJS,
- `ruffle` for Flash,
- `jsdos` for DOS,
- `pico8` for PICO-8.

Trim any trailing slash from `romm_base_url`. Never put the API token in the URL. The browser
uses its own RomM login session.

**Knowing the platform.** Mediatheca doesn't store a linked rom's platform yet. `RomMSync`
already calls `RomM.getRomDetail` for **every** rom that has closed sessions, including roms
that are already linked. Record that rom's platform slug in a small integration-owned lookup
table in the same step (e.g. `romm_rom_platform(rom_id, platform_slug)`, upserted on every
run). Treat it as external-metadata cache, like `MetadataCache`: no new `GameCommand`/`GameEvent`,
no new event stream. Already-linked games are backfilled by the next sync run, so no separate
backfill job is needed. `GetGameDetail` joins through the game's `romm_rom_id`. If there is no
row, the game gets no button.

**Which platforms are playable.** A pure function in `RomM.fs` (e.g.
`playerRouteFor: platformSlug -> string option`) maps a platform slug to a player route. It
copies the slug lists RomM's own frontend uses to decide whether to show its Play button
(`isEJSEmulationSupported` / `isRuffleEmulationSupported` / js-dos / PICO-8 helpers in RomM's
`frontend/src/utils`, pinned to the release on harbour, 5.3.1). Record the RomM version and
source path in a comment. Switch, GameCube and Wii map to `None`. Emulator **streaming**
(`/rom/:rom/stream`) is out of scope.

## Acceptance criteria
- [ ] `RomM.playerRouteFor` returns `Some "ejs"` for EmulatorJS platform slugs, including the
      Nintendo handhelds and home consoles in RomM's list (e.g. `nes`, `snes`, `n64`, `gb`,
      `gbc`, `gba`, `nds`), `Some "ruffle"` / `Some "jsdos"` / `Some "pico8"` for their
      platforms, and `None` for `switch`, `ngc` and `wii` and for an unknown slug. It is covered
      by Expecto tests.
- [ ] A sync run upserts the platform slug of every rom whose detail it fetched, linked or
      newly linked, into the lookup table. A second identical run leaves the table unchanged
      and still appends zero events. This is covered by the existing fixture-driven `RomMSync`
      tests or an extension of them.
- [ ] `getGameDetail` returns `RommPlayUrl = Some "{base}/rom/{id}/ejs"` for a RomM-linked game
      on an EmulatorJS platform. It returns `None` when the game has no RomM rom id, when
      `romm_base_url` is empty, when the platform maps to `None`, and when no platform row
      exists yet. Each case has an Expecto test, and one test pins that a configured base URL
      with a trailing slash does not produce a double slash.
- [ ] The URL never contains the RomM API token (asserted in the tests above).
- [ ] The hero renders a Play button only when `RommPlayUrl` is `Some`. It is an anchor with
      `href` = that URL, `target="_blank"` and `rel="noopener noreferrer"`, so clicking it
      opens a new tab and leaves Mediatheca on the game page. A Steam-only or manual game shows
      no button.
- [ ] The button uses existing `DesignSystem.fs` compositions and Velvet Lobby tokens. It is
      added to the StyleGuide page only if it introduces a new composition. It reads as the
      hero's primary action without crowding the status and rating controls, on desktop and
      at phone width. [human-eye]
- [ ] Clicking Play on a real RomM-linked NES/SNES/GBA-class game on harbour opens RomM's
      player in a new tab and the game boots. [human-eye]

## Notes
- **Builder decisions (2026-09-27):** show the button only when the platform is playable in the
  browser, not on every RomM-linked game. Go straight to the player route, not RomM's game
  page. Only games the sync has already linked get a button; linking a game to a rom by hand
  is a separate idea and not captured here.
- **Routing:** this task is filed under integration because the RomM URL shape, the
  platform-to-player rules and the base-URL setting all belong to the RomM adapter. The Games
  side changes only by gaining one optional DTO field and one hero control.
- The browser must already be logged into RomM (same-browser session cookie). If it isn't,
  RomM shows its login page first. That is acceptable, and Mediatheca doesn't try to handle it.
- Open question for the builder, not a blocker: does playing in RomM's web player record a
  RomM play session? If it does, the loop closes itself: play from Mediatheca, and the next
  sync imports the session. It is worth checking once while doing the eye-check above.
- RomM also has a console-mode route (`/console/rom/:rom/play`). It isn't used here because the
  per-player routes skip RomM's own detail page.

## Outcome

Added a **Play** button to the game detail hero (`src/Client/Pages/GameDetail/Views.fs`, in the
title/meta block, right below the year/playtime line so it doesn't crowd the status/rating row).
It renders only when `GameDetail.RommPlayUrl` (new `string option` field, `src/Shared/Shared.fs`)
is `Some`, as an anchor with `target="_blank"`/`rel="noopener noreferrer"`. Styling reuses
`DesignSystem.heroCard`'s "▶ Watch" button tokens verbatim (`bg-gold text-primary-content
rounded-full ...`) inline rather than adding a new composition, since no new visual shape was
needed — nothing added to the StyleGuide page.

Server side, `GameDetail.RommPlayUrl` is computed entirely in `GameProjection.getBySlug`
(`src/Server/GameProjection.fs`): a new LEFT JOIN through a new `romm_rom_platform(rom_id,
platform_slug)` table (created in `GameProjection.createTables`, classified `Cache "RomMSync"` in
`Administration.tableRegistry`) resolves the linked rom's platform slug, `RomM.playerRouteFor`
(`src/Server/RomM.fs`) maps it to a player route, and a non-empty `romm_base_url`
(`SettingsStore`) completes the URL — trimming any trailing slash. Any missing piece (no
`romm_rom_id`, no base URL, unplayable platform, no platform row yet) degrades to `None`, never a
partial URL; the token is never read into the URL at all.

`RomM.RomMRomDetail` gained a `PlatformSlug: string` field, decoded from the SAME `platform_id`
rom-detail response `RomMSync` already fetches per rom with a closed session — no second HTTP
call. `RomMSync.runSync`'s `importFor` (called for AlreadyLinked/Matched/Created roms, never for
Ambiguous/Failed ones) now upserts `(rom.Id, rom.PlatformSlug)` into `romm_rom_platform` via the
new `GameProjection.upsertRommRomPlatform`, mirroring the existing `findByRommRomId` call site.
Already-linked games get backfilled the next time they have a closed session, per the task's own
design — no separate backfill job.

`RomM.playerRouteFor`'s slug lists were pulled from RomM's real source (tag `5.3.1`,
`rommapp/romm`, commit `95599dadbe93c8f7b8a8647148fafbe293df3167`,
`frontend/src/utils/index.ts`'s `isEJSEmulationSupported`/`isRuffleEmulationSupported`/
`isJsDosEmulationSupported`/`isPico8EmulationSupported`), not reconstructed from general
knowledge — fetched and transcribed via a research pass, cited in the function's own doc comment.
Deliberately excludes EmulatorJS's netplay-only nightly-core slugs (3ds, new-nintendo-3ds,
intellivision), which RomM only shows behind a server-side heartbeat flag this adapter can't read
— treated as unplayable rather than guessed.

Tests: `RomMTests.fs` gained 6 new `playerRouteFor` cases (EJS Nintendo handhelds/consoles,
Ruffle, js-dos, PICO-8, Switch/GameCube/Wii/unknown → `None`, case-insensitivity) plus a
`PlatformSlug` decode assertion on the existing recorded fixture. `RomMSyncTests.fs`'s
`romDetailJson` helper gained a `platformSlug` parameter (all 9 call sites updated) and a new
test proves a sync run upserts the linked rom's platform slug and a second identical run leaves
both the table and the event stream position unchanged. A new `RomMPlayButtonTests.fs` covers
`getBySlug`'s `RommPlayUrl` directly against seeded projection state: the happy path, each `None`
degradation (no rom id, no base URL, unplayable platform, no platform row yet), the
trailing-slash-safe URL, and that the API token never appears in the built URL.
`GameDetail/DefaultTab.test.fs`'s mock `GameDetail` record gained `RommPlayUrl = None`.

Verified: `dotnet build` (Server + Server.Tests) clean; full Expecto suite 1059/1059 passing;
`npm run build` (Fable/Vite) clean; `npm run test:client` (Vitest) 129/129 passing.

**For the builder's eye-check (human-eye criteria not verifiable from a worktree):** confirm the
Play button reads as the hero's primary action (not crowding status/rating) on desktop and phone
width, and — per the task's own open question — check once whether playing via RomM's web player
records a RomM play session, since if it does the loop closes itself on the next sync.
