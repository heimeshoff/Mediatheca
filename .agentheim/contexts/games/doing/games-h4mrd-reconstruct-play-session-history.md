---
id: games-h4mrd
title: Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration
status: doing
type: chore
context: games
created: 2026-08-01
completed:
depends_on: [games-p6vkz]
blocks: []
tags: [games, play-session, prior-play-time, migration, steam, journal]
related_adrs: [0025, 0026, 0029, 0032, 0034, 0035]
related_research: []
prior_art: [integration-004, administration-n8kqw, administration-vrc56]
---

## Why

204 `Game_play_time_set` events across 157 streams encode their own deltas. Verified against the live
log: Grounded 509→570 = 61 minutes, and `game_play_session`'s 2026-02-20 row for Grounded is exactly
61 minutes. So genuine per-session history is recoverable for **149 games the imperative table never
covered** — the table holds only 42 rows across 8 games.

This writes *new* events, which is fully compatible with deferring the log purge
(`administration-z6ymt`).

## What

- Pure
  `PlaySessionMigration.plan : (streamId * (totalMinutes * DateTimeOffset) list) list -> Map<string, TableRow list> -> Map<string, int> -> int -> MigrationPlan`
  in `src/Server/PlaySessionMigration.fs` (after `GameProjection.fs`, before `Administration.fs`). The
  third parameter is the `steam_playtime_snapshot` totals, read before that table is dropped. The
  Administration handler is a thin shell — the `decideAndClaimWipeImportGuard` extraction shape.
- Decode via `Games.Serialization.deserialize`, never ad-hoc JSON (ADR-0032 discipline). Derive dates
  via `PlaytimeTracker.toGamingDay syncHour tsᵢ` — **reuse it, do not re-derive it**, or reconstructed
  and live sessions land in different buckets and the heatmap gets a visible seam at the migration date.

### The first observation is prior playtime, not a session

Each stream's earliest `Game_play_time_set` total (`t₀`) becomes one `Prior_play_time_recorded t₀` —
**dateless**, contributing to the total, invisible to the diary. Every subsequent positive delta
becomes a `Play_session_recorded` with `Source = SteamSync`, dated at its own event's gaming day.

This is the whole point of `games-p6vkz`'s prior-playtime event and it removes the ugliest part of the
original plan. The earlier draft dated the lump at `day(t₀)` as an ordinary session and accepted, in
writing, that *"one day in early 2026 shows ~2952 minutes for Grounded — wrong as a day, correct as a
total"*. **That cost is gone.** No fabricated day appears in the heatmap, Recently Played, or
`getPlaytimeSummary`, and no `Imported` source case is needed — every session the migration writes is a
genuinely observed delta on a genuinely known date.

Rejected alternative worth recording: dating the lump at `Game_steam_library_date_set` to spread lumps
across years asserts *a falsehood with more precision*.

### Negative deltas emit nothing, adjust nothing; they are counted and reported

The log records only the *net* effect, not which session was edited, so any allocation would be
invented data. Nearly free in practice: all three affected games (Grounded, Windrose, Starcom) are
among the 8 the table covers — not a coincidence, since the decreases happened *because* the user
edited through the manual API.

### Table wins where it exists, all-or-nothing per game

A slug present in `game_play_session` contributes one `Play_session_recorded` per real row (`Manual`
when `steam_app_id = 0`, else `SteamSync`) and its reconstruction is discarded entirely — including its
prior-playtime lump, since `Σ table rows = t_last` already accounts for everything the user considers
real. Never mixed: those 8 games have `Game_play_time_set` events both before and after 2026-02-20, so
mixing double-counts.

Integrity gate exploiting a structural identity: `recomputeAndPublishTotal:319` publishes `SUM` over the
whole table for that slug, so `Σ table rows = t_last` **by construction**. Assert it per game; a slug
failing it is refused and reported, never guessed at.

### Carrying the cursor across the cutover

`steam_playtime_snapshot` holds what **Steam reported** (12 rows); the reconstruction yields what **we
counted**. For most games these agree. Where they do not — the games whose sessions the user edited or
deleted — emit one `Steam_observed_total_reconciled snapshot.total_minutes`, which sets
`SteamObservedMinutes` **without changing `TotalPlayTimeMinutes`**.

Without this, Grounded's post-migration cursor would read 2282 against Steam's ~2952 and the very first
sync would fabricate a 670-minute session. Bounded and small: at most the 12 games that have a snapshot
row at all, and in practice only those with a negative delta in their history.

The 145 games with no snapshot row need nothing — their cursor becomes `t_last`, and the next sync's
delta is the genuine playtime since that last observation.

### Transport and guardrails

**Operator-triggered SSE route** `/api/stream/migrate-play-sessions`, not a startup migration — it
appends ~200 irreversible events to 157 real streams. The silent-boot pattern of
`GameJournal.migrateFromContentBlocks` is right for *rebuildable* plain-table migrations and wrong here.
All guardrails are existing machinery:

- pure dry-run preview (ADR-0034 guardrail 2 — cancelling leaves the store unchanged by construction);
- `VACUUM INTO` backup + throwaway-connection verify (guardrail 1);
- `isAnyProjectionDirty` (ADR-0025 — the migration reads the live table and must not read a stale one);
- a new mutually-exclusive `AdminGuards` key (ADR-0035);
- per-stream expected-position `appendToStream`, never the explicit-rowid path.

**Checkpoint rewind + operator-run Rebuild-all is the cutover moment**: pre-migration table rows are
read, converted to events, then the table is dropped and rebuilt purely from those events. Without it
the first drift check reports all 42 rows as `onlyInLive`.

**Idempotency, two mechanisms:** a `play_session_migration_completed` setting (the
`game_journal_migrated` idiom) *and* — the real guarantee — a per-stream refusal for any stream already
containing a `Play_session_*` or `Prior_play_time_recorded` event, so a crash mid-run leaves a state a
re-run completes and can never double-append.

**The completion marker is also the Steam-sync gate** (`games-p6vkz`, `syncGateOpen`): until it is
set on a store containing `Game_play_time_set` events, `runSync` refuses to dispatch. This closes the
deploy-to-migration window race in which a scheduled sync would append `Prior_play_time_recorded` to
untouched streams that this migration's per-stream refusal would then skip. Write the marker **only
after** the migration transaction commits — a marker written early would open the sync onto a
half-migrated store.

The dry-run preview must report: streams to be touched, events to be appended, games covered by the
table vs by reconstruction, prior-playtime lumps to be recorded, cursor reconciliations to be emitted,
negative deltas skipped, and any slug failing the `Σ table rows = t_last` integrity gate.

## Acceptance criteria

- [ ] `SELECT COUNT(*) FROM game_play_session WHERE minutes_played <= 0` returns 0.
- [ ] Reconstruction-only games: `prior_play_time + Σ session minutes = t_last(slug) + Σ|negative deltas|`, **and** the count of such streams with a non-zero correction term is 0 — so the check reduces to plain equality.
- [ ] Reconstruction-only games: exactly one `Prior_play_time_recorded` per stream whose `t₀ > 0`, and its minutes equal that stream's earliest `Game_play_time_set` total.
- [ ] Table-covered games: `Σ session minutes = Σ pre-migration table minutes` **and** `= t_last(slug)`, with **zero** `Prior_play_time_recorded` events emitted for them.
- [ ] Global: for every game, `game_list.total_play_time` = `game_detail.total_play_time` = `game_detail.prior_play_time + Σ game_play_session.minutes_played` = `Games.reconstitute(stream).TotalPlayTimeMinutes`.
- [ ] **Cursor conservation: for every game with a `steam_playtime_snapshot` row, `Games.reconstitute(stream).SteamObservedMinutes` equals that row's `total_minutes` after migration.**
- [ ] **Expecto: replaying the Grounded fixture (509→570→…→2952→2282) then dispatching `Record_steam_observed_total 2952` emits zero events** — the phantom-session regression, end to end through the migration.
- [ ] Row-count conservation: post-migration `game_play_session` count = `plan.ExpectedRowCount`; `COUNT(*) WHERE event_type LIKE 'Play_session_%' OR event_type = 'Prior_play_time_recorded'` = `plan.Events.Length`; no stream received events unless it previously had ≥1 `Game_play_time_set` or ≥1 table row.
- [ ] Every reconstructed session `date` parses as `yyyy-MM-dd`, is ≤ today, and is ≥ `day(t₀)` for its stream.
- [ ] `SELECT COUNT(*) FROM game_play_session WHERE source NOT IN ('steam','manual')` returns 0 — no `Imported` case exists.
- [ ] **`checkProjectionDrift` returns an empty discrepancy list for `PlaySessionProjection` and `GameProjection` after Rebuild-all.**
- [ ] A second run appends 0 events, changes 0 rows, and leaves `getMaxGlobalPosition` unchanged.
- [ ] Expecto: with legacy `Game_play_time_set` events present and no completion marker, the sync gate refuses; after a successful migration run the marker exists and the gate opens — the deploy-window race, end to end.
- [ ] `tests/Server.Tests/PlaySessionMigrationTests.fs` exercises `plan` as a pure function, including a fixture reproducing Grounded's full 509→570→…→2952→2282 sequence alongside its 8-row table slice and its snapshot row.
- [ ] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"Play-session history reconstructed from cumulative totals; the first observation is prior
playtime"*, `scope: games`. Reasonable fold: merge into `games-p6vkz`'s ADR.

The ADR should state the property that makes this migration honest: it introduces **no invented
dates**. Every date it writes came from an event timestamp or a table row; the one quantity whose date
is genuinely unknown — the pre-tracking lump — is recorded as a fact that carries no date at all.
