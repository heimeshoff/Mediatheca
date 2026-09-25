---
id: integration-jkbm1
title: RomM adapter — import Nintendo games' metadata and play sessions from the self-hosted RomM instance via its REST API
status: backlog
type: feature
context: integration
created: 2026-09-25
completed:
depends_on: []
blocks: []
tags: [romm, games, playtime, nintendo, sync]
related_adrs: []
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: []
---

## Why
Nintendo games are played outside Steam, so Mediatheca's playtime and diary never see them.
RomM (self-hosted on harbour at `https://romm.elver-minor.ts.net`, v5.3.1) already catalogues
those ROMs with rich metadata and records play sessions. It exposes a REST API, so Mediatheca
can pull that data the same way it pulls Steam playtime, without anyone entering it by hand.

## What
A new Integration adapter (`RomM.fs`) plus a sync that:

- lists the Nintendo platforms and their games from RomM,
- matches or creates the corresponding **Game** in the Games BC,
- imports RomM **play sessions** as Games play sessions with a new session source (`RomM`,
  alongside `SteamSync` and `Manual`), so they feed play time, the Journal diary and the
  "any play session promotes to InFocus" rule.

### RomM API surface (checked against the live instance's `/openapi.json`, 2026-09-25)
- **Auth:** Client API Token (`rmm_` + 64 hex chars), sent as `Authorization: Bearer rmm_…`.
  Per-user, and you choose its scopes. Read-only is enough: `roms.read`, `roms.user.read`,
  `platforms.read`, and `assets.read` only if covers are pulled. HTTP Basic and OAuth2 password
  (`POST /api/token`) also exist. Interactive docs: `https://romm.elver-minor.ts.net/api/docs`.
- `GET /api/platforms` — platform ids and slugs, used to select the Nintendo ones.
- `GET /api/roms?platform_ids=…&updated_after=…&limit=&offset=` — paged game list, which
  suits incremental sync. It can also filter by `last_played`.
- `GET /api/roms/{id}` → `DetailedRomSchema`: name, summary, `metadatum` (genres, companies…),
  provider ids (`igdb_id`, `ss_id`, `hltb_id`, `ra_id`, `steam_id`, `moby_id`…), per-provider
  metadata including `hltb_metadata`, covers (`url_cover`, `path_cover_large`), and
  `rom_user` { `last_played`, `now_playing`, `backlogged`, `status`, `completion`, `rating`,
  `difficulty`, `hidden` }.
- `GET /api/play-sessions?rom_id=&device_id=&start_after=&end_before=&limit=&offset=` (scope
  `roms.user.read`) → `PlaySessionSchema` { `id`, `rom_id`, `device_id`, `save_slot`,
  `start_time`, `end_time`, `duration_ms`, `created_at`, `updated_at` }.

## Acceptance criteria
- [ ] The RomM base URL and Client API Token are configurable in Settings. The token is never
      logged, and a 401/403 surfaces as a clear "token invalid" state, like Steam's manual token.
- [ ] A sync imports every game on the configured Nintendo platforms. A game already in the
      library is matched rather than duplicated (matching rule decided in refinement).
- [ ] Every RomM play session becomes a Games play session with source `RomM`, attributed to
      the right gaming day. Re-running the sync with no new RomM sessions produces zero new
      events (idempotent).
- [ ] A RomM session on a game that isn't InFocus promotes it to InFocus, just like Steam and
      manual sessions do.
- [ ] Adapter decoding is covered by tests against recorded RomM JSON fixtures (platforms,
      roms list, rom detail, play sessions).

## Notes
- **Playtime only exists if a RomM client records it.** RomM stores sessions only when a
  client posts them (`POST /api/play-sessions`): the RomM desktop app timing a native emulator
  launch, or a handheld companion syncing. Games played on real Switch hardware or in an
  emulator RomM doesn't know about have no sessions. Before building the playtime half, check
  what harbour's instance actually holds (one `curl` with a token to `/api/play-sessions`).
  The metadata import is useful even with zero sessions.
- **Open questions for refinement:**
  - **Identity matching.** Mediatheca Games are keyed on RAWG id (plus an optional Steam
    appId), but RomM carries IGDB, ScreenScraper, HLTB and similar ids, never RAWG. Options:
    match by name+year, resolve through RAWG search, or store a RomM rom id / IGDB id as a new
    external id on Game (a Games BC change, like `Set_steam_app_id`).
  - **Session granularity.** RomM sessions are individual start/end intervals. Games play
    sessions are one per `(gameSlug, gamingDay)` and merge by summing. Idempotency therefore
    needs a cursor that remembers which RomM session ids were already imported. Ideally it is
    derivable from the event log, in the spirit of ADR-0050's `SteamObservedMinutes`, rather
    than external imperative state.
  - **Games BC impact.** A new play-session source value (`RomM`) in the Games domain. Decide
    whether that is part of this task or a split-off Games task.
  - **Cadence.** Scheduled (`ScheduledJobs.fs`, like Steam) or client-initiated with a
    cooldown (like Jellyfin).
  - **What to take from `rom_user`.** Whether RomM's `status`, `completion` and `rating` should
    map onto Mediatheca status and rating, or be ignored in favour of Mediatheca's own
    lifecycle.
  - **Scope.** Nintendo platforms only, or every RomM platform with a platform filter in
    Settings.
- The research report `romm-vs-mediatheca-2026-09-23` §11 tags "API" as out of scope. That
  finding is about Mediatheca *exposing* a companion API. *Consuming* RomM's API, as here, is a
  different question that the report didn't evaluate.
