---
id: integration-001
title: Jellyfin sync silently stopped writing episode watch history
status: done
type: bug
context: integration
created: 2026-05-27
completed: 2026-05-27
commit:
depends_on: []
blocks: []
tags: [jellyfin, sync, observability, watch-history, series]
related_adrs: []
related_research: []
prior_art: []
---

## Why

Series episode-watched state from Jellyfin stopped flowing into Mediatheca
around mid-May, even though the sync keeps running. The library no longer
reflects what was actually watched, which defeats the core "personal media
diary" value of the app.

This was confirmed with a **live, read-only diagnosis** against the Jellyfin
server on 2026-05-27 (token + URL taken from the backup DB, temp credentials
file deleted afterwards). The findings rule out the obvious causes and pin the
failure to the import execution path:

- **Token is valid, server reachable.** `GET /System/Info` and the exact
  `GET /Users/{id}/Items?IncludeItemTypes=Movie...` query the app issues both
  returned HTTP 200 with data. → The "expired/rejected token" hypothesis is
  **disproven** as the current trigger.
- **Movies are fine.** Jellyfin holds only 37 movies, 3 of them `Played`
  (Project Hail Mary, 28 Years Later, Fight Club) — all three already recorded
  in Mediatheca. Nothing new to sync on the movie side.
- **Series are broken.** Jellyfin has `Played` episodes that were never written
  to Mediatheca despite a sync running today (`jellyfin_last_sync` =
  2026-05-27T10:52Z):
  - **The Boys** S5E5, S5E6 — `LastPlayedDate` 2026-05-26
  - **Gen V** S2E4, S2E5, S2E6, S2E8 — `LastPlayedDate` 2026-05-21

  The last `Episode_watched` event in the store is from 2026-05-13. Everything
  played in Jellyfin after that date is missing.

- **All Mediatheca-side preconditions for writing those episodes are met:**
  - `series_detail` matches both by TMDB id (`the-boys-2019` / 76479,
    `gen-v-2023` / 205715).
  - The season/episode structure exists: The Boys has S1–S5 × 8 episodes;
    Gen V has S1–S2 × 8 episodes. So S5E5/E6 and S2E4–E8 are real rows.
  - They are **not** already in `series_episode_progress` (The Boys progress
    stops at S5E4; Gen V at S2E3), so the dedup would not skip them.
  - `Series.decide` for `Mark_episode_watched` (`Series.fs:455`) has **no
    episode-existence guard** — it writes `Episode_watched` as long as the
    rewatch session exists. The `default` session exists for both series.
  - `JellyfinStore` is fully populated (37 movie / 25 series / 251 episode
    rows), so the import got past series Phase 1b — the abort, if any, is in
    series Phase 2 (the watch-history write loop) or `executeCommand` is
    throwing per-item.

  → The steady-state logic *would* write these episodes. So a running import
  must be **aborting or erroring partway through series Phase 2**, and the
  failure is completely invisible.

## What

The reason the breakage is invisible — and the highest-value thing to fix
first, because it will reveal the real abort point — is the sync's total lack
of error surfacing:

1. **`runJellyfinImport` swallows failures.** It collects per-item and fetch
   errors into `result.Errors` but still returns `Ok` (`Api.fs:724-973`). An
   exception in the series Phase 2 loop (e.g. a `SqliteException` from
   `executeCommand`, or a decode/HTTP error on one series) aborts the rest of
   the import — and the result the caller sees is indistinguishable from
   "nothing to sync."
2. **`JellyfinSync` hides the outcome.** It persists `jellyfin_last_sync`
   regardless of success/failure and keeps `lastSyncResult` only in memory
   (`JellyfinSync.fs:74-92`), lost on restart. So the timestamp keeps
   advancing while nothing is written, and there is no persisted trail to
   diagnose from.

