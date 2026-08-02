---
id: 0051
title: SeriesProjection drift reaches zero by dropping externally-sourced columns; the lossy-rebuild guard is retired
scope: series
status: accepted
date: 2026-08-02
supersedes: [0049]
superseded_by: []
related_tasks: [series-d5tpn]
related_research: []
---

# ADR 0051: SeriesProjection drift reaches zero by dropping externally-sourced columns; the lossy-rebuild guard is retired

## Context

This is the proof step of the deterministic-rebuild workstream (ADR-0043 through ADR-0050):
`series-r2xhv` cut the TMDB refresh and Jellyfin materialization write paths over to the cache tier,
`series-q8jwc` cut every read over to compose from that cache at query time, and both left
`series_list`/`series_detail`'s own externally-sourced columns physically present but permanently
unwritten and unread — dead weight, not yet proof. Drift only reaches zero once those columns stop
existing: an ignore-list on `Administration.diffTable` would be a second hand-maintained schema
registry, exactly the species ADR-0031 rejected when it chose `PRAGMA table_info` over a
hand-maintained PK map, and it would be a mechanism for declaring this bug's recurrence acceptable.
`diffTable` stays byte-for-byte as written; it now reads zero because there is nothing left to find.

## Decision

### Columns dropped, via `ALTER TABLE ... DROP COLUMN` (idempotent `try/with`, after the metadata-cache seed)

- `series_list` drops `tmdb_rating`, `season_count`, `episode_count`, `next_up_season`,
  `next_up_episode`, `next_up_title`.
- `series_detail` drops `overview`, `tmdb_rating`, `episode_runtime`, and the vestigial `jellyfin_id`
  (owned by `JellyfinStore` since the ADR-0033 era).
- `status` stays in both tables — the narrowed `Series_refreshed` still carries every real transition
  into the log (ADR-0047), so it remains fully replayable.
- `SeriesProjection.dropDeprecatedColumns` is called from `Composition.buildApp` immediately AFTER
  `MetadataCache.seedFromProjections`, never before: the seed's `SELECT` reads these same columns off
  `series_detail` to populate `series_metadata_cache`, so dropping first would break it outright on any
  database not yet seeded. This is the one genuinely load-bearing ordering constraint in the whole
  change.

### Deviation from the task text: `backdrop_ref` is NOT dropped

The task's "What" section listed `backdrop_ref` among `series_detail`'s columns to drop. This
contradicts ADR-0048 (`series-q8jwc`), which explicitly classified `BackdropRef` as an **identity-card**
field — driven by its own explicit event (`Series_backdrop_replaced`), never something a TMDB refresh
keeps fresh, the same class as `PosterRef` (which the task never proposed dropping). The current
codebase confirms this: `SeriesProjection.handleEvent`'s `Series_backdrop_replaced` arm writes
`series_detail.backdrop_ref` directly, and `getBySlug`/`getDashboardSeriesNextUp` read it straight off
`series_detail`, never through `series_metadata_cache`. Dropping it would break both. Treated as a
drafting error in the task text (most likely a copy-paste artifact from `series_metadata_cache`'s own
`backdrop_ref` column, seeded once but never actually read by anything — a separate, harmless leftover
from `MetadataCache.fs`'s seed, out of this task's scope) rather than a live instruction; `backdrop_ref`
is kept in both tables, following the settled ADR-0048 classification.

### `SeriesProjection.dropTables` no longer drops the cache tier

Discovered mid-task, and required to satisfy the task's own acceptance criterion ("rebuilding
SeriesProjection leaves cache row counts unchanged"): the handler's `Drop` function (used by
`Projection.rebuildProjection`'s `Drop; Init; replay` cycle, and by `checkProjectionDrift`'s shadow
replay) still dropped `series_season_cache`/`series_episode_cache` — tables `series-m7fdk` reclassified
`Cache` (owned by `MetadataCache.fs`, never checkpoint-tracked). A rebuild would have silently wiped
every TMDB/Jellyfin-materialized season and episode row, recreated the tables empty (nothing in event
replay repopulates them — that write path is command-time-only since `series-r2xhv`), and permanently
lost the data — precisely the hazard the lossy-rebuild guard existed to prevent, reintroduced by an
unrelated table classification change it was never updated to track. `dropTables` now drops only the
tables `tableRegistry` actually classifies `Projected "SeriesProjection"`: `series_list`, `series_detail`,
`series_rewatch_sessions`, `series_episode_progress`.

