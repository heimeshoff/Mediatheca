---
id: games-h4mrd
title: Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — recovering per-day history for 149 games the imperative table never covered — via an operator-triggered SSE migration
status: todo
type: chore
context: games
created: 2026-08-01
completed:
depends_on: [games-p6vkz]
blocks: []
tags: [games, play-session, migration, steam, journal]
related_adrs: [0025, 0026, 0029, 0032, 0034, 0035]
related_research: []
prior_art: [integration-004, administration-n8kqw, administration-vrc56]
---

## Why

204 `Game_play_time_set` events across 157 streams encode their own deltas. Verified against the live
log: Grounded 509→570 = 61 minutes, and `game_play_session`'s 2026-02-20 row for Grounded is exactly
61 minutes. So genuine per-session history is recoverable for **149 games the table never covered** —
the table holds 42 rows across 8 games.

This writes *new* events, which is fully compatible with deferring the log purge (`administration-z6ymt`).

## What

- Pure
  `PlaySessionMigration.plan : (streamId * (totalMinutes * DateTimeOffset) list) list -> Map<string, TableRow list> -> int -> MigrationPlan`
  in `src/Server/PlaySessionMigration.fs` (after `GameProjection.fs`, before `Administration.fs`).
  The Administration handler is a thin shell — the `decideAndClaimWipeImportGuard` extraction shape.
- Decode via `Games.Serialization.deserialize`, never ad-hoc JSON (ADR-0032 discipline). Derive the
  date via `PlaytimeTracker.toGamingDay syncHour tsᵢ` — **reuse it, do not re-derive it**, or
  reconstructed and live sessions land in different buckets and the heatmap gets a visible seam at the
  migration date.

**Initial lump** → one ordinary `Play_session_recorded`, `Source = Imported`, dated at `day(ts₀)`.

- Not discarded: that would zero ~149 games' lifetime totals, the number `game_list`, the HLTB
  comparison (`GameProjection.fs:805-830`) and `getTotalPlayTime:889` all read.
- Not a distinct event type: that forces every consumer — both projections, the aggregate fold, the
  Journal queries, the drift check, `EventFormatting`, `handledEventTypes` — to learn a permanent
  second concept for a one-time import.
- Rejected alternative worth recording: dating the lump at `Game_steam_library_date_set` to spread
  lumps across years asserts *a falsehood with more precision*, and scatters large fake sessions across
  the whole heatmap instead of confining them to a handful of known-suspect first-sync days.
- **Accepted cost, stated plainly:** one day in early 2026 shows ~2952 minutes for Grounded — wrong as
  a day, correct as a total, legible because it is badged `Imported`.

**Negative deltas emit nothing, adjust nothing; they are counted and reported.** The log records only
the *net* effect, not which session was edited, so any allocation would be invented data. Nearly free
in practice: all three affected games (Grounded, Windrose, Starcom) are among the 8 the table covers —
not a coincidence, since the decreases happened *because* the user edited through the manual API.

**Table wins where it exists, all-or-nothing per game.** A slug present in the table contributes one
event per real row (`Manual` when `steam_app_id = 0`, else `SteamSync` — real observations are never
`Imported`) and its reconstruction is discarded entirely. Never mixed: those 8 games have
`Game_play_time_set` events both before and after 2026-02-20, so mixing double-counts.

Integrity gate exploiting a structural identity: `recomputeAndPublishTotal:319` publishes `SUM` over
the whole table for that slug, so `Σ table rows = t_last` **by construction**. Assert it per game; a
slug failing it is refused and reported, never guessed at.

**Operator-triggered SSE route** `/api/stream/migrate-play-sessions`, not a startup migration — it
appends ~200 irreversible events to 157 real streams. The silent-boot pattern of
`GameJournal.migrateFromContentBlocks` is right for *rebuildable* plain-table migrations and wrong
here. Guardrails, all existing machinery:

- pure dry-run preview (ADR-0034 guardrail 2 — cancelling leaves the store unchanged by construction);
- `VACUUM INTO` backup + throwaway-connection verify (guardrail 1);
- `isAnyProjectionDirty` (ADR-0025 — the migration reads the live table and must not read a stale one);
- a new mutually-exclusive `AdminGuards` key (ADR-0035);
- per-stream expected-position `appendToStream`, never the explicit-rowid path.

**Checkpoint rewind + operator-run Rebuild-all is the cutover moment**: pre-migration table rows are
read, converted to events, then the table is dropped and rebuilt purely from those events. Without it
the first drift check reports all 42 rows as `onlyInLive`.

**Idempotency, two mechanisms:** a `play_session_migration_completed` setting (the `game_journal_migrated`
idiom) *and* — the real guarantee — a per-stream refusal for any stream already containing a
`Play_session_*` event, so a crash mid-run leaves a state a re-run completes and can never double-append.

## Acceptance criteria

- [ ] `SELECT COUNT(*) FROM game_play_session WHERE minutes_played <= 0` returns 0.
- [ ] Reconstruction-only games: `Σ minutes = t_last(slug) + Σ|negative deltas|`, **and** the count of such streams with a non-zero correction term is 0 — so the check reduces to plain equality.
- [ ] Table-covered games: `Σ minutes = Σ pre-migration table minutes` **and** `= t_last(slug)`.
- [ ] Global: for every game, `game_list.total_play_time` = `game_detail.total_play_time` = `Σ game_play_session.minutes_played` = `Games.reconstitute(stream).TotalPlayTimeMinutes`.
- [ ] Row-count conservation: post-migration count = `plan.ExpectedRowCount`; `COUNT(*) WHERE event_type LIKE 'Play_session_%'` = `plan.Events.Length`; no stream received events unless it previously had ≥1 `Game_play_time_set` or ≥1 table row.
- [ ] Every reconstructed `date` parses as `yyyy-MM-dd`, is ≤ today, and is ≥ `day(ts₀)` for its stream.
- [ ] **`checkProjectionDrift` returns an empty discrepancy list for `PlaySessionProjection` after Rebuild-all.**
- [ ] A second run appends 0 events, changes 0 rows, and leaves `getMaxGlobalPosition` unchanged.
- [ ] `SELECT COUNT(*) FROM game_play_session WHERE source NOT IN ('steam','manual','imported')` returns 0; the 8 table-covered games contribute zero `imported` rows.
- [ ] `tests/Server.Tests/PlaySessionMigrationTests.fs` exercises `plan` as a pure function, including a fixture reproducing Grounded's 509→570→…→2952→2282 sequence.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"Play-session history reconstructed from cumulative totals"*, `scope: games`. Reasonable fold:
merge into `games-p6vkz`'s ADR.

The dry-run preview should report, at minimum: streams to be touched, events to be appended, games
covered by the table vs by reconstruction, negative deltas skipped, and any slug failing the
`Σ table rows = t_last` integrity gate.
