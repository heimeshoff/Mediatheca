---
id: integration-jkbm1
title: RomM adapter — on a scheduled sync, import play sessions from the self-hosted RomM instance for the platforms picked in Settings (Nintendo by default), linking or creating a Game only for roms that have played sessions
status: done
type: feature
context: integration
created: 2026-09-25
completed:
depends_on: [games-rmxg2, design-system-001]
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

- pages through RomM's **play sessions** (`GET /api/play-sessions`, no `rom_id` filter, which
  the live schema allows) and keeps only closed ones (`end_time` set),
- groups them by `rom_id` and keeps roms whose platform (from `GET /api/roms/{id}`) is among
  the selected platforms. **Roms with no closed session are never touched**: no Game is
  created or linked for them (builder decision, 2026-09-25),
- matches or creates the corresponding **Game**, then links it with the rom-id external
  identity that `games-rmxg2` adds (`Set_romm_rom_id`),
- imports the sessions as Games play sessions with source `RomM`: one
  `games-rmxg2` `Record_romm_play_session` per (game, gaming day).

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
- `GET /api/play-sessions?rom_id=&device_id=&start_after=&end_before=&limit=&offset=` (scope
  `roms.user.read`; every parameter optional, `limit` defaults to 50) → `PlaySessionSchema` { `id`, `rom_id`, `device_id`, `save_slot`,
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
- [ ] Only roms with at least one closed RomM session on a selected platform are processed. A
      rom with no sessions, or only open ones, creates and links nothing (fixture test).
- [ ] For every such rom not yet linked, a sync run tries these in order:
      1. `GameProjection.findByRommRomId`.
      2. On a miss, match an existing library game by normalized name, plus release year when
         both have one, and attach the rom id via `Set_romm_rom_id`. If **more than one**
         library game matches, the rom is skipped: nothing is linked or created, its sessions
         aren't imported, and it's counted as `ambiguous` (with its name) in the run summary.
         It is retried on every run, so linking it by hand later is enough.
      3. If nothing matches, create the Game from RomM's own metadata (name, summary, cover,
         genres, release date) through the identity-card path Steam creation uses
         (`MetadataCache.upsertGameIdentityCard`), then attach the rom id.

      Each branch, including the ambiguous skip, has a fixture-driven test.
- [ ] Every RomM session with `end_time` set on a linked rom becomes part of a
      `Play_session_recorded` (`Source = RomM`) via `Record_romm_play_session`. Its gaming day
      is `PlaytimeTracker.toGamingDay (PlaytimeTracker.getSyncHour conn)` applied to the
      session's `start_time` converted from UTC to server-local time. This is the same
      function and the same `playtime_sync_hour` boundary Steam and manual sessions use, never
      `romm_sync_hour`. One session lands on one day with no splitting, and all of a game's
      new sessions on the same gaming day go in one command. A test pins a UTC `start_time`
      just before and after the boundary. Sessions without `end_time` are skipped and
      imported on a later sync once RomM closes them.
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
  emulator RomM doesn't know about still produce none, so they don't enter Mediatheca through
  this adapter.
- **Sessions-first scope** (builder decision, 2026-09-25, second refine). A Game is created or
  linked only when its rom has a closed play session. This keeps never-played ROM dumps out of
  the library, so new games still land in Backlog and are promoted to InFocus by the session
  itself. It is also why the sync starts from `/api/play-sessions` rather than from the rom
  list: fetching sessions first means one paged call per run instead of one call per rom.
- **Ambiguous matches are skipped, never guessed** (builder decision, 2026-09-25). A duplicate
  Game is worse than a rom waiting to be linked by hand.
- **One task, not split** (builder decision, 2026-09-25). The Settings card is meaningless
  without its endpoints, which matches how the Audible sync shipped.
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

## Verifier note (iteration 1)

