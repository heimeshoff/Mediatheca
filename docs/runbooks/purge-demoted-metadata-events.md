# Runbook: purge the 11 demoted Game metadata event types

> **Executed 2026-08-05; filter tooling removed by infrastructure-r8kqt.**
> The purge described below ran once, successfully, on 2026-08-05. The
> `EventLogFilter.fs` CLI tool and its `filter-demoted-events` entry point
> it depended on were deleted by `infrastructure-r8kqt` (2026-09-03) since
> the purge they existed for is complete and does not recur — git history
> is the escape hatch if it's ever needed again. This document stays as the
> historical record of what was purged and how.

Task: `administration-z6ymt`. Mechanism: ADR-0038's wipe-first import.
Execution shape: ADR-0056 — operator-executed by hand through the Settings
UI, never an automated boot routine. **Workers never touch the live
database** (2026-08-02 incident rule) — every step below is run by the
builder, on the real `DATA_DIR`, not by a worker or in a worktree.

## What this purges

Exactly these 11 event types, everywhere they appear in the event log
(deny-list, not a stream-scoped operation):

```
Game_categorized
Game_hltb_hours_set
Game_description_set
Game_short_description_set
Game_website_url_set
Game_play_mode_added
Game_play_mode_removed
Game_steam_last_played_set
Game_store_added
Game_store_removed
Game_play_time_set
```

Every one of these is a fully inert legacy event: `Games.evolve`'s arm for
it is a no-op, `GameProjection`'s handler arm for it is a no-op, and no
surviving command can re-emit it (all confirmed by `GamesTests.fs`'s
`demotedEventsAreNoOpsTests` and `GameFacetProjectionTests.fs`'s
`demotedEventsReplayTests`, and extended to the filter tool itself by
`EventLogFilterTests.fs`). Deleting these rows changes nothing about how
the store replays.

**Never dropped, regardless of duplication in any preview**, no matter what
the exported file contains: `Game_rawg_id_set`, `Game_steam_app_id_set`,
`Game_family_owner_added`, `Game_family_owner_removed`,
`Game_steam_library_date_set` (the identity-card clause, ADR-0043),
`Series_refreshed` (a still-fully-live event type — only its historical
no-change rows are inert, and that's a payload-level filter this task
explicitly defers, not shipped here), and h4mrd's reconstructed play-session
history: `Play_session_recorded`, `Prior_play_time_recorded`,
`Steam_observed_total_reconciled` (ADR-0050).

`EventLogFilter.purgeEligibleEventTypes` (`src/Server/EventLogFilter.fs`) is
the single source of truth for the deny-list — this document names it for
review, but the code is authoritative.

## Steps

Every step maps 1:1 to the Settings UI's Projections tab, Backup section
(`/settings`, expand "Projections").

### 0. Preconditions

- [human-eye] **Run the drift check first** ("Run check" in the Projections
  tab) and confirm **0 discrepancies** across every projection. This is the
  clean baseline the whole purge is verified against — do not proceed on a
  store already showing drift.

### 1. Export

Click **"Export events"** in the Backup section. This downloads the full
event log as NDJSON via `GET /api/stream/export-events`
(`EventStore.exportNdjson`, ADR-0029). Note the exported file's line count
— you'll need it in step 3's write-gap guard.

### 2. Filter offline

Run the offline filter tool against the exported file, on your own machine
— this step never touches `DATA_DIR` or opens a `SqliteConnection`:

```
dotnet run --project src/Server -- filter-demoted-events <exported-file> <filtered-file>
```

