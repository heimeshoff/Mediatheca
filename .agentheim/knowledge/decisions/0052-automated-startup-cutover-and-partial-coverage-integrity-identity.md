# ADR-0052: Automated one-boot cutover, and the partial-coverage identity added to the play-session integrity gate

Date: 2026-08-03
Status: Accepted
Related: ADR-0034, ADR-0050, ADR-0051, games-h4mrd, series-d5tpn, series-t3jkv

## Context

The cutover plan (plan.md, 2026-08-02) required an operator to press eight buttons in a strict
order on the live server: drift check → Rebuild SeriesProjection → drift check → play-session
dry-run → migrate → Rebuild-all → drift check → sync. The operator asked for the whole sequence
to run unattended as part of a normal deploy — one installation step, no buttons.

Two facts made naive automation dangerous:

1. **The ADR-0051 compensating events live only in the dev copy's log.** The eleven
   status/genres compensating events series-d5tpn appended went into the *local* database. The
   production event log never received them, so the same drift (plus anything newer — the live
   nightly refresh kept imperatively writing until this deploy) re-manifests on the production
   store, and a blind rebuild would erase live's fresher values.
2. **The migrate→rebuild window is not crash-safe on its own.** `runPlaySessionMigration`
   rewinds GameProjection/PlaySessionProjection checkpoints to 0; only the subsequent
   Rebuild-all restores consistency. A crash between the two would make the next boot's
   incremental catch-up replay the whole log INTO populated tables —
   `PlaySessionProjection.mergeSession` SUMS on conflict, so every session would double.

## Decision

### 1. `StartupCutover.fs` — the plan's Phases 3–5 as a marker-gated, idempotent boot step

Runs in `Composition.buildApp`'s single-threaded startup window (after projection catch-up,
before Kestrel serves and before scheduled jobs start, so no guard interleaving is possible):

- **Backup first** (`backupIfPending`): a `VACUUM INTO` copy under `<data-dir>/backups/`,
  taken before this release's silent migrations first touch an existing store (ADR-0034-grade,
  unlike the raw file copy ADR-0051's correction note flagged). A failed backup disables the
  cutover for that boot; the app still starts.
- **Series phase**: drift check (scoped — see below) → classify SeriesProjection discrepancies →
  auto-compose compensating events for the two fixable shapes (`status` mismatch →
  `Series_refreshed`, `genres` mismatch → `Series_categorized`, both sourced from live's values,
  metadata-stamped `{"source":"startup-cutover"}` per ADR-0051's correction) → verify drift is
  zero → the one deliberate `SeriesProjection` rebuild. ANY other discrepancy shape aborts the
  cutover before the rebuild can erase live values.
- **Play-session phase**: `previewPlaySessionMigration` (logged in full) → hard gate on zero
  integrity failures → `runPlaySessionMigration` (its own second backup) → rebuild-all → final
  all-projections drift check → completion marker.
- **Crash guard**: a `startup_cutover_phase` settings marker set just before the migration and
  cleared after rebuild-all. `ensureSafeCatchUp` (which now fronts
  `Projection.startAllProjections` at boot) sees a leftover marker and rebuilds every
  projection instead of catching up, then the cutover re-runs — every step is idempotent
  (compensating events only for drift actually found, per-stream `streamAlreadyMigrated`
  refusal, drop+replay rebuilds).
- **Abort ≠ crash**: every gate failure logs `!!! CUTOVER ABORTED`, skips the rest, and lets
  the app boot on the old data with the Steam-sync gate still closed. Restart retries.

**Drift-check scoping (pre-migration only):** GameProjection is excluded because its drift is
*expected* pre-migration (`Game_play_time_set` replays as a mandatory no-op since games-p6vkz;
reconstructing those totals is the migration's whole purpose), and PlaySessionProjection is
excluded because its live table still has the legacy schema — `diffTable` reads live's columns
and would throw selecting `steam_app_id` from the new-schema shadow. Both are rebuilt and
drift-checked in the final all-projections pass.

### 2. The integrity gate accepts a second exact identity: `m0 + Σ table = t_last`

The rehearsal against a fresh production copy aborted on
`the-eternal-life-of-goldman-demo-2017`: table total 5, last event total 19. The data shape is
structurally valid, not corrupt: the game was imported carrying 14 minutes of pre-tracking
playtime (first observation `Game_play_time_set 14`, correctly no table row), then one tracked
5-minute session (table row + `Game_play_time_set 19`). games-h4mrd's strict gate
(`Σ table rows = t_last`) silently assumed the table covers the whole cumulative history; for
any game imported with prior playtime and later tracked, it can never hold.

`PlaySessionMigration.plan` now accepts exactly two identities for a table-covered slug:

- **Full coverage** — `Σ table rows = t_last`: unchanged, no lump.
- **Partial coverage** — `m0 + Σ table rows = t_last` with `m0 > 0`: emits
  `Prior_play_time_recorded m0` (the same dateless lump the reconstruction path uses) followed
  by the table's dated rows. Since `Prior_play_time_recorded` advances
  `ActiveGame.SteamObservedMinutes` too (`Games.evolve`), the snapshot reconciliation compares
  against `m0 + Σ SteamSync rows` — for goldman-demo that is 14 + 5 = 19 = snapshot, so no
  reconciliation event and no phantom session.

Anything satisfying neither identity is still refused and reported — the gate still never
guesses. `PriorPlayTimeLumpCount` is now counted off the planned events themselves so
partial-coverage lumps report honestly.

## Consequences

- A production deploy is now a single step: stop container, deploy image, start container.
  First boot runs silent migrations + the full cutover; the logs narrate every phase and end
  in either `cutover COMPLETE` or `CUTOVER ABORTED <reason>`.
- The completion marker (`startup_cutover_2026_08_completed`) makes the whole module inert
  after success; it can be deleted wholesale in a future release once the fleet (of one) has
  cut over.
- Rehearsed end-to-end against a fresh copy of the production database before deploy; the
  rehearsal's abort is what surfaced the partial-coverage case.