**REASONS:**
- Check 1, criterion 1 (Settings card: base URL, masked token, platform picker filled live from `GET /api/platforms`, Nintendo pre-checked on first load, saving persists `romm_base_url` / `romm_api_token` / `romm_platform_ids` as a JSON array through `SettingsStore`) has no test and no artifact covering it. No Expecto test calls `Api.setRomMSettings` / `getRomMSettings` / `fetchRomMPlatforms`. That leaves the JSON-array persistence and the parse in `Composition.getRomMConfig` (src/Server/Composition.fs:510) unpinned, and nothing checks that `getRomMSettings` never returns the token. The Nintendo pre-check branch in `RomM_platforms_loaded` (src/Client/Pages/Settings/State.fs, around lines 91-110) has no Vitest case, even though `npm run test:client` exists. The Outcome has no manual-exercise note for this criterion either; it only calls the code "trivial Elmish plumbing", which does not count as coverage. The `[human-eye]` marker is on criterion 2, not criterion 1.
- Check 1, criterion 13 (`RomM.fs` decoding tested "against recorded RomM JSON fixtures") is not met as written. The fixtures are hand-authored ("matching the RomM v5.3.1 schemas"), but the task's Notes say to record them from the live instance with a read-only token, scrubbing the token. The decoders make wire-shape assumptions that only a recorded fixture can confirm: `/api/play-sessions` decoded as a plain array while `/api/roms` is a paged `{items,...}` envelope; `metadatum.first_release_date` treated as Unix seconds; `url_cover` assumed same-host and needing the bearer token.
- Minor, related to criterion 4: `fetchRomMPlatforms` in src/Server/Api.fs writes `romm_last_error` on a 401/403 but does not clear it when a later call succeeds.
- Also noted: the `Set_romm_rom_id` results in `RomMSync.resolveSlug` are discarded with `|> ignore`. If linking fails, sessions are still imported and the rom counts as linked or created.

**SUGGESTED_FIX:** Record real responses for `/api/platforms`, `/api/roms`, `/api/roms/{id}` and `/api/play-sessions` from the live RomM instance with a scrubbed read-only token (RomM HTTP only, never Mediatheca's live DB), use them as the fixtures, and fix any decoder they break. Add an Expecto test for `setRomMSettings`/`getRomMSettings` (JSON-array persistence, token never returned) and a Vitest case for the Nintendo pre-check in `RomM_platforms_loaded`. Clear `romm_last_error` when `fetchRomMPlatforms` succeeds.

**ITERATION_HINT:** likely-fixable

**Conductor note:** RomM's public `/openapi.json` (no auth) confirms `/api/play-sessions` → plain array, `/api/platforms` → plain array, `/api/roms` → `CustomLimitOffsetPage_SimpleRomSchema_` envelope, `first_release_date` → integer (unit not stated by the spec), `url_cover` → nullable string. Every `/api/*` data endpoint answers 401 without a token.

## Salvage note

Paused by the builder after verification iteration 1 (2026-09-25 18:05) until a read-only RomM Client API Token is available for recording real fixtures. The worktree `.worktrees/integration-jkbm1` (branch `aw/integration-jkbm1`) is kept with the iteration-1 work committed as `wip [integration-jkbm1] iter 1`.
- Code diff salvaged to `.agentheim/salvage/integration-jkbm1-escalated-iter1.patch`
- Worker's iteration-1 RESULT (README_DELTA + OUTCOME drafts) salvaged to `.agentheim/salvage/integration-jkbm1-escalated-iter1.bookkeeping.md`

To resume: provide the token (a scratchpad file, never committed), then run `/agentheim:work integration-jkbm1`. Phase 1 recovery reuses this worktree and re-dispatches iteration 2 against the Verifier note above.

## Outcome

Iteration 2 addressed every point in the iteration-1 verifier note without changing the adapter's scope or shape:

**1. Criterion 1 (Settings persistence) now has tests.** `tests/Server.Tests/RomMApiTests.fs` (new, 6 cases) exercises `Api.create`'s `getRomMSettings`/`setRomMSettings`/`fetchRomMPlatforms` end to end against a real `SettingsStore`-backed connection: `romm_platform_ids` persists as a JSON array (`[1,3,7]`, matching `Composition.getRomMConfig`'s own parser and `steam_family_members`'s convention); `getRomMSettings` never returns the raw token (only `TokenConfigured: bool`, pinned by asserting the token string never appears in the DTO's printed form, while confirming server-side it IS stored); `ApiToken = None` keeps the previously-saved token, matching Audible/Steam's "blank keeps existing" convention; an unset `romm_platform_ids` degrades to an empty selection, not an error; and `fetchRomMPlatforms` clears/sets `romm_last_error` on success/401 respectively. `src/Client/Pages/Settings/RomMPlatformPrecheck.test.fs` (new Vitest, 4 cases) exercises `RomM_platforms_loaded`'s Nintendo pre-check branch directly through `State.update`: both name- and slug-matched Nintendo platforms are pre-checked only when nothing is selected yet; a previously-saved non-empty selection is never overridden; and a failed fetch records the error without touching the selection.

