---
id: integration-jkbm1
title: RomM adapter — import games' metadata and play sessions for the platforms picked in Settings (Nintendo by default) from the self-hosted RomM instance, on a scheduled sync
status: backlog
type: feature
context: integration
created: 2026-09-25
completed:
depends_on: [games-rmxg2]
blocks: []
tags: [romm, games, playtime, nintendo, sync]
related_adrs: [0088, 0065, 0078]
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: []
---

## Why
Nintendo games are played outside Steam, so Mediatheca's playtime and diary never see them.
RomM (self-hosted on harbour at `https://romm.elver-minor.ts.net`, v5.3.1) already catalogues
those ROMs with rich metadata and records real play sessions. It exposes a REST API, so
Mediatheca can pull that data the same way it pulls Steam playtime, without anyone entering it
by hand.

## What
A new `RomM.fs` adapter plus a scheduled sync that:

- lists the configured platforms' games from RomM (`GET /api/platforms`,
  `GET /api/roms?platform_ids=…`),
- matches or creates the corresponding **Game**, then links it with the rom-id external
  identity that `games-rmxg2` adds (`Set_romm_rom_id`),
- imports RomM **play sessions** (`GET /api/play-sessions?rom_id=…`) as Games play sessions
  with source `RomM`, via `games-rmxg2`'s `Record_romm_play_session`.

This task adds no new `GameCommand` or `GameEvent`. It only calls the ones `games-rmxg2`
lands. ADR-0088 has the session-id cursor design.

### RomM API surface (checked against the live instance's `/openapi.json`, 2026-09-25)
- **Auth:** Client API Token (`rmm_` + 64 hex chars), sent as `Authorization: Bearer rmm_…`.
  Per-user, and you choose its scopes: `roms.read`, `roms.user.read`, `platforms.read`, and
  `assets.read` for covers. Interactive docs: `https://romm.elver-minor.ts.net/api/docs`.
- `GET /api/platforms`: platform ids and slugs, used by the Settings picker.
- `GET /api/roms?platform_ids=…&updated_after=…&limit=&offset=`: paged, suits incremental sync.
- `GET /api/roms/{id}` → `DetailedRomSchema`: name, summary, `metadatum` (genres,
  companies…), provider ids (`igdb_id`, `ss_id`, `hltb_id`, `steam_id`…), covers
  (`url_cover`, `path_cover_large`), and `rom_user` (ignored, see below).
- `GET /api/play-sessions?rom_id=&start_after=&end_before=&limit=&offset=` (scope
  `roms.user.read`) → `PlaySessionSchema` { `id`, `rom_id`, `device_id`, `save_slot`,
  `start_time`, `end_time`, `duration_ms`, `created_at`, `updated_at` }.

## Acceptance criteria
- [ ] Settings has a RomM card with Base URL, API Token (masked input), and a platform picker
      populated live from `GET /api/platforms`. Nintendo platforms are pre-checked on first
      load. Saving persists `romm_base_url`, `romm_api_token` and `romm_platform_ids` (a JSON
      array, like `steam_family_members`) via `SettingsStore`.
- [ ] The RomM card matches the existing Steam, Jellyfin and Audible cards
      (DesignSystem.fs compositions, paper overlay for any floating surface) and introduces no
      new component. [human-eye]
- [ ] The token never appears in a log line. No logging call interpolates the raw token or
      the `Authorization` header value.
- [ ] A 401/403 from any RomM call becomes a typed `TokenRejected` result. It is persisted to
      `romm_last_error` with a fixed `"RomM token rejected: "` prefix, mirroring
      `Steam.webApiKeyRejectedMessage`/`steam_api_key_last_error` (ADR-0065), and the next
      successful call clears it. Tested with a stubbed 401 response.
- [ ] For every rom on a selected platform, a sync run tries these in order:
      1. `GameProjection.findByRommRomId`.
      2. On a miss, match an existing library game by normalized name, plus release year when
         both have one, and attach the rom id via `Set_romm_rom_id`.
      3. If nothing matches, create the Game from RomM's own metadata (name, summary, cover,
         genres, release date) through the identity-card path Steam creation uses
         (`MetadataCache.upsertGameIdentityCard`), then attach the rom id.

      Each branch has a fixture-driven test.
- [ ] Every RomM session with `end_time` set on a linked rom becomes a `Play_session_recorded`
      (`Source = RomM`) via `Record_romm_play_session`. Its gaming day comes from the session's
      `start_time` through `PlaytimeTracker`'s existing gaming-day function, the same one
      Steam and manual sessions use: one session, one day, no splitting. Sessions without
      `end_time` are skipped and imported on a later sync once RomM closes them.
- [ ] `duration_ms` is rounded to the nearest minute, with a minimum of 1 for any
      `duration_ms > 0`. A real short session still consumes its RomM session id instead of
      being retried forever.
- [ ] Re-running the sync against unchanged fixture data (same roms, same sessions) appends
      zero new events. A fixture-driven Expecto test asserts the game stream's position is
      unchanged after a second identical sync.
- [ ] A RomM session on a game that isn't InFocus promotes it to InFocus, exercised end-to-end
      through the sync test.
- [ ] `rom_user` fields (`status`, `completion`, `rating`, `backlogged`, `now_playing`) never
      reach a command. The adapter's domain mapping does not reference them.
- [ ] A new `"RomM sync"` `JobSpec` in `ScheduledJobs.fs` runs at a configurable local hour
      (`romm_sync_hour`). Its default differs from `playtime_sync_hour`, so the two jobs don't
      contend for `PlaytimeTracker`'s shared `jobLock` in the same minute. A "Sync now" button
      in Settings shares the same `JobRunRecorder`/`tryStartJob` path, following the Audible
      sync's wrapper-`JobSpec` pattern (ADR-0078). `romm_last_sync` and `romm_last_error` are
      shown on the card.
- [ ] `RomM.fs` decoding is covered by Expecto tests against recorded RomM JSON fixtures:
      platforms, roms list, rom detail, and play sessions.

## Notes
- **Sessions exist.** The builder confirmed on 2026-09-25 that harbour's RomM already holds
  play sessions recorded by a RomM-aware client. Games played on real Switch hardware or in an
  emulator RomM doesn't know about still produce none; for those, the metadata import alone is
  the value.
- **Recording fixtures.** Record the fixtures from the live instance with a read-only token,
  scrubbing the token. The worker records RomM
  HTTP responses only. It never touches Mediatheca's live DB.
- **Cadence.** Scheduled, like Steam playtime, rather than client-initiated with a cooldown,
  like Jellyfin. RomM playtime builds up while the user is away from Mediatheca, so a sync
  triggered on page load would skip days when the SPA is never opened.
- **Platform scope.** The picker lists every RomM platform, not just Nintendo, with Nintendo
  pre-selected (builder decision, 2026-09-25).
- **`rom_user` is ignored** (builder decision, 2026-09-25). Mediatheca's own lifecycle and
  rating stay authoritative; only play sessions drive InFocus promotion.
- **Research scope.** Research report `romm-vs-mediatheca-2026-09-23` §11 puts "API" out of
  scope, but that means Mediatheca *exposing* a companion API. It doesn't apply here.
