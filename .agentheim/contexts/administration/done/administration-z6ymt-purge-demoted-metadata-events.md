---
id: administration-z6ymt
title: Purge the 11 demoted metadata event types from the event log via the ADR-0038 wipe-first import — offline type-level NDJSON filter plus operator-executed runbook (ADR-0056) — and retire the completed games-h4mrd play-session migration machinery in the same change
status: done
type: chore
context: administration
created: 2026-08-01
completed: 2026-08-04
depends_on: [games-v4nqe, series-r2xhv, games-h4mrd]
blocks: []
tags: [event-log, migration, cleanup, ndjson]
related_adrs: [0029, 0034, 0038, 0043, 0050, 0052, 0055, 0056, 0058]
related_research: []
prior_art: [administration-n8kqw, administration-vrc56, administration-wwc36]
---

## Why

Once `games-v4nqe` landed, eleven Game event types are fully inert — codecs still exist (so
historical rows still decode and the unknown-event report stays clean), but every `evolve` arm is a
no-op, every projection arm is gone, and no surviving command can re-emit them:

`Game_categorized`, `Game_hltb_hours_set`, `Game_description_set`, `Game_short_description_set`,
`Game_website_url_set`, `Game_play_mode_added`, `Game_play_mode_removed`,
`Game_steam_last_played_set`, `Game_store_added`, `Game_store_removed`, `Game_play_time_set`.

The ~7,668 `Game_play_mode_added` rows alone are the bulk of the log. Deleting these rows changes
nothing about replay — proven today by `GamesTests.fs` (`demotedEventsAreNoOpsTests`) and
`GameFacetProjectionTests.fs` (`demotedEventsReplayTests`); this task's fixture criteria extend that
proof to the filter tool itself.

`Game_categorized` purging does **not** conflict with ADR-0055 — 0055 protects
`game_list.genres`/`game_detail.genres`, which are sourced exclusively from
`Game_added_to_library`'s payload, never from `Game_categorized` (provably state-neutral).

`Game_play_time_set` (204 rows) is included **because the builder decided (2026-08-04) to retire the
games-h4mrd migration machinery in the same change**: that migration ran to completion in production
on 2026-08-02, is marker-gated inert, and is the only remaining reader of those events
(`Administration.readCumulativePlayTimeEvents`). Purging the events while deleting the machinery
keeps the codebase honest — no route left wired whose re-run would silently no-op.

Two premises from the original 2026-08-01 capture are corrected:

- **The "~1000 duplicate external-identity events" premise is false** — disproven by `games-w4tzc`:
  `Game_steam_app_id_set` is exactly 1:1 with streams, `Game_family_owner_added` counts are
  legitimate multi-owner families, `Game_steam_library_date_set` has 1 excess row total. All are
  load-bearing (identity-card clause, ADR-0043); none are purged.
- **`Series_refreshed` is still a fully live event type** post-`series-r2xhv` (still emitted by
  `SeriesRefresh.fs`, still read by `Series.evolve` and `SeriesProjection`). Only its ~566 historical
  no-change rows are inert, and dropping those needs a payload-level predicate, not a type filter —
  **deferred out of this task at the builder's direction (2026-08-04)**; see Notes.

## What

Mechanism unchanged: ADR-0038's wipe-first import — export NDJSON → filter offline → wipe-import,
`VACUUM INTO` backup first, checkpoints reset.

**Execution shape (ADR-0056): worker deliverables + builder-executed live step, never an automated
boot routine.** The worker ships:

1. **An offline NDJSON filter** — a pure, line-level function (parse only the `eventType` field;
   kept lines pass through byte-identical, never re-serialized — ADR-0029's byte-stability
   discipline) applying a deny-list of exactly the 11 enumerated types, with an executable
   post-condition: every dropped line's type ∈ the 11-type set, and `kept + dropped = input`. The
   worker chooses the invocation mechanism (fsx script, console entry point, or similar) — it must
   be runnable by the builder on a laptop against an exported file, and the runbook documents it.
2. **Fixture-backed tests** in the ADR-0029 `EventStoreNdjsonTests.fs` shape (`StringReader`/
   `StringWriter`, no HTTP) — see Acceptance criteria.
3. **The h4mrd retirement**: delete `src/Server/PlaySessionMigration.fs`, both
   `/api/stream/migrate-play-sessions` routes (preview + apply), the
   `AdminGuards.PlaySessionMigrationInProgress` key and its mutual-exclusion arms,
   `Administration.readCumulativePlayTimeEvents`, `computeMigrationPlanAndApplicability` and its
   callers, and their test files — following the `StartupCutover.fs` retirement precedent
   (ADR-0052). Codecs and `evolve` no-op arms for the 11 purged types are **retained**, not deleted.