### Three query functions retargeted, named by ADR-0048 as this task's problem

`getRecentlyAbandoned` (ADR-0048/`series-x9mfp`'s deliberately-deferred sibling of
`getRecentlyFinished`), `getCurrentlyWatchingCount`, and `getCompletionRate` all read `series_list`'s
soon-to-be-dropped `tmdb_rating`/`season_count`/`episode_count` directly. All three retarget to
`LEFT JOIN series_metadata_cache`/`series_episode_counts`/`series_next_up`, the same composition
`series-q8jwc` gave `getAll`/`getRecentlyFinished`.

### Eleven pre-existing residual discrepancies, discovered by running the real drift check against the actual dev database

The task named exactly two residual discrepancies inherited from `series-r2xhv`/ADR-0047:
`love-death-robots-2019` (status) and `silo-2023-2` (an `onlyInShadow` row). Running
`Administration.checkProjectionDrift` against the real local `mediatheca.db` (backed up first to
`~/app/mediatheca/backups/` before any write) found:

- **`silo-2023-2` no longer manifests.** Verified absent from both live and shadow `series_list`/
  `series_detail` (zero rows in either, confirmed by direct query) — the duplicate-`tmdb_id` backstop in
  `SeriesProjection.handleEvent`'s `Series_added_to_library` arm now skips it identically on both sides.
  No remove-vs-restore decision was needed; this is recorded as resolved-by-verification, not acted on.
- **`love-death-robots-2019`'s status mismatch was confirmed**: live held `Returning`, a full shadow
  replay computed `Ended`. Per ADR-0047's own framing ("a transition back to Returning happened without
  being recorded"), live's value is the more current reality — the show was un-cancelled after the event
  log's last-recorded transition, and the now-retired imperative refresh writer captured that renewal
  correctly, but no event was ever appended for it. Resolved with a `Series_refreshed { PreviousStatus =
  Some "Ended"; NewStatus = Some "Returning" }` event, converging the log (and every future replay) to
  match what live already correctly showed.
