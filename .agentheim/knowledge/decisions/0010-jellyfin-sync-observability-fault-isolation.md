---
id: 0010
title: Jellyfin sync persists its last result and isolates per-item faults
scope: integration
status: accepted
date: 2026-05-27
supersedes: []
superseded_by: []
related_tasks: [integration-001]
related_research: []
---

# ADR 0010: Jellyfin sync persists its last result and isolates per-item faults

## Context

Series episode watch state from Jellyfin silently stopped flowing into
Mediatheca around mid-May 2026 even though the sync kept running. A live
read-only diagnosis (recorded in `integration-001`) ruled out the obvious causes
(token valid, server reachable, movies fine, all Mediatheca-side write
preconditions met) and pinned the breakage to the import *execution path*:

1. `runJellyfinImport` wrapped the entire import in a single `try/with` and
   always returned `Ok`. An exception escaping the series Phase 2 write loop
   (e.g. a `SqliteException` from `executeCommand`, since `executeCommand` has no
   internal try/with) aborted the rest of the run, discarded the partial progress
   *and* the accumulated `Errors` list, and the caller could not tell "nothing to
   sync" apart from "exploded halfway".
2. `JellyfinSync` persisted only the `jellyfin_last_sync` timestamp and kept the
   result in memory, lost on restart. So the timestamp kept advancing while
   nothing was written and there was no persisted trail to diagnose from.

## Decision

Observability first, then structural fix.

- **Persist the last sync result.** `JellyfinSync` now writes the last result
  (counts + error list, or the failure message) to a `jellyfin_last_sync_result`
  setting via `SettingsStore`, encoded as JSON with Thoth, and reads it back in
  `initialize`. The result therefore survives a restart and is reachable as
  `JellyfinSyncStatus.SyncCompleted` / `SyncFailed`.
- **Partial failure is failure.** When `runJellyfinImport` accumulates any
  per-item error it now returns `Error` (with the counts folded into the message)
  instead of a silent `Ok`, so the status becomes `SyncFailed` rather than a
  misleading `SyncCompleted` with a zero-ish count.
- **Isolate per-item faults.** The series Phase 2 write loop was extracted into
  `JellyfinImport.syncSeriesWatchHistory` — a pure, injectable function that wraps
  each series and each episode write in its own `try/with`. A throw on one
  series/episode is recorded into the error list and the loop continues, so one
  bad item can no longer abort the whole batch. Episodes are fetched into a batch
  first (isolating fetch errors per series), then the write loop runs over that
  batch with an injected `writeEpisode` command executor.

## Why not just patch the suspected root cause?

The live diagnosis localized the breakage to Phase 2 but could not name the
exact throwing line without the error surfacing in place. A guessed root-cause
patch would be unverifiable. Making failure visible *and* structurally
non-fatal fixes the whole class of "one item kills the run" bugs regardless of
which line throws, which is the durable fix.

## Consequences

- `JellyfinImport.syncSeriesWatchHistory` is unit-testable without HTTP or
  SQLite (fault injection via the `writeEpisode` lambda); a regression test
  covers "one series throws -> others still written + run reports failure".
- The persisted failure message carries the error list, so a future breakage is
  diagnosable from the DB alone.
- Surfacing the failure in the Settings UI (beyond persisting it) remains a
  frontend follow-up that must `depends_on` the design-system styleguide task.
- Re-authentication on a rejected token is still not implemented; it was a latent
  hypothesis disproven as the current trigger and remains a lower-priority
  robustness follow-up.
