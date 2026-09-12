---
id: games-h4mrd
title: Reconstruct play-session history from the 204 cumulative Game_play_time_set totals — each stream's first observation becoming prior playtime rather than a fabricated session — via an operator-triggered SSE migration
status: done
type: chore
context: games
created: 2026-08-01
completed: 2026-08-02
depends_on: [games-p6vkz]
blocks: []
tags: [games, play-session, prior-play-time, migration, steam, journal]
related_adrs: [0025, 0026, 0029, 0032, 0034, 0035, 0050]
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

- [x] `SELECT COUNT(*) FROM game_play_session WHERE minutes_played <= 0` returns 0.
- [x] Reconstruction-only games: `prior_play_time + Σ session minutes = t_last(slug) + Σ|negative deltas|`, **and** the count of such streams with a non-zero correction term is 0 — so the check reduces to plain equality.
- [x] Reconstruction-only games: exactly one `Prior_play_time_recorded` per stream whose `t₀ > 0`, and its minutes equal that stream's earliest `Game_play_time_set` total.
- [x] Table-covered games: `Σ session minutes = Σ pre-migration table minutes` **and** `= t_last(slug)`, with **zero** `Prior_play_time_recorded` events emitted for them.
- [x] Global: for every game, `game_list.total_play_time` = `game_detail.total_play_time` = `game_detail.prior_play_time + Σ game_play_session.minutes_played` = `Games.reconstitute(stream).TotalPlayTimeMinutes`.
- [x] **Cursor conservation: for every game with a `steam_playtime_snapshot` row, `Games.reconstitute(stream).SteamObservedMinutes` equals that row's `total_minutes` after migration.**
- [x] **Expecto: replaying the Grounded fixture (509→570→…→2952→2282) then dispatching `Record_steam_observed_total 2952` emits zero events** — the phantom-session regression, end to end through the migration.
- [x] Row-count conservation: post-migration `game_play_session` count = `plan.ExpectedRowCount`; `COUNT(*) WHERE event_type LIKE 'Play_session_%' OR event_type = 'Prior_play_time_recorded'` = `plan.Events.Length`; no stream received events unless it previously had ≥1 `Game_play_time_set` or ≥1 table row.
- [x] Every reconstructed session `date` parses as `yyyy-MM-dd`, is ≤ today, and is ≥ `day(t₀)` for its stream.
- [x] `SELECT COUNT(*) FROM game_play_session WHERE source NOT IN ('steam','manual')` returns 0 — no `Imported` case exists.
- [x] **`checkProjectionDrift` returns an empty discrepancy list for `PlaySessionProjection` and `GameProjection` after Rebuild-all.**
- [x] A second run appends 0 events, changes 0 rows, and leaves `getMaxGlobalPosition` unchanged.
- [x] Expecto: with legacy `Game_play_time_set` events present and no completion marker, the sync gate refuses; after a successful migration run the marker exists and the gate opens — the deploy-window race, end to end.
- [x] `tests/Server.Tests/PlaySessionMigrationTests.fs` exercises `plan` as a pure function, including a fixture reproducing Grounded's full 509→570→…→2952→2282 sequence alongside its 8-row table slice and its snapshot row.
- [x] `npm test` passes; `npm run build` passes.

## Notes

**ADR:** *"Play-session history reconstructed from cumulative totals; the first observation is prior
playtime"*, `scope: games`. Reasonable fold: merge into `games-p6vkz`'s ADR.

The ADR should state the property that makes this migration honest: it introduces **no invented
dates**. Every date it writes came from an event timestamp or a table row; the one quantity whose date
is genuinely unknown — the pre-tracking lump — is recorded as a fact that carries no date at all.

## Outcome