**2. Criterion 13 (recorded fixtures) is now met.** Recorded real responses from `https://romm.elver-minor.ts.net` (RomM v5.3.1) with the read-only Client API Token supplied for this purpose: `GET /api/platforms` (kept verbatim, 3 platforms — this instance has no non-Nintendo platform yet, so platform-filter *behaviour*, as opposed to wire decoding, stays covered by `RomMSyncTests.fs`'s synthetic platform ids), `GET /api/roms?limit=3&offset=0` (trimmed to 2 items, dropped `char_index`/`filter_values`), `GET /api/roms/33` ("3 Ninjas Kick Back", trimmed of unused metadata sibling blocks, kept every field the decoder reads plus `rom_user` verbatim), and `GET /api/play-sessions?limit=10&offset=0` (trimmed to 2 of 10 sessions). Replacing the hand-authored fixtures with these caught two real bugs, both fixed:
- `metadatum.first_release_date` is Unix **milliseconds**, not seconds — proved by cross-checking the same rom's sibling `igdb_metadata.first_release_date` (genuine IGDB seconds) recorded alongside it, and by the millisecond interpretation (1994-11-19) matching "3 Ninjas Kick Back"'s real release year while the seconds interpretation would overflow `DateTimeOffset`'s representable range entirely. Fixed in `RomM.decodeRomDetail` (`FromUnixTimeMilliseconds`, was `FromUnixTimeSeconds`) and in `RomMSyncTests.fs`'s `romDetailJson` fixture-builder helper (`ToUnixTimeMilliseconds`, was `ToUnixTimeSeconds`).
- `url_cover` is typically an absolute, third-party CDN URL (libretro's public thumbnail host), not a same-host RomM asset. Added `RomM.isSameHost` and made `RomM.downloadCover` attach the bearer token only when the cover URL's host matches `config.BaseUrl`'s host — never leaking the Client API Token to an unrelated third party. Three new Expecto cases in `RomMTests.fs` pin this (same-host sends the token, third-party host does not, `isSameHost` itself for scheme/query/path-independence and a graceful `false` on an unparseable URL).
- `start_time`/`end_time` carry NO timezone suffix at all (`"2026-09-25T10:43:02"`) — confirmed the existing `decodeUtcDateTime`'s `AssumeUniversal` handling (unchanged) already parses this correctly as UTC; pinned with an explicit `DateTime(2026,9,25,10,43,2,DateTimeKind.Utc)` equality assertion.
- All four `RomMTests.fs` decoding tests (platforms, roms list, rom detail, play sessions) were rewritten against the recorded values with updated assertions (real names/ids/years/URLs).

**3. `fetchRomMPlatforms` now clears `romm_last_error` on success** (`src/Server/Api.fs`) — a successful platforms fetch is unambiguous proof the token is valid, mirroring `RomMSync.runSync`'s own successful-fetch clear. Covered by a new `RomMApiTests.fs` case.

**4. `RomMSync.resolveSlug`'s discarded `Set_romm_rom_id` result is now handled.** Both the "matched by name" and "created" branches now match on `executeGameCommand`'s `Result` instead of `|> ignore`-ing it: a failure degrades to the existing `Failed` outcome (not counted as linked/created, sessions not imported, retried next run) exactly like a creation failure already did. Forced the failure in a fixture test the only reliable way available without real concurrency: seeded a normal Active game, then appended `Game_removed_from_library` directly to its event stream WITHOUT running the projection (so `game_detail` still lists it as a name-matching candidate while `Games.decide` itself now refuses `Set_romm_rom_id` with "Game has been removed") — the exact `executeGameCommand` failure path `resolveSlug` must degrade on.

Verified the recorded token never leaked: `grep -rn "rmm_" tests src` (excluding the pre-existing UI placeholder string `"rmm_..."` and this iteration's own synthetic test tokens like `rmm_test_token`) returns nothing, and a direct `grep` for the literal token value across the full working-tree diff returns nothing.

`npm run build` (Fable, clean), `dotnet run --project tests/Server.Tests/Server.Tests.fsproj` (1045 passed, up from 1035 after iteration 1, 0 failed), and `npm run test:client` (129 passed, up from 125, 0 failed) are all green.

Key files: `src/Server/RomM.fs`, `src/Server/RomMSync.fs`, `src/Server/Api.fs`. Tests: `tests/Server.Tests/RomMApiTests.fs` (new), `tests/Server.Tests/RomMTests.fs`, `tests/Server.Tests/RomMSyncTests.fs`, `src/Client/Pages/Settings/RomMPlatformPrecheck.test.fs` (new).