- **Ten additional `genres` column mismatches were discovered**, not named by the task: same root cause
  as the status mismatch — the same now-retired pre-`series-r2xhv` refresh code wrote
  `name`/`overview`/`poster`/`genres`/`rating` directly (ADR-0047's own Context section names this list),
  and TMDB's genre classification for a show can legitimately change over time. Live held the freshest
  TMDB-observed genre list for `a-knight-of-the-seven-kingdoms-2026`, `ahsoka-2023`,
  `cyberpunk-edgerunners-2022`, `fallout-2024`, `invincible-2021`, `spider-noir-2026`,
  `stranger-things-2016`, `the-legend-of-vox-machina-2022`,
  `the-lord-of-the-rings-the-rings-of-power-2022`, and `the-simpsons-1989`; the event log's derived value
  (whatever `Series_added_to_library` or an occasional import-time `Series_categorized` last recorded)
  had drifted from it, purely because no `Series_categorized` event was ever appended for the change (no
  live command path fires that event today — it exists in the codebase but has no caller in `Api.fs`).
  Resolved the same way: one compensating `Series_categorized <live's genre list>` event per slug.
- Achieving this task's "gate" acceptance criterion (drift zero) required fixing all eleven, not just the
  one the task named — a partial fix would have left the live drift check non-zero regardless of the
  column drop.

**Correction (iteration 2, post-verifier):** the paragraph above originally claimed these eleven events
were appended by "replicating `Administration.appendCompensatingEventCore`'s codec+append+catch-up idiom
directly." That claim was inaccurate and has been removed. The actual script called
`EventStore.appendToStream` directly with hand-built `EventData` records and never set `Metadata` at all,
so it defaulted to `"{}"` — it did **not** carry `appendCompensatingEventCore`'s
`{"source":"admin-console"}` marker (see `Administration.fs:114`). The eleven events at
`global_position` 17641-17651 are therefore **permanently indistinguishable from organic, user-driven
events** in the log: nothing about their `metadata` column marks them as admin-injected compensating
facts. This cannot be fixed retroactively — `EventStore`'s append-only design (ADR-0002) means an
already-appended event's metadata cannot be edited in place without exactly the kind of direct-table
surgery ADR-0032 exists to forbid. The gap is recorded here as a permanent, accepted fact about these
eleven events, not corrected after the fact. Future one-off compensating-event scripts against the live
database MUST go through `appendCompensatingEventCore` itself (or otherwise explicitly set
`Metadata = "{\"source\":\"admin-console\"}"`) rather than reimplementing its primitives by hand — the
"replicating the idiom" framing understated how easy it is to reproduce the codec/append/catch-up
sequence while silently dropping the one field that makes the result auditable.

### The lossy-rebuild guard is retired in full

`checkProjectionDrift` now reports zero for SeriesProjection, and (per the `dropTables` fix above)
nothing outside `SeriesProjection.fs` writes any table it owns — both halves of ADR-0049's own
retirement criterion. Its one entry (`"SeriesProjection"`) is removed from
`lossyRebuildProjections`, and since the list is then empty, the whole mechanism is deleted per
ADR-0049's instruction: `lossyRebuildProjections`, `lossyRebuildRejectionMessage`,
`allowLossyRebuildEnvVar`/`MEDIATHECA_ALLOW_LOSSY_REBUILD`, the `LossyRebuildBlocked` case of
`RebuildRejection`, its SSE-handler rejection arm, and `CinemarcoImport.fs`'s fallback-to-incremental
branch (Step 6 now unconditionally calls `Projection.rebuildProjection` for every handler). The 3
lossy-rebuild-guard tests in `ProjectionRebuildTests.fs` are replaced (not merely deleted) with tests
proving the retirement: SeriesProjection claims the single-flight rebuild guard like any other
projection, "Rebuild all" completes all six handlers with no skip, and — satisfying this task's own
acceptance criterion — rebuilding SeriesProjection leaves every cache-tier table's row count unchanged.

### Live database changes are real, not simulated

Both fixes (the 11 compensating events and the schema drop itself) were applied to the actual local
development database (`~/app/mediatheca/mediatheca.db`), backed up beforehand. This is the one-time
production migration the task ships, not a rehearsal — `npm start`/the Settings > Projections page will
show 0 discrepancies against the live data on next run, not just in the Expecto suite's in-memory
fixtures.

**Correction (iteration 2, post-verifier):** "backed up beforehand" is technically true but misleadingly
phrased — the pre-task backup was a **raw file copy** of the live database (the `.db` file plus its
`-shm`/`-wal` WAL sidecars), not `EventStore.vacuumIntoBackup`/`VACUUM INTO` (ADR-0034). ADR-0034 exists
specifically because a plain file copy of a WAL-mode SQLite database is not guaranteed consistent unless
every writer is quiesced and the WAL is fully checkpointed first — `VACUUM INTO` is the one-statement
replacement that produces a guaranteed-consistent single file with no sidecars to manage or get out of
sync. This task's live run did not go through that mechanism (it isn't wired to arbitrary maintenance
scripts, only to `Administration.runSurgeryMutation`'s own flow), so the backup, while real and usable for
manual recovery, carries a weaker consistency guarantee than the one this codebase has standardized on for
exactly this kind of live-database operation. Recorded honestly rather than implied to be ADR-0034-grade.

### The stranded-rename incident (iteration 1) and the code guard added in response

The live run above did not go cleanly. `MetadataCache.fs`'s own doc comment for `initialize` had already
identified, in prose, the ordering hazard between its `ALTER TABLE ... RENAME` (moving the real
`series_seasons`/`series_episodes` data to `series_season_cache`/`series_episode_cache`) and
`SeriesProjection.createTables`'s independent `CREATE TABLE IF NOT EXISTS` fallback for the same two
tables: "Reversing this order would let `createTables` claim the new name as an empty table *first*,
making the rename attempted here fail (target already exists) and stranding the real rows under the old
name forever." Iteration 1's out-of-band live run realized that exact hazard for real: something ran
`createTables` (or an equivalent empty-table creation) ahead of `MetadataCache.initialize`'s rename,
stranding 4624 episode rows and 370 season rows under `series_episodes`/`series_seasons`, with every
subsequent boot's rename attempt failing silently ("target already exists") and staying that way. The
conductor repaired the live database by hand (empty cache tables dropped, populated tables renamed into
place, verified 4624/370 rows restored under the new names) — that repair is not part of this ADR's scope,
only the code-level guard that must now exist so the same hazard cannot recur silently:

`MetadataCache.initialize` (`src/Server/MetadataCache.fs`) gained a `recoverStranded` step, run
unconditionally right after the rename attempts, on every call: it detects the exact stranded shape
(old-named table exists and has rows, new-named table exists and is empty) and repairs it by dropping the
empty impostor and renaming the real data into place — the same repair applied to the live database by
hand. Removing `SeriesProjection.createTables`'s independent declaration of these two tables entirely was
considered (it would eliminate the second call site outright) but reverted: a large share of
`tests/Server.Tests/` calls `SeriesProjection.handler.Init` directly, without going through
`MetadataCache.initialize` first, and depends on that fallback existing on its own — removing it would
have meant restructuring test fixtures across multiple unrelated test files for a change out of this
task's scope. `recoverStranded` therefore makes the ordering hazard survivable rather than structurally
impossible.

**Correction (iteration 3, post-verifier): the iteration-2 guard was itself fatal against the real
incident shape, and has been fixed.** `recoverStranded` repairs via `DROP TABLE <newTable>` then
`ALTER TABLE <oldTable> RENAME TO <newTable>` — but the views `series_next_up`/`series_episode_counts`
(created later in the same `initialize` function, and present on any live database that has booted once,
including the one that produced the iteration-1 incident) `SELECT FROM series_episode_cache`. SQLite
revalidates every view in the schema during `ALTER TABLE ... RENAME`: with the view still in place, the
`DROP TABLE` commits and the subsequent `RENAME` throws `error in view series_next_up: no such table:
main.series_episode_cache` (reproduced directly against SQLite 3.49.1). Because the call chain
`recoverStranded` → `initialize` → `Composition.buildApp` was entirely unguarded, this turned into a hard
startup crash with the cache table already dropped — strictly worse than the original stranded-but-inert
state, and the iteration-2 tests did not catch it because neither fixture created the views before calling
`initialize`, so neither exercised the shape that actually occurs on a live database.

The fix, all inside `recoverStranded` itself: (1) `DROP VIEW IF EXISTS series_next_up` and
`DROP VIEW IF EXISTS series_episode_counts` run before the `DROP TABLE`/`RENAME` pair —
`initialize`'s own `CREATE VIEW IF NOT EXISTS` block, later in the same function, unconditionally
recreates both, so they always exist again by the time `initialize` returns; (2) all four statements
(the two view drops, the table drop, the rename) run inside a single `conn.BeginTransaction()`, so a
mid-repair failure can never leave `newTable` dropped without `oldTable` successfully renamed into its
place — the transaction rolls back, leaving the pre-repair state (data still under `oldTable`) exactly as
it was found; (3) the whole repair is wrapped in `try/with`, and an unexpected failure is logged to
stderr (`eprintfn`) and swallowed rather than propagated, so `recoverStranded` can never be the reason
`Composition.buildApp` fails to boot. `tests/Server.Tests/MetadataCacheTests.fs`'s two stranded-rename
tests now create both views (matching `initialize`'s own DDL) before invoking `initialize`, so they
genuinely reproduce the live incident's schema shape, and assert afterward that `series_episode_counts`/
`series_next_up` return the recovered row, not just that the underlying tables do.

## Consequences

### Positive
- `series_list`/`series_detail` can no longer silently drift on the columns removed — SQLite enforces
  their absence, closing the class of bug ADR-0043 through ADR-0050 diagnosed.
- The lossy-rebuild guard, a deliberately temporary mechanism (ADR-0049's own framing), is fully deleted
  rather than left as dead, permanently-empty scaffolding.
- Eleven real data discrepancies in the user's own library are corrected, not merely papered over by the
  drift check no longer looking at the columns that exposed them.

### Negative / accepted tradeoffs
- `series_list.next_up_*`/`season_count`/`episode_count` can no longer be materialized at all — a
  projection may never read the cache tier (ADR-0045), so these are now exclusively the
  `series_next_up`/`series_episode_counts` SQL views (`series-m7fdk`): computed on read, structurally
  incapable of drifting, invisible to `PRAGMA table_info`.
- The eleven compensating events are a permanent, visible addition to the Series event log (each
  `Series_categorized`/`Series_refreshed` event is a real, replayable fact from here forward) — accepted
  as the correct event-sourced resolution (ADR-0002: the log is authoritative) rather than a one-off
  direct table edit, which ADR-0032 already rejected as an option for exactly this class of fix.
- Two known gaps from `series-q8jwc` remain open, unrelated to this task's scope: `series-t3jkv`
  (`series_metadata_cache` has no ongoing write path) and `series-x9mfp` is now resolved by this task's
  `getRecentlyAbandoned` retarget, so only `series-t3jkv` remains.
- **The eleven compensating events carry `metadata: {}`, not `{"source":"admin-console"}`** (see Decision,
  above) — a permanent, uncorrectable gap. Anyone auditing the Series event log by metadata alone cannot
  distinguish these eleven admin-injected facts from organic ones; only this ADR's slug list and the
  `global_position` range (17641-17651) identify them.
- **The pre-write live-database backup was a raw WAL file copy, not an ADR-0034 `VACUUM INTO` snapshot**
  (see Decision, above) — usable for manual recovery, but without ADR-0034's consistency guarantee.
- **The stranded-rename incident** (see Decision, above) happened once, for real, against the live
  database, and was repaired by the conductor rather than prevented. The code guard
  (`MetadataCache.initialize`'s `recoverStranded`) makes recurrence survivable, not impossible — the
  ordering hazard's root cause (two independent `CREATE TABLE IF NOT EXISTS` declarations for the same two
  tables, in two different modules) is still present in the codebase; see Alternatives, below, for why
  removing it structurally was attempted and reverted. What "survivable" concretely means, as of iteration
  3: the guard is view-safe (it drops and lets `initialize` recreate `series_next_up`/
  `series_episode_counts` around the repair, so it no longer crashes on the view revalidation SQLite
  performs during `ALTER TABLE ... RENAME`), atomic (one transaction — a mid-repair failure cannot leave
  the new-named table dropped without the old data successfully renamed into place), and non-fatal on an
  unexpected failure (logged via `eprintfn` and swallowed, never propagated to `Composition.buildApp`).
  It does not make the underlying ordering hazard impossible, and it does not retroactively fix anything —
  it only guarantees that hitting the hazard again degrades to a no-op-with-a-log-line rather than a boot
  crash or silent data loss.

## Alternatives considered

- **Ignore-list on `Administration.diffTable` for the dropped columns instead of removing them.**
  Rejected outright by the task's own framing — a second hand-maintained schema registry, and a
  mechanism for declaring recurrence of this exact bug acceptable.
- **Drop `backdrop_ref` per the task's literal text.** Rejected — contradicts ADR-0048's settled
  identity-card classification and breaks `Series_backdrop_replaced`/`getBySlug` outright; see above.
- **Leave the ten genre discrepancies for a follow-up backlog task**, fixing only the one status mismatch
  the task named. Rejected — the task's own gate acceptance criterion requires `checkProjectionDrift` to
  report zero for SeriesProjection; leaving any known discrepancy unresolved would fail that criterion
  regardless of the column drop.
- **Route the compensating-event fix through `Administration.create`'s full `IAdminApi` record** (the
  same surface the Settings UI drives). Rejected for a one-off production data fix: `create` requires
  unrelated stub dependencies (job-run recorder, image base path, scheduled jobs) that add risk and
  noise for no benefit over calling the same underlying `Series.Serialization` + `EventStore` +
  `Projection` primitives `appendCompensatingEventCore` itself composes. **In hindsight (iteration 2):**
  this rejection reasoning still stands, but the actual script that was written did not go far enough in
  the other direction either — it skipped `appendCompensatingEventCore` *and* dropped the one field
  (`Metadata = "{\"source\":\"admin-console\"}"`) that made using that function's idiom worthwhile in the
  first place. A future one-off script should call `appendCompensatingEventCore` directly if at all
  reachable, or explicitly set that metadata field if not, rather than re-deriving the "just the
  primitives" idiom from scratch.
- **Remove `SeriesProjection.createTables`'s independent `CREATE TABLE IF NOT EXISTS` declaration of
  `series_episode_cache`/`series_season_cache` entirely**, making `MetadataCache.initialize` the only
  possible creator of those tables and eliminating the stranded-rename ordering hazard structurally.
  Attempted in iteration 2 and reverted: a large share of `tests/Server.Tests/` calls
  `SeriesProjection.handler.Init` directly without going through `MetadataCache.initialize` first, and
  relies on that fallback existing on its own — nine tests broke. Restructuring those fixtures was out of
  this task's scope. `MetadataCache.initialize`'s new `recoverStranded` step (see Decision, above) makes
  the hazard survivable instead of impossible; that tradeoff is recorded here rather than left implicit.

## References

- `src/Server/SeriesProjection.fs` — `createTables`/`dropDeprecatedColumns`/`dropTables`, the
  `Series_added_to_library` INSERT statements, `getRecentlyAbandoned`/`getCurrentlyWatchingCount`/
  `getCompletionRate`.
- `src/Server/Composition.fs` — `dropDeprecatedColumns`'s call site, immediately after
  `MetadataCache.seedFromProjections`.
- `src/Server/Administration.fs` — the deleted lossy-rebuild guard (`lossyRebuildProjections`,
  `lossyRebuildRejectionMessage`, `RebuildRejection.LossyRebuildBlocked`, `decideAndClaimRebuildGuard`).
- `src/Server/CinemarcoImport.fs` — Step 6's unconditional `Projection.rebuildProjection`.
- `src/Server/MetadataCache.fs` — `initialize`'s `recoverStranded` guard against the stranded-rename
  hazard (iteration 2), made view-safe, atomic, and non-fatal-on-unexpected-failure (iteration 3).
- `tests/Server.Tests/ProjectionDriftTests.fs` — the add+refresh+Jellyfin-materialization+episode-watched
  fixture proving zero SeriesProjection discrepancies (this task's gate criterion).
- `tests/Server.Tests/ProjectionRebuildTests.fs` — the three tests replacing the retired guard's
  coverage, including the cache-row-count-unchanged proof.
- `tests/Server.Tests/MetadataCacheTests.fs` — the seed test updated to simulate a pre-drop legacy
  `series_detail` schema directly, since a fresh `SeriesProjection.handler.Init` no longer has these
  columns at all; plus (iteration 2) the two stranded-rename recovery tests proving `recoverStranded`'s
  guard, updated (iteration 3) to create `series_next_up`/`series_episode_counts` before recovery runs and
  assert both views return the recovered row afterward — genuinely reproducing the live incident's schema
  shape.
- ADR-0002 — projections as disposable, rebuildable read models; the authority-of-the-log principle the
  eleven compensating events follow, and the reason their metadata gap (see Decision, above) cannot be
  retroactively corrected.
- ADR-0031 — the shadow-replay drift detector this task's gate criterion runs through unmodified.
- ADR-0032 — the compensating-event composer (`appendCompensatingEventCore`) this task's live-data script
  should have called directly rather than re-deriving its primitives by hand (see Alternatives, above).
- ADR-0034 — `VACUUM INTO` event-surgery backup guardrail; this task's pre-write backup did not go through
  it (see Decision, above).
- ADR-0043/0044/0045/0046/0047/0048 — the full deterministic-rebuild chain this task completes.
- ADR-0049 (superseded by this ADR) — the lossy-rebuild guard retired here, per its own retirement
  criterion and explicit assignment of that retirement to `series-d5tpn`.
- `.agentheim/contexts/series/backlog/series-t3jkv-wire-series-metadata-cache-write-path.md` — the one
  remaining open gap from `series-q8jwc`.
