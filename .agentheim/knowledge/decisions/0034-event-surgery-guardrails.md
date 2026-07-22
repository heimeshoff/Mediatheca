---
id: 0034
title: Event surgery guardrails — VACUUM INTO backup on a per-request connection, FTS5 rebuild-on-mutate, checkpoint-rewind dirty signal, and stream/global-position gap tolerance
scope: administration
status: accepted
date: 2026-07-22
supersedes: []
superseded_by: []
related_tasks: [administration-wwc36]
related_research: []
---

# ADR 0034: Event surgery guardrails — VACUUM INTO backup on a per-request connection, FTS5 rebuild-on-mutate, checkpoint-rewind dirty signal, and stream/global-position gap tolerance

## Context

The compensating-event composer (administration-xjmda, ADR-0032) is the
idiomatic, safe fix for bad data in an event-sourced store: append a
corrective event, never touch history (ADR-0002). But it can't reach two
real cases: a genuinely wrong-payload event (the composer's round-trip
validation refuses a payload that doesn't deserialize, but can't repair one
that does deserialize yet is simply *wrong*), and a stranded event-type name
left by a code-side DU rename (there is no live serializer arm for the old
name to compensate through). Both require mutating the log directly — the
one thing ADR-0002's append-only model and `EventStore.createFtsIndex`'s own
design (`events_fts` has only an `AFTER INSERT` trigger, "because rows in
`events` never change or disappear") assume never happens. This ADR is the
escape hatch for those two cases, hard-gated behind `depends_on
administration-xjmda` so the safe path always ships first, and specifies the
guardrails that make raw log mutation survivable in a single-user app with
no other operator to catch a mistake.

## Decision

### Three operations, one shared three-guardrail protocol

**Edit** (`EventStore.editEventData` — `UPDATE events SET data, metadata
WHERE global_position = @gp`), **delete** (`EventStore.deleteEventRow` —
`DELETE FROM events WHERE global_position = @gp`), and **rename**
(`EventStore.renameEventTypeRows` — `UPDATE events SET event_type = @new
WHERE event_type = @old`) each run behind the identical protocol:

1. **`VACUUM INTO` backup, first, in autocommit.** `EventStore.vacuumIntoBackup
   conn backupPath` issues `VACUUM INTO @path` — a single, transactionally-
   consistent, WAL-aware snapshot of everything committed at that instant —
   on the op's own per-request connection (`use conn = factory ()`, ADR-0033),
   *before* any `conn.BeginTransaction()` (SQLite refuses `VACUUM` inside a
   transaction). ADR-0003 explicitly flagged raw `cp` as wrong for a
   WAL-mode database; `VACUUM INTO` is the one-statement replacement, and
   incidentally also yields a plain non-WAL standalone file (no `-wal`/`-shm`
   sidecars to manage for a backup). The backup is verified before any
   mutation proceeds: open it on a **throwaway**, deliberately unconfigured
   `SqliteConnection` (never `EventStore.configureConnection` — that would
   flip a plain backup file to WAL mode and spawn its own sidecars) and run
   `PRAGMA integrity_check` plus a `SELECT COUNT(*) FROM events`. Either the
   `VACUUM INTO` itself or the verify step failing aborts the whole
   operation with **no row touched**, surfaced as `SurgeryResult.BackupFailed
   reason` — a typed case, not an exception, mirroring the
   `OrphanScan`/`PurgeResult` typed-outcome idiom ADR-0025 established.
2. **Preview + explicit confirm.** `previewEventEdit`/`previewEventDelete`
   (`int64 -> Async<SurgeryEventRow option>` / `Async<SurgeryDeletePreview
   option>`) return exactly the one targeted row by `global_position`;
   delete's preview additionally carries `StreamCurrentPosition` (the
   stream's pre-delete `stream_position`) so the client can render the
   gap-consequence copy without a second round trip.
   `previewEventTypeRename` (`string -> Async<SurgeryRenamePreview>`) returns
   an exact `Count` of matching rows plus a bounded `Sample` (never the full
   set — a rename can touch thousands of rows). Every preview is a pure
   `SELECT`, so cancelling — simply never calling the commit method — leaves
   `events`/`events_fts`/`projection_checkpoints` byte-for-byte unchanged by
   construction, not by any explicit rollback logic. Confirmation is a
   paper-overlay modal (ADR-0016, `Components.ModalPanel`), the same
   component the composer's confirmation dialog and the image-purge
   confirmation use.
3. **Projections-dirty signal via checkpoint rewind, reusing ADR-0025
   verbatim.** On successful backup, `Administration.runSurgeryMutation`
   opens `conn.BeginTransaction()` and, in ONE transaction: runs the
   mutation, then (edit/delete only) `EventStore.rebuildFtsIndex conn` —
   `INSERT INTO events_fts(events_fts) VALUES ('rebuild')`, the exact idiom
   `EventStore.createFtsIndex`'s own backfill path uses — then rewinds every
   checkpoint-tracked projection's checkpoint to 0
   (`Projection.saveCheckpoint conn handler.Name 0L` per handler — the same
   net effect as a literal `UPDATE projection_checkpoints SET last_position =
   0`, but also correct for a handler that has never checkpointed).
   `Administration.isAnyProjectionDirty` (ADR-0025, unmodified) then reports
   every handler dirty (`head - 0 > 0`) until the operator reruns Rebuild-all
   (administration-qjcp4, ADR-0024); rebuild's own drop+reinit+replay-from-0
   is unaffected by the prior checkpoint value, so this purely flips the
   dirty signal with no double work. This is the exact "leave dirty, reuse
   Rebuild-all" precedent ADR-0029 set for NDJSON import.

### Order within the shared transaction is load-bearing

Mutation runs **before** the FTS rebuild, always. For delete this is the
whole point: the insert-only `events_fts_ai` trigger never covers a vanished
row, so a full `('rebuild')` must see the row already gone to correctly stop
matching it. For edit, the rebuild must see the new `data`, not the old.
Rename has no FTS step at all — FTS indexes `data`, not `event_type`.

### Delete leaves permanent gaps — no renumbering

`deleteEventRow` never renumbers `stream_position` or `global_position`.
Verified safe against every consumer of those columns: `EventStore.appendToStream`
re-reads `MAX(stream_position)` fresh via `getStreamPosition` immediately
before each append (a gap in the middle doesn't change what the next append
computes); `EventStore.getMaxGlobalPosition` is deliberately `MAX`, not
`COUNT` — exactly so a gap doesn't desynchronize the store-head/lag
computation; and the keyset (`queryEventPage`) and live-tail
(`queryEventsAfter`) cursors use strict `<`/`>` comparisons only, never
assuming a contiguous sequence.

### Concurrency: no lock, per-request connection (ADR-0033), not ADR-0030's retired gate

Each commit op (`editEvent`/`deleteEvent`/`renameEventType`) opens exactly
one `use conn = factory ()` for its entire body — backup, verify, mutation,
FTS rebuild, checkpoint rewind, all on the same connection, identical to how
`appendCompensatingEventCore` (ADR-0032) already works under ADR-0033. There
is no app-level lock, and no revival of ADR-0030's retired `requestDbLock`:
since ADR-0033 there is no shared *request-serving* connection object left
to guard — the hazard that lock closed (concurrent command creation/disposal
racing on one shared `SqliteConnection`) cannot arise when every op has its
own. Cross-connection write contention (a surgery commit racing a concurrent
`addFriend`, say) is serialized at the **file** level by WAL + `busy_timeout`
(ADR-0028's finding, generalized by ADR-0033), surfacing at worst as a
retryable busy wait, never a crash — proven directly by a test mirroring
`RequestConnectionConcurrencyTests.fs`: a surgery `editEvent` fired
concurrently with a burst of `addFriend` calls on a shared factory /
file-backed temp DB, asserting zero exceptions and that both effects land.

### Backup retention is keep-all

`backups/` — a sibling directory of the live db file, derived from `dbPath`
the same way `Composition.fs` derives `images/` from the data dir — is never
pruned by this feature (a locked builder decision from refinement: "keep-all,
never auto-prune"). `getBackupStats : unit -> Async<BackupStats>` is a plain
directory walk (`Count`, `TotalBytes`) feeding the Surgery tab's stats panel;
a fresh, collision-free filename per backup (`mediatheca-<timestamp>-<8-hex>.db`)
guarantees uniqueness even across several surgeries in the same millisecond.

### Cross-tab dirty banner, no new API method

The Admin shell (`src/Client/Pages/Admin/Views.fs`) renders a "projections
out of sync — rebuild" banner above the tab bar, visible on every tab, whose
dirty/clean state is derived purely client-side from
`AdminProjectionsModel.Stats`'s existing `Lag` field (`Lag > 0L` on any
handler) — no new server method. `Admin.State`'s `Surgery_msg` handler
additionally dispatches a `Projections` `Load` immediately after every
committed mutation (`Mutation_completed (Applied _)`), so the banner reacts
without waiting for the operator to visit the Projections tab; it clears
naturally once Rebuild-all's existing `Stats` reload (already wired for
every rebuild step and for import) reports `Lag = 0` everywhere.

## Alternatives considered

- **A dedicated backup connection, serialized by an app-level lock (the
  design as originally refined, against ADR-0030's `requestDbLock`).**
  Retired the same day administration-mz6kp landed ADR-0033: once there is
  no shared request-serving connection object, there is nothing left for a
  lock to protect, and adding one back would reintroduce exactly the
  contention ADR-0033 removed for every other request path.
- **A separate `is_dirty` flag table instead of rewinding checkpoints.**
  Rejected — `Administration.isAnyProjectionDirty` (ADR-0025) already
  computes dirty from `head - checkpoint > 0`; rewinding the checkpoint to 0
  is a pure reuse of that existing signal with zero new schema, and it's the
  literal truth (the projection genuinely no longer reflects the mutated
  log).
- **Self-triggering a rebuild from inside the surgery operation, instead of
  leaving the store dirty for the operator to rebuild explicitly.** Rejected
  for the same reason ADR-0029 rejected it for NDJSON import: reuse the
  existing Rebuild-all machinery rather than inventing a second rebuild
  trigger path; a surgery op is already a multi-step, potentially slow
  transaction, and Rebuild-all's own progress UI is the right place for the
  operator to watch a full replay happen.
- **Renumbering `stream_position`/`global_position` after a delete, to avoid
  gaps entirely.** Rejected — renumbering would rewrite every subsequent
  row's position (an unbounded-cost operation on a large stream/store) and
  would invalidate any `global_position` an operator had bookmarked (e.g. a
  Follow-mode live-tail cursor, ADR-0023) at the exact moment of the delete.
  Gap tolerance was verified safe against every real consumer instead (see
  Decision above).

## Consequences

### Positive
- Two concrete failure modes the composer structurally cannot reach — a
  wrong-but-parseable payload, and a stranded event-type name — now have a
  real fix path, without weakening ADR-0002's append-only guarantee for the
  organic, non-surgery case (the guardrails exist precisely because this
  path is exceptional).
- `VACUUM INTO` + throwaway-connection verify gives every mutation a
  provable, restorable snapshot with no dedicated backup infrastructure and
  no new connection-management pattern beyond what ADR-0033 already
  established.
- The dirty signal is exactly the one ADR-0025 already computes — zero new
  schema, zero new dirty-detection logic, one more caller of
  `Projection.saveCheckpoint ... 0L`.

### Negative / accepted tradeoffs
- Every surgery mutation is materially slower than the corresponding
  organic write (a full `VACUUM INTO` of the whole store, not just the one
  row) — acceptable because surgery is an exceptional, operator-triggered,
  infrequent action, not a hot path.
- `backups/` grows without bound over the life of the app (keep-all,
  no pruning) — an accepted tradeoff per the locked builder decision; a
  future pruning policy is out of scope here.
- `events_fts`'s `AFTER INSERT`-only trigger design (correct and sufficient
  for every other path in the codebase) now has exactly one caller — event
  surgery — responsible for manually re-syncing it; a future new mutation
  path over `events` that forgets this would silently stale the index. No
  new enforcement mechanism was added beyond the doc comments on
  `createFtsIndex`, `rebuildFtsIndex`, and this ADR.

### Neutral
- `tests/Server.Tests/TestDb.fs`'s `TempDb` now places its backing db file
  inside a private per-instance subdirectory (rather than directly under the
  shared OS temp root) and exposes that path via `.Path`, so a sibling
  directory a test derives from it (this feature's `backups/`) is scoped to
  that one `TempDb` instance and cleaned up by its `Dispose`, never shared
  across concurrently-running tests. Existing callers using only
  `.Connection`/`.Factory` are unaffected.

## References

- `src/Server/EventStore.fs` — `getEventByGlobalPosition`, `countEventsOfType`,
  `sampleEventsOfType`, `editEventData`, `deleteEventRow`,
  `renameEventTypeRows`, `rebuildFtsIndex`, `vacuumIntoBackup`.
- `src/Server/Administration.fs` — `ensureBackupsDir`, `newBackupPath`,
  `toSurgeryEventRow`, `runSurgeryMutation`, `computeBackupStats`, and the
  seven new `IAdminApi` record members (`previewEventEdit`,
  `previewEventDelete`, `previewEventTypeRename`, `editEvent`, `deleteEvent`,
  `renameEventType`, `getBackupStats`).
- `src/Shared/Shared.fs` — `SurgeryEventRow`, `SurgeryDeletePreview`,
  `SurgeryRenamePreview`, `SurgeryResult`, `BackupStats`, and the seven
  `IAdminApi` method signatures.
- `src/Client/Pages/AdminSurgery/` — the Surgery tab's Types/State/Views.
- `src/Client/Pages/Admin/Views.fs` — the cross-tab dirty banner
  (`dirtyBanner`); `src/Client/Pages/Admin/State.fs` — the `Surgery_msg`
  handler's immediate Projections-stats reload after a committed mutation.
- `tests/Server.Tests/EventSurgeryTests.fs` — `EventStore.fs`'s new
  primitives, unit-tested in isolation.
- `tests/Server.Tests/AdminSurgeryTests.fs` — the full `IAdminApi` surface,
  including the backup/verify path, preview/cancel byte-for-byte
  invariance, FTS resync, checkpoint rewind, rename, delete + Rebuild-all
  consistency, keep-all backup stats, and the concurrency proof.
- `tests/Server.Tests/TestDb.fs` — `TempDb`'s private per-instance
  subdirectory and `.Path` member.
- ADR-0002 — event sourcing/append-only baseline this feature is the
  deliberate, guarded exception to.
- ADR-0003 — SQLite/WAL baseline; the `VACUUM INTO`-not-`cp` backup
  reasoning this ADR builds on directly.
- ADR-0020 — `events_fts`'s external-content, insert-only-trigger design;
  the staleness this feature's FTS rebuild-on-mutate step closes.
- ADR-0024 — Rebuild-all, the control this feature's dirty signal defers to.
- ADR-0025 — `isAnyProjectionDirty`'s not-dirty guard, reused verbatim here.
- ADR-0029 — NDJSON import; the "leave dirty, reuse Rebuild-all" precedent.
- ADR-0032 — the compensating-event composer this feature is the escape
  hatch for; the hard `depends_on` that gates this task behind it.
- ADR-0033 — the per-request connection factory this feature's concurrency
  model is built on, retiring ADR-0030's `requestDbLock`.
- administration-wwc36 — the task that shipped this feature.
- administration-n8kqw (blocked on this task) — the wipe-first restore path,
  independently corroborated the same `events_fts` staleness finding this
  ADR documents.