4. **A runbook** at `docs/runbooks/purge-demoted-metadata-events.md` whose steps map 1:1 to the
   Settings UI flow (Projections tab, Backup section: export → run filter → wipe-import confirm →
   Rebuild-all → drift check).

The **builder** then executes the live purge by hand per the runbook. Workers never touch the live
database.

**Excluded from the purge set — never dropped, regardless of duplication in any fixture:**
`Game_rawg_id_set`, `Game_steam_app_id_set`, `Game_family_owner_added`, `Game_family_owner_removed`,
`Game_steam_library_date_set` (identity-card clause, ADR-0043), `Series_refreshed` (live type,
filter deferred), and all of h4mrd's reconstructed history (`Play_session_recorded`,
`Prior_play_time_recorded`, `Steam_observed_total_reconciled` — load-bearing, ADR-0050).

**Out of scope, dropped entirely:** the `GameAddedData` payload scrub
(`Description`/`ShortDescription`/`WebsiteUrl`). `decodeGameAddedData` declares those fields
`Required` — a key-removing rewrite makes the creation event undeserializable and the game silently
vanishes from replay. It buys zero row reduction for a materially higher risk class. If ever wanted,
it needs its own task with a decoder-tolerance ordering constraint and its own ADR.

## Acceptance criteria

- [x] Filter fixture test: given an NDJSON fixture mixing all 11 purge-eligible types with all
      excluded types (including deliberately duplicated `Game_steam_app_id_set` /
      `Game_family_owner_added` / `Game_steam_library_date_set` instances), the filter drops 100% of
      purge-eligible lines, retains every other line byte-identical, and `kept + dropped = input`.
- [x] Fixture: `Game_categorized` rows are dropped AND a full projection replay before/after shows
      `game_list.genres`/`game_detail.genres` unchanged — guards the ADR-0055 boundary.
- [x] Fixture: none of the five identity-card types is ever dropped, even when duplicated — guards
      against reintroducing the disproven "duplicate identity events" premise.
- [x] Fixture: `Series_refreshed` lines are never dropped — the deferred no-change filter must not
      partially ship.
- [x] Fixture: `Play_session_recorded` / `Prior_play_time_recorded` /
      `Steam_observed_total_reconciled` lines are never dropped.
- [x] Replay-determinism fixture: a fixture store containing ≥1 instance of each of the 11 types
      interleaved with live events; full projection rebuild before and after filter + re-import
      yields row-identical projections (drift-check-style diff, 0 discrepancies).
- [x] h4mrd retirement: the files/routes/guard listed in What §3 are deleted; `git grep
      migrate-play-sessions` and `git grep readCumulativePlayTimeEvents` return nothing; Steam sync
      still dispatches on a post-purge store (the `syncGateOpen` gate stays open — existing test
      adapted, not deleted); `npm test` and `npm run build` pass.
- [x] Runbook committed at `docs/runbooks/purge-demoted-metadata-events.md`, steps mapping 1:1 to
      the Settings UI flow, including the position-gap note (gaps in
      `global_position`/`stream_position` are expected and need no renumbering, per ADR-0038).
- [ ] Builder runs the drift check and confirms 0 discrepancies BEFORE starting the live purge
      (clean baseline). [human-eye]
- [ ] Before confirming the wipe-import, builder verifies the confirm modal's incoming-side line
      count equals the filter's reported kept-count, and the discard-side count equals the export's
      recorded line count (write-gap guard — events appended between export and confirm would be
      silently discarded). Mismatch → cancel (model-only, nothing sent) and re-export. [human-eye]
- [ ] Post-purge, builder runs Rebuild-all (mandatory, not optional — wipe-import resets checkpoints
      to 0 without dropping tables, and accumulator-style projection arms would double-count under
      incremental catch-up), then a second drift check confirming 0 discrepancies, and records the
      actual before/after event counts in this task's Notes — replacing the stale estimate.
      [human-eye]

## Notes

- **Deferral lifted 2026-08-04** at the builder's direction: the production cutover ran COMPLETE
  2026-08-03 (drift 0/7), so the deployed live version can now take the migration. The original
  "BACKLOG ONLY" marker is void.
- **Deferred follow-up (builder decision 2026-08-04):** the ~566 no-change `Series_refreshed` rows
  (`newStatus: null`). ~5% of the reduction, needs a payload-level predicate where a false positive
  silently reverts a real airing-status transition on rebuild. Capture separately if it ever
  matters.
- The stale expected-reduction figure (17,638 → ~7,500) predates `games-v4nqe`; do not carry it
  forward — the real numbers come from the export at execution time. The standing advisory at
  `protocol.md` (2026-08-03) flagging this task's stale premises is resolved by this refinement.