Delivered as specified. `src/Server/PlaySessionMigration.fs` (new, pure, compiled after
`PlaytimeTracker.fs`/`Sse.fs` and before `Administration.fs`) implements
`plan : (streamId * (totalMinutes * DateTimeOffset) list) list -> Map<string, TableRow list> ->
Map<string, int> -> int -> MigrationPlan` exactly per the task's signature: table-covered slugs win
outright (all-or-nothing, integrity-gated on `Σ table rows = t_last`), reconstruction-only slugs get
one dateless `Prior_play_time_recorded t0` (when `t0 > 0`) plus one `Play_session_recorded` per
positive subsequent delta (negative/zero deltas counted, not adjusted), and either path emits a
trailing `Steam_observed_total_reconciled` when a `steam_playtime_snapshot` row disagrees with the
derived observed total. No threshold logic (that's `Games.decide`'s live-sync concern, not this
migration's).

`src/Server/Administration.fs` gains the DB-touching shell: `AdminGuards.PlaySessionMigrationInProgress`
(mutually exclusive with `RebuildingProjections`/`WipeImportInProgress` in every direction — both
existing guard functions gained a `PlaySessionMigrationInFlight`/`RefusedPlaySessionMigrationInFlight`
case), `decideAndClaimPlaySessionMigrationGuard`, raw-SQL readers for the legacy (old-schema)
`game_play_session` table and the orphaned `steam_playtime_snapshot` table (neither was ever actually
`DROP`ped by games-p6vkz — only their code was deleted; this migration is what finally issues the
`DROP TABLE` for the snapshot, once its values are carried across as reconciliation events), and
`runPlaySessionMigration` (VACUUM INTO backup → plan → per-stream idempotent append, skipping any
stream already carrying a `Play_session_*`/`Prior_play_time_recorded` event → checkpoint rewind for
`GameProjection`/`PlaySessionProjection` → completion-marker write, only after every append commits).
Mounted as `POST /api/stream/migrate-play-sessions` in `Composition.fs`, alongside the existing
wipe-import/rebuild SSE routes. `PlaytimeTracker.toGamingDay`, `.getSyncHour`, and
`.migrationCompletedSettingKey` are un-privated for reuse by the migration and its executor.

Critical finding recorded in the ADR addendum: the physical `game_play_session` table is, until an
operator runs Rebuild-all, still in its OLD non-event-sourced schema (`id, game_slug, steam_app_id,
date, minutes_played, created_at`) — `CREATE TABLE IF NOT EXISTS` is a no-op against it, so the
migration must NOT run incremental projection catch-up (it would violate the old schema's `NOT NULL`
columns); it only rewinds checkpoints, leaving the actual drop+recreate to the operator's existing,
separate Rebuild-all action.

Tests: `tests/Server.Tests/PlaySessionMigrationTests.fs` (15 cases) exercises `plan` as a pure
function — prior-playtime-only-for-t0>0, negative-delta counting, table-wins-all-or-nothing,
Manual/SteamSync round-tripping, the integrity gate (pass/fail/vacuous), cursor-conservation
reconciliation on both paths, the full Grounded 509→570→…→2952→2282 fixture with its 8-row table
slice and mismatched snapshot, row-count/event-count conservation, never-touched-if-untouched, and
date format/ordering/no-`Imported`-case. `tests/Server.Tests/AdminPlaySessionMigrationTests.fs`
(7 cases) exercises the DB-touching path end to end against a real file-backed `TestDb`: the
deploy-window race (`syncGateOpen` refuses before, opens after), row/event-count conservation plus
the orphaned snapshot table's drop, the phantom-session regression through `Projection.rebuildProjection`
+ `Games.decide (Record_steam_observed_total (2952, _))` emitting zero events, `checkProjectionDrift`
reporting zero discrepancies for both projections after Rebuild-all, second-run idempotency
(0 events appended, `getMaxGlobalPosition` unchanged), the never-touched invariant, and the
three-way guard mutual exclusion. `npm test` (530 tests, up from 508) and `npm run build` both pass.

ADR-0050 gained a `## Addendum (games-h4mrd, 2026-08-02)` section per the task's requested fold,
recording the no-invented-dates property, the table-wins rule, cursor carry-over, the
never-actually-dropped snapshot table, the two-mechanism idempotency design, the
checkpoint-rewind-vs-Rebuild-all split, and the SSE transport/guard shape. No BC README changes: the
migration reuses `games-p6vkz`'s events/ubiquitous language verbatim, introducing no new domain
concept.

Key files: `src/Server/PlaySessionMigration.fs` (new), `src/Server/Administration.fs`,
`src/Server/PlaytimeTracker.fs`, `src/Server/Composition.fs`, `src/Server/Server.fsproj`,
`tests/Server.Tests/PlaySessionMigrationTests.fs` (new),
`tests/Server.Tests/AdminPlaySessionMigrationTests.fs` (new),
`tests/Server.Tests/Server.Tests.fsproj`,
`.agentheim/knowledge/decisions/0050-play-sessions-first-class-events-two-fold-cursor.md`.

## Outcome (iteration 2)

Fixed the three findings from the iteration-1 verifier note.

**Real dry-run preview (ADR-0034 guardrail 2).** Extracted the plan-computation-plus-applicability-filtering
logic that iteration 1 had inlined only inside `runPlaySessionMigration` into a shared, read-only
`Administration.computeMigrationPlanAndApplicability` (reads the legacy table/snapshot/cumulative-event
data, runs `PlaySessionMigration.plan`, filters to streams not already migrated — no backup, no append, no
mutation of any kind). Two callers now share it verbatim:
- `Administration.previewPlaySessionMigration : SqliteConnection -> PlaySessionMigrationPreview` — pure
  read.
- `Administration.runPlaySessionMigration` (unchanged behavior, refactored to call the shared helper).

Wired as a genuinely separate, explicit second call: `GET /api/stream/migrate-play-sessions/preview`
(`Administration.playSessionMigrationPreviewHandler`, plain JSON response, checks `isAnyProjectionDirty`
but claims no guard since nothing it does can mutate) is mounted in `Composition.fs` alongside the existing
`POST /api/stream/migrate-play-sessions` apply route. An operator now has a real way to inspect the plan —
cancelling after a preview leaves the store unchanged by construction, since the preview handler never opens
a transaction, never backs up, and never appends.

**Seven-field report contract.** `PlaySessionMigrationPreview` (new type) surfaces exactly what the task's
`## What` names: `StreamsToBeTouched`, `EventsToBeAppended`, `TableCoveredSlugs`, `ReconstructedSlugs`,
`PriorPlayTimeLumpCount`, `ReconciliationCount`, `NegativeDeltasSkipped`, `IntegrityFailures` — all already
computed by `PlaySessionMigration.plan`, now actually surfaced through both the preview JSON response and
(minus the two apply-only counters) the apply path's `complete` SSE frame.

**Integrity-gate refusals now visible.** The apply path's `complete` frame gained an `integrityFailures`
field (`{"slug":...,"tableTotal":...,"lastEventTotal":...}` per refused slug) alongside the existing
`backupPath`/`streamsMigrated`/`streamsSkipped`/`eventsAppended` — a refused slug is no longer invisible in
an otherwise-clean apply report. `outcome.Plan.IntegrityFailures` was already populated by iteration 1's
`plan`; only the surfacing through the SSE frame and the preview JSON was missing.

**ADR-0050 addendum and code comment corrected.** The addendum's final paragraph previously claimed a
preview existed that didn't; rewrote it to describe the actual two-route shape
(`GET .../preview` + `POST ...`) sharing one computation, and to name the fix as an iteration-2 correction
of iteration 1's inaccurate claim. The `src/Server/Administration.fs` doc comment above
`runPlaySessionMigration` (previously claiming "the dry-run preview IS this same computation" with no
preview actually wired anywhere) now correctly describes the shared helper and its two callers.

Tests added to `tests/Server.Tests/AdminPlaySessionMigrationTests.fs` (4 new cases, 11 total in that file):
preview leaves the store byte-identical (zero events appended, `getMaxGlobalPosition`/marker/event-type
counts unchanged); preview reports all seven fields against a fixture seeded with a genuine integrity
failure (a "broken" slug whose table total disagrees with its last cumulative total); apply-after-preview
(the preview's numbers match exactly what the following apply call does); an integrity failure is visible
in the apply outcome and the refused slug genuinely receives no play-session events. `npm test` — 534 tests
(up from 530), all passing. `npm run build` passes.

Key files this iteration: `src/Server/Administration.fs` (refactored plan-computation into
`computeMigrationPlanAndApplicability`; added `PlaySessionMigrationPreview`, `previewPlaySessionMigration`,
`playSessionMigrationPreviewHandler`, `integrityFailuresJson`/`slugListJson`/`previewJson`; `complete` frame
gained `integrityFailures`), `src/Server/Composition.fs` (mounted the new preview route),
`tests/Server.Tests/AdminPlaySessionMigrationTests.fs` (4 new cases),
`.agentheim/knowledge/decisions/0050-play-sessions-first-class-events-two-fold-cursor.md` (addendum
corrected).

## Verifier note (iteration 1)

REASONS:
- ADR-0034 (listed in `related_adrs`) guardrail 2 — "Preview + explicit confirm … cancelling leaves the store unchanged by construction" — is silently unimplemented, though the task's `## What` explicitly binds this migration to it ("All guardrails are existing machinery: pure dry-run preview (ADR-0034 guardrail 2 …)"). `Administration.playSessionMigrationStreamHandler` (src/Server/Administration.fs:1812-1826) has exactly one entry point that goes backup → `PlaySessionMigration.plan` → `appendToStream` with no preview event, no dry-run parameter, and no confirm step. `git grep migrate-play-sessions` finds only the route registration at src/Server/Composition.fs:397 — an operator has no way to inspect the plan without also irreversibly appending ~200 events to 157 real streams. Guardrails 1 (VACUUM INTO + throwaway-connection verify) and 3 (checkpoint rewind) are correctly implemented; only 2 is missing.
- The task's `## What` (lines 116-118) mandates a seven-item report contract ("The dry-run preview must report: streams to be touched, events to be appended, games covered by the table vs by reconstruction, prior-playtime lumps to be recorded, cursor reconciliations to be emitted, negative deltas skipped, and any slug failing the `Σ table rows = t_last` integrity gate"). `MigrationPlan` computes all seven, but nothing in production reads them. The `complete` SSE frame (src/Server/Administration.fs:1825) emits only `backupPath`/`streamsMigrated`/`streamsSkipped`/`eventsAppended`.
- Consequence, not merely cosmetic: an integrity-gate refusal is invisible to the operator. A slug failing `Σ table rows = t_last` is dropped from `StreamEvents` in `plan` and is not counted in `StreamsSkippedAlreadyMigrated` either (that figure is derived only from already-migrated streams, src/Server/Administration.fs:1770), so the run reports a clean `complete` while a game was silently skipped — contradicting the task's "refused and reported, never guessed at."
- The ADR-0050 addendum asserts a facility the diff does not contain: final paragraph — "the dry-run preview is the same pure `PlaySessionMigration.plan` computation the actual apply path uses — no separate preview code path that could diverge". There is no preview path at all, so the durable record is now inaccurate. The same claim appears in the code comment at src/Server/Administration.fs:1722.

SUGGESTED_FIX: Give the operator a real preview before the irreversible append — either a `preview` SSE frame emitted from `runPlaySessionMigration` requiring an explicit second confirmed call, or a dry-run mode on `/api/stream/migrate-play-sessions` that runs `plan` and returns without appending — and have it report all seven `MigrationPlan` fields the task names, `IntegrityFailures` included; then correct the ADR-0050 addendum's final paragraph and the src/Server/Administration.fs:1722 comment to describe what actually ships.

ITERATION_HINT: likely-fixable