This is `EventLogFilter.runCli` (`src/Server/EventLogFilter.fs`), a thin
shell around the pure `EventLogFilter.filterNdjson`: it parses only each
line's `eventType` field, drops every line whose type is in the 11-type
deny-list, and passes every other line through **byte-identical** — never
reparsed, never re-serialized (ADR-0029's byte-stability discipline).

The tool prints a summary to stdout:

```
Input lines:    <N>
Kept lines:     <N - dropped>
Dropped lines:  <dropped>
Dropped by type:
  Game_categorized               <count>
  Game_hltb_hours_set            <count>
  ...
```

Sanity-check before proceeding:
- `Kept lines + Dropped lines` must equal `Input lines` (the tool guarantees
  this by construction, but eyeball it).
- Every line under "Dropped by type" must name one of the 11 types above —
  if anything else appears there, STOP; that means the deny-list in
  `EventLogFilter.fs` has drifted from this document, and the purge must not
  proceed until that's resolved.
- If the tool prints an `UnparseableLines` warning, STOP and investigate —
  an unparseable line is kept fail-safe (never silently dropped), but its
  presence means something in the export doesn't match ADR-0029's NDJSON
  shape and needs a look before you trust the rest of the file.
- Record the exported file's line count (step 1) and the filtered file's
  `Kept lines` count — both are needed for step 3's write-gap guard.

### 3. Wipe & re-import

In the Backup section, use the **"Wipe & re-import"** file input and select
the **filtered** file (not the original export). This opens the confirm
dialog (paper-overlay, ADR-0016).

- [human-eye] **Before clicking "Wipe & import"**, verify:
  - The dialog's incoming-side line count equals the filter's reported
    **Kept lines** count from step 2.
  - The dialog's discard-side count (the live store's current event count,
    fetched fresh via `getWipeImportPreview`) equals the **export's line
    count** from step 1.
  - **Mismatch → Cancel** (model-only, nothing sent) and re-export from
    step 1. A mismatch here means events were appended to the live store
    between the export (step 1) and this confirm — proceeding would
    silently discard those events forever (the write-gap guard ADR-0038
    calls out).

- Click **"Wipe & import"**. This runs `Administration.runWipeAndImport`:
  a `VACUUM INTO` backup first (autocommit, verified), then one transaction:
  delete all events → import the filtered NDJSON → rebuild the FTS index →
  rewind every registered projection's checkpoint to 0. A malformed line
  anywhere rolls back the whole wipe, not just the import.

- **Position gap note**: `deleteAllEvents` does not reset SQLite's
  `sqlite_sequence` — a subsequent append lands strictly above the
  discarded log's own max `global_position`, not necessarily
  `(new max) + 1`. Permanent gaps in `global_position`/`stream_position`
  are **expected and require no renumbering** (ADR-0034's gap-tolerance
  reasoning, reused by ADR-0038 for wipe-first import).

### 4. Rebuild all

- [human-eye] Click **"Rebuild all"** in the Projections tab. This is
  **mandatory, not optional**: wipe-import resets every checkpoint to 0
  without dropping any projection table, so incremental catch-up would
  replay the whole (now-smaller) log against tables that still hold
  pre-purge accumulator state — any accumulator-style projection arm (e.g.
  `total_play_time` addition) would double-count. A full drop+replay is the
  only correct catch-up after a wipe-import.

### 5. Verify

- [human-eye] Run the drift check again ("Run check"). Confirm **0
  discrepancies** across every projection.
- [human-eye] Record the actual before/after event counts (from step 1's
  export line count and the post-purge `getEventStoreSummary`/health-tab
  total) in `administration-z6ymt`'s task Notes, replacing the stale
  pre-`games-v4nqe` estimate that task file's Why section still carries.

## What this runbook does NOT do

- It does not purge `Series_refreshed`'s historical no-change rows — that
  needs a payload-level predicate (`newStatus: null`), deliberately deferred
  by this task at the builder's direction (2026-08-04). Capture separately
  if it ever matters.
- It does not touch the `GameAddedData` payload fields
  (`Description`/`ShortDescription`/`WebsiteUrl`) — `decodeGameAddedData`
  declares them `Required`, so a key-removing rewrite would make
  `Game_added_to_library` undeserializable and the game would silently
  vanish from replay. Out of scope entirely; see the task's "What" section.