Secondary (latent, not today's trigger): there is **no re-authentication** if
the token is ever rejected — `jellyfin_access_token` is written only by
`testJellyfinConnection` (`Api.fs:3944`); auto-sync just reads it
(`Program.fs:123`). Worth hardening, but the token is currently valid, so this
is not the cause and should not be the focus.

## Acceptance criteria

- [ ] Sync outcome is observable: the last sync **result** (counts + error
      list, not just the timestamp) is persisted and survives a server restart,
      and a wholesale or partial failure is reachable as
      `JellyfinSyncStatus.SyncFailed` rather than a silent `Ok`/zero-count.
- [ ] With observability in place, reproduce against the live server and
      capture the actual error/abort point that prevents
      The Boys S5E5–E6 and Gen V S2E4–E8 from being written.
- [ ] Fix that revealed root cause so the import completes the series Phase 2
      loop even if one item fails (per-item errors must not abort the whole
      run).
- [ ] After the fix, the known-missing episodes (The Boys S5E5–E6, Gen V
      S2E4–E8, plus anything newer) appear in Mediatheca on the next sync —
      verified end to end against the live server.
- [ ] `npm run build` clean and `npm test` green; a regression test covers
      "one series in the batch raises during Phase 2 → other series still get
      their episodes written, and the run reports a failure" against stubbed
      Jellyfin + an injected fault.

## Notes

- **Diagnosis is done; this is now an observability-then-fix task, not a
  whodunit.** The remaining unknown is *which* line in series Phase 2 throws —
  add the error surfacing first, then the server's `[JellyfinSync]` log /
  persisted error list will name it.
- Relevant code: `src/Server/Api.fs` `runJellyfinImport` (~724-973, series
  Phase 2 at ~911-960), `src/Server/JellyfinSync.fs` (~74-92),
  `src/Server/Series.fs` `decide` `Mark_episode_watched` (~455),
  `src/Server/SeriesProjection.fs` `getDefaultRewatchId` /
  `getWatchedEpisodesForSession` (~1087-1101).
- Candidate abort points to check once errors are visible: a `SqliteException`
  (DB busy/locked) from `executeCommand` mid-loop; a Thoth decode error on one
  series' episodes; or an unhandled throw in the per-series `getEpisodes` /
  command path. Phase 1b completing (JellyfinStore populated) but Phase 2
  writing nothing is the key clue.
- Archived prior art (`.workflow.archived/tasks/done/`): `037-jellyfin-auto-sync`
  (built the on-visit background sync + last-synced display) and
  `017-jellyfin-play-button` (original integration + TMDB-id matching).
- Doc drift to fix opportunistically: the Integration README/index call
  Jellyfin sync a `ScheduledJobs` job, but `Program.fs` only schedules Steam
  playtime + Series TMDB refresh — Jellyfin is client-init triggered with a
  5-min cooldown.
- Latent robustness follow-up (separate, lower priority): re-authenticate with
  stored username/password and retry once on a 401/403 during sync.
- Surfacing the failure in the Settings UI (beyond persisting it) is a frontend
  follow-up and must `depends_on` the design-system styleguide task; split it
  out if it grows.

## Outcome

Observability-first, then a structural fault-isolation fix. See ADR
[0010](../../../knowledge/decisions/0010-jellyfin-sync-observability-fault-isolation.md).

**What changed:**
- **Persisted last result (criterion 1).** `JellyfinSync.fs` now persists the
  last sync result (counts + error list, or failure message) to a
  `jellyfin_last_sync_result` setting as JSON (Thoth) and reads it back in
  `initialize`, so a breakage survives a restart and is reachable as
  `JellyfinSyncStatus.SyncFailed`. `runJellyfinImport` (`Api.fs`) now returns
  `Error` (counts folded into the message) when ANY per-item error occurred,
  so partial failure surfaces as `SyncFailed` instead of a silent `Ok`.
- **Fault isolation (criteria 3 & 5).** The series Phase 2 write loop was
  extracted into a new pure, injectable module `JellyfinImport.fs`
  (`syncSeriesWatchHistory`) that wraps each series and each episode write in
  its own `try/with`. A throw on one series/episode (e.g. a `SqliteException`
  escaping `executeCommand` — which has no internal try/with) is now recorded
  into the error list and the loop continues, instead of aborting the whole
  run via the single top-level `try/with`. `runJellyfinImport` fetches episodes
  into a batch first (per-series fetch errors isolated), then delegates the
  write loop to the new module.
- **Regression test (criterion 5).** `tests/Server.Tests/JellyfinImportTests.fs`
  covers: fault in one series does not abort the others + run reports failure;
  per-item `Error` recorded without aborting; correct skip of already-watched /
  unplayed episodes; happy path. `npm test` green (259 tests), `npm run build`
  clean.
- **Doc drift fixed** in the Integration README: clarified Jellyfin sync is
  client-init triggered (5-min cooldown in `JellyfinSync.fs`), NOT a
  `ScheduledJobs` job.

**PENDING — live end-to-end verification (criteria 2 & 4):** The worker had no
Jellyfin credentials and no local DB available, so reproduction against the
live server and confirmation that The Boys S5E5–E6 / Gen V S2E4–E8 appear after
a sync could NOT be performed. The structural fix targets the diagnosed failure
mode (Phase 2 abort) and is fully unit-tested; with the new observability the
persisted `jellyfin_last_sync_result` will now name the exact failing item on
the next live run. Live verification is deferred to whoever has server access.

**Key files:**
- `src/Server/JellyfinImport.fs` (new) — fault-isolating series watch-history loop
- `src/Server/Api.fs` — `runJellyfinImport` Phase 2 rewritten to delegate; partial failure now returns Error
- `src/Server/JellyfinSync.fs` — persist + restore last sync result
- `src/Server/Server.fsproj`, `tests/Server.Tests/Server.Tests.fsproj` — compile order
- `tests/Server.Tests/JellyfinImportTests.fs` (new) — regression tests

**Follow-ups created:** `integration-002` (re-auth on 401/403),
`integration-003` (surface failure in Settings UI, depends_on design-system-001).