- The `play_session_migration_completed` settings marker survives the wipe (settings is an
  `Imperative` table, never wiped by the event-log import) — the worker decides whether the marker
  and the `syncGateOpen` check simplify away with the machinery, as long as the sync-dispatch
  criterion above holds.
- ADR-0056 (written during this refinement) records why this is operator-executed rather than a
  `StartupCutover.fs`-style boot routine: the dangerous failure is a semantically wrong filter only
  a human comparing preview counts catches, and retry-next-boot is incompatible with backup-restore
  as the recovery path.
- **Worker note (2026-08-04):** `StartupCutover.fs`'s `playSessionPhase` had a hard compile-time
  dependency on `Administration.previewPlaySessionMigration`/`runPlaySessionMigration` this task's
  "StartupCutover.fs is NOT in your scope" note hadn't anticipated. Resolved by reducing that one
  phase to a guard (verifies `Game_play_time_set` can no longer occur, since `games-v4nqe` demoted
  its only writer) rather than deleting it — the narrowest edit that keeps the build green without
  doing a full `StartupCutover.fs` retirement, which stays out of scope. See ADR-0058.
- **[human-eye] steps remain for the builder**, per ADR-0056/the Rules block: run the drift check for
  a clean baseline, execute export → filter → wipe-import (verifying the confirm modal's line counts
  against the export/filter output before confirming) → Rebuild-all → a second drift check, and
  record the actual before/after event counts here, replacing the stale pre-`games-v4nqe` estimate in
  the Why section above. See `docs/runbooks/purge-demoted-metadata-events.md` for the full procedure.

## Outcome

Worker-side deliverables shipped; the live purge itself is the builder's post-merge step (ADR-0056).

- **Offline filter**: `src/Server/EventLogFilter.fs` — `EventLogFilter.filterNdjson` (pure,
  `TextReader`/`TextWriter`, no `SqliteConnection`) plus `EventLogFilter.purgeEligibleEventTypes` (the
  11-type deny-list) and `EventLogFilter.runCli`. Invoked via `dotnet run --project src/Server --
  filter-demoted-events <in> <out>`, dispatched from a new branch at the top of `Program.fs`'s `main`
  before the Giraffe host starts — verified end-to-end against a hand-built sample file.
- **Fixtures/tests**: `tests/Server.Tests/EventLogFilterTests.fs` — 8 tests covering the full-mix
  drop/retain/byte-identity criterion, the identity-card/`Series_refreshed`/h4mrd never-dropped
  criteria, blank/unparseable-line handling, the `Game_categorized`-genres-unchanged fixture (ADR-0055
  boundary), and the replay-determinism fixture (`GameProjection.getAll`/`getBySlug` row-identical
  before vs. after a real export → filter → import → Rebuild-all round trip, 0 discrepancies).
- **h4mrd retirement**: deleted `src/Server/PlaySessionMigration.fs`,
  `tests/Server.Tests/PlaySessionMigrationTests.fs`,
  `tests/Server.Tests/AdminPlaySessionMigrationTests.fs`; removed both
  `/api/stream/migrate-play-sessions*` routes from `Composition.fs`; removed
  `AdminGuards.PlaySessionMigrationInProgress` and every mutual-exclusion arm referencing it in
  `Administration.fs` (`decideAndClaimWipeImportGuard`, `decideAndClaimRebuildGuard`, both SSE
  handlers' match arms); removed `previewPlaySessionMigration`/`runPlaySessionMigration`/
  `computeMigrationPlanAndApplicability`/`readCumulativePlayTimeEvents` and the whole play-session
  migration section. `PlaytimeTracker.syncGateOpen`/`migrationCompletedSettingKey` were left as-is
  (already correct post-purge: `hasLegacyPlayTimeEvents` becomes permanently false, so the gate opens
  unconditionally) — `PlaytimeTrackerTests.fs`'s existing pure `syncGateOpen` tests cover this and were
  neither deleted nor needed adapting. `StartupCutover.fs`'s `playSessionPhase` was reduced to a guard
  (see the Worker note above and ADR-0058) since it had a direct compile dependency on the deleted
  functions.
- **Runbook**: `docs/runbooks/purge-demoted-metadata-events.md`, steps mapping 1:1 to the Settings UI
  Backup section, including the position-gap note.
- **ADRs**: `0058-offline-filter-cli-and-startup-cutover-forced-edit.md`.
- **BC README**: updated with a new bullet on the offline filter, the h4mrd retirement, and the
  `StartupCutover.fs` forced edit.
- **Tests**: 609/609 passing (`npm test`); `npm run build` passes (Fable compile gate, client
  untouched by this task as expected).
