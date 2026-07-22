---
id: administration-wwc36
title: Event surgery — raw edit/delete/rename with auto-backup, preview, and projections-dirty flag
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-22
depends_on: [administration-xjmda, administration-qjcp4, design-system-001]
blocks: [administration-n8kqw]
tags: [admin-console, event-store, surgery, backup]
related_adrs: [0002, 0003, 0020, 0024, 0025, 0029, 0032, 0033, 0034]
related_research: []
prior_art: []
---

## Why
Single-user app, owner is the operator: sometimes the honest fix is editing the log itself — a typo'd payload, an event appended by a buggy import, an event type renamed in code that left old names stranded in the store. This must exist, but only behind guardrails that make it hard to lose data. The idiomatic append-only fix (compensating events, administration-xjmda) is the *first* safe path; this is the escape hatch for the cases append can't reach (a genuinely wrong-payload event, a stranded event-type name), and it hard-depends on xjmda so the safe path always ships first.

## What
A Surgery tab (`/admin/surgery`, a new `Router.AdminTab` variant under the existing `/admin` shell) with three operations, each behind the **same three-guardrail protocol**:

1. **Auto-backup first (`VACUUM INTO`, not file-copy).** Before any mutation, snapshot the live WAL-mode DB via a single `VACUUM INTO '<data-dir>/backups/mediatheca-<timestamp>.db'` on **the op's own per-request connection** (`use conn = factory ()`, ADR-0033 — see the **Concurrency** note under the API surface below) — the transactionally-consistent, WAL-aware, one-statement replacement for the checkpoint+copy dance (ADR-0003 explicitly warns raw `cp` is wrong here; `VACUUM INTO` also yields a plain non-WAL standalone file). No dedicated backup connection and no app-level lock: since ADR-0033 there is no shared request connection to serialize against — each op runs on a connection no other request holds, exactly like the composer's `appendCompensatingEvent`. `VACUUM INTO` runs first, in autocommit (SQLite refuses `VACUUM` inside a transaction), before any `BeginTransaction`. Then prove the backup by opening it on a **throwaway** `SqliteConnection` (a plain non-WAL file — do *not* run `configureConnection` on it, which would flip it to WAL and spawn sidecars) and running `PRAGMA integrity_check` / a `COUNT(*)`; if `VACUUM INTO` throws or the verify fails, **abort with no row touched**.
2. **Preview + confirm.** Show exactly the affected rows (edit/delete: the one targeted row by `global_position`; rename: exact count + a bounded sample of rows at the old `event_type`) and require explicit confirmation in a paper-overlay dialog (reuse `Components.ModalPanel` — the same paper-overlay modal xjmda's composer confirmation uses, ADR-0016). Cancel leaves `events`, `events_fts`, and `projection_checkpoints` byte-for-byte unchanged.
3. **Projections dirty (reuse ADR-0025 detection, no new table).** After the mutation, `UPDATE projection_checkpoints SET last_position = 0` for the six checkpoint-tracked handlers. `Administration.isAnyProjectionDirty` (ADR-0025) then reads `head - 0 > 0` for every handler → dirty; rebuild's own drop+reinit+replay-from-0 is unaffected by the prior checkpoint value, so this purely flips the signal with no double-work. A new **"projections out of sync — rebuild" banner** in the Admin shell (above the tab bar, visible on every tab; client-derived from `getProjectionStats`'s `Lag` field — no new API method) shows until Rebuild-all (administration-qjcp4/ADR-0024) clears the lag. This is the "leave dirty, reuse Rebuild-all" precedent ADR-0029 set for import.

Operations:
- **Edit** a single event's `data` / `metadata` JSON. **Must re-sync FTS:** `events_fts` is external-content FTS5 over `events.data` (ADR-0020) with only an `AFTER INSERT` trigger — an UPDATE of `data` leaves the index stale (false positives/negatives in search). Follow every edit with `INSERT INTO events_fts(events_fts) VALUES ('rebuild')` — the exact idiom `EventStore.createFtsIndex`'s own backfill path uses.
- **Delete** a single event. Leaves `stream_position` / `global_position` **gaps** — no renumbering (verified safe: `appendToStream` re-reads `MAX(stream_position)` fresh immediately before each append via `Api.executeCommand`, and keyset-pagination (ADR-0020) + live-tail (ADR-0023) cursors use `<`/`>` only, never assuming contiguity). **Also re-syncs FTS** via the same `('rebuild')` insert (the "rows disappear" case the insert-only trigger doesn't cover). Preview states the gap consequence in its copy.
- **Rename** an event type store-wide: `UPDATE events SET event_type = @new WHERE event_type = @old` — the schema-migration verb for DU renames. Reflected automatically in the explorer's event-type filter (`getDistinctEventTypes` is live `SELECT DISTINCT`, no cache) and the Health tab's type counts (also live). **No FTS action** (FTS indexes `data`, not `event_type`).

Server API surface (plain `IAdminApi` Remoting — no SSE; each op is a fast single-statement mutation, the backup is a local file op with a boolean outcome, closer to `purgeOrphanedImages` than to the streamed rebuild/export):
- Preview: `previewEventEdit` / `previewEventDelete` (`int64 -> Async<...>`, the one row) and `previewEventTypeRename` (`string -> Async<{| Count; Sample |}>`).
- Commit: `editEvent` / `deleteEvent` / `renameEventType`, each returning a shared `SurgeryResult = BackupFailed of reason | Applied of backupPath * affectedRows` DU (mirrors the existing `OrphanScan`/`PurgeResult` typed-outcome idiom — backup failure is a typed case, not an exception).
- Backup stats: `getBackupStats : unit -> Async<{| Count; TotalBytes |}>` — a directory walk over `backups/`, feeding the keep-all retention UI.

**Concurrency (ADR-0033 — per-request connection, no lock).** *(This section was refined against ADR-0030's `requestDbLock`, which ADR-0033/administration-mz6kp has since retired. It now follows the per-request-connection model.)* Each commit op (`editEvent` / `deleteEvent` / `renameEventType`) opens **exactly one** `use conn = factory ()` for its whole body — identical to how `appendCompensatingEvent`, the SSE handlers, and every `Administration.create` member now work. **No app-level lock, no replacement for `requestDbLock`.** The hazard the old gate guarded ("`SqliteConnection` does not support nested transactions") was an object-thread-safety property of the *shared* connection; with per-request connections there is no shared object to race on, so surgery's multi-step body is as safe unlocked as the composer's single append ("no lock needed, since no other request shares that connection object"). Cross-connection write contention (a surgery mutation vs. a concurrent `addFriend`) is serialized at the **file** level by WAL + `busy_timeout` (re-applied per connection by `EventStore.configureConnection`), surfacing at worst as a retryable busy wait, never a crash — the ADR-0028 finding ADR-0033 generalized.

Transaction shape within the body:
- **VACUUM INTO** (step 1) runs in autocommit, before any `BeginTransaction`.
- **Backup verify** (step 2) is a throwaway connection on the backup file — no bearing on `conn`'s transaction state.
- **Mutation → FTS `('rebuild')` → checkpoint rewind** (steps 3–5) share **one** `conn.BeginTransaction()`, committed at the end (`Rollback(); reraise()` on failure) — the exact shape `EventStore.appendToStream` uses, so a crash can never leave the mutation applied but FTS/checkpoint un-updated. Order is load-bearing: mutate first, *then* `('rebuild')`, so the full FTS rebuild sees post-mutation `events` content (for delete this is the whole point — the insert-only `events_fts_ai` trigger never covers a vanished row). `INSERT INTO events_fts(events_fts) VALUES ('rebuild')` and the `projection_checkpoints` UPDATE are ordinary DML and sit inside the mutation transaction on the same `Microsoft.Data.Sqlite` connection with no explicit `cmd.Transaction` (the connection's single active transaction is implicit), exactly as `appendToStream` issues multiple `Db.exec` calls under one `BeginTransaction`.

**Backup/mutation interleaving is intended, not a race.** Under per-request connections a foreign write can land between the VACUUM INTO snapshot (step 1) and the mutation (step 3). This is fine: `VACUUM INTO` yields a transactionally-consistent, atomic point-in-time snapshot of everything committed at the instant it ran — it cannot produce a torn file, and an append landing after it is safely committed in the live store, merely absent from *that one* backup. It can't corrupt the mutation either: edit/delete target a fixed `global_position` and any interleaved append gets a *new* AUTOINCREMENT position (no collision); rename targets an `event_type` set and an unrelated interleaved append is orthogonal. The backup's only invariant — "a consistent restore point exists, taken no later than the mutation" — holds regardless of interleaving (and on a single-user app two simultaneous writes essentially never occur).

## Acceptance criteria
- [ ] Every mutation path (edit, delete, rename) runs `VACUUM INTO` to a fresh timestamped path in the data dir before touching `events`; if `VACUUM INTO` throws or a post-backup open-and-query of the backup file fails, the operation aborts with no row touched. (Test: seed a store, trigger each op, assert the backup file opens and its event count matches the pre-mutation store.)
- [ ] Preview for edit/delete returns exactly the one targeted row by `global_position`; preview for rename returns the exact count of rows matching the old `event_type` plus a bounded sample; cancelling the dialog leaves `events`, `events_fts`, and `projection_checkpoints` byte-for-byte unchanged. (Test: diff full-table dumps before/after a cancel.)
- [ ] After edit or delete, `events_fts` is re-synced via `INSERT INTO events_fts(events_fts) VALUES ('rebuild')`: a search for the new/remaining text finds it, and a search for text present only in the pre-edit/pre-delete payload does not. (Test: edit an event's `data`, search old and new substrings via `queryEventPage`'s `Search` filter.)
- [ ] After any surgery mutation, `projection_checkpoints.last_position` is 0 for all six checkpoint-tracked handlers and `Administration.isAnyProjectionDirty` returns them all non-empty immediately after. (Direct unit test against `Administration.fs`.)
- [ ] Each commit op (edit/delete/rename) runs its entire body — `VACUUM INTO` backup → throwaway-connection verify → mutation+FTS+checkpoint transaction — on a single `use conn = factory ()`, touching no shared mutable connection (same model as `appendCompensatingEvent`, ADR-0033). A surgery commit fired concurrently with a burst of N `addFriend` calls, each on its own factory-drawn connection, completes with zero `SqliteConnection` exceptions (no nested-transaction crash, no object-level race); the surgery's mutation, FTS re-sync, and checkpoint rewind all land, and every concurrent friend is recorded. Same-file write contention is serialized by WAL + `busy_timeout`, surfacing at worst as a retryable busy wait, never a crash. (Test: mirror `RequestConnectionConcurrencyTests.fs` on `TestDb.withTempDbFactory` — drive a surgery commit concurrently with concurrent `addFriend` on the same factory / **file-backed** temp DB, not `:memory:`, so file-level WAL serialization is actually exercised; assert no exception and both effects.)
- [ ] The Admin shell renders a "projections out of sync — rebuild" banner whenever dirty state is non-empty (derived from `getProjectionStats`'s `Lag`), and it disappears once Rebuild-all completes. The dirty→clean transition is machine-checkable via `getProjectionStats` before/after Rebuild-all; the banner's visual placement / paper-overlay styling is `[human-eye]`.
- [ ] Rename updates every occurrence; `getDistinctEventTypes`/`getEventTypes` reflects the new name and never the old one afterward, with zero rows remaining at the old `event_type`. (Direct SQL assertion.)
- [ ] Deleting an event and running Rebuild-all produces projection state consistent with the edited log — the deleted event's effects are absent from every projection touching that stream, and no other stream's projection state is disturbed. (Test: seed a stream with N events, delete one mid-stream, rebuild, assert the projection matches replaying the remaining N-1 events directly.)
- [ ] The delete confirmation dialog states the stream-position-gap consequence for that stream/event. Machine-checkable that the preview payload carries the stream's current position so the client *can* render that copy; the exact wording / paper-overlay presentation is `[human-eye]`.
- [ ] Backup retention is keep-all: no backup file is ever deleted by this feature, and the Surgery UI's backup-stats panel shows cumulative count + total bytes matching an independent directory walk. (Test: trigger 3 surgeries, assert 3 backup files exist and stats match `Directory.GetFiles` sum.)

## Notes
**Builder decisions (locked during refinement 2026-07-22):** (1) one task carries all three ops — the three-guardrail protocol is the unit of work, splitting would triplicate the backup/preview/dirty scaffolding for no isolation benefit; (2) hard `depends_on administration-xjmda` so the safe append-only compensating-event path ships before raw log mutation is ever possible; (3) backup retention is **keep-all**, never auto-prune, with cumulative size + count surfaced in the Surgery UI.

**Settled against source (via orchestrator/architect, 2026-07-22):**
- Backup = `VACUUM INTO` on the op's own per-request connection (`use conn = factory ()`, ADR-0033; ADR-0003's WAL-backup caveat still applies — `VACUUM INTO`, never raw `cp`), run in autocommit before the mutation transaction, verified by re-opening the file on a throwaway connection before mutating. *(Originally refined as "on the shared connection" under ADR-0024's precedent — retired with the shared connection by ADR-0033.)*
- Dirty = rewind `projection_checkpoints.last_position` to 0, reusing `isAnyProjectionDirty` (ADR-0025) verbatim — no new flag table.
- **FTS gap (the sharp finding):** `events_fts` has only an `AFTER INSERT` trigger (`EventStore.createFtsIndex` comment: *"there is no UPDATE/DELETE trigger… because rows in events never change or disappear"*) — surgery breaks that invariant for the first time. Edit + delete must issue `('rebuild')`; rename doesn't touch FTS. **Independently corroborated by administration-n8kqw**, which found the same staleness reading the same comment.
- Delete leaves gaps, no renumber — verified against `EventStore.appendToStream` (MAX, read fresh in `Api.executeCommand`), `getMaxGlobalPosition` (MAX, deliberately gap-tolerant), and the keyset/live-tail cursors.
- The cross-tab dirty banner does **not** exist yet — this task adds it in the Admin shell (`getProjectionStats.Lag` drives it; no new API method).
- **Concurrency model reconciled to ADR-0033 (per-request connection), 2026-07-22 REFINE.** This task was originally refined against ADR-0030's `requestDbLock`; **administration-mz6kp landed ADR-0033 the same day, retiring that lock** and moving every request/op onto its own `use conn = factory ()`. There is now **no shared request connection and no lock** — each surgery commit op opens one connection for its whole body, exactly like the composer's `appendCompensatingEvent` ("no lock needed, since no other request shares that connection object"). WAL + `busy_timeout` serialize cross-connection writes at the file level (ADR-0028's finding, generalized by ADR-0033). Confirmed against source: `appendCompensatingEventCore`, `Administration.create`'s `factory : unit -> SqliteConnection` parameter, and the five SSE handlers in `src/Server/Administration.fs` all follow the `use conn = factory ()` idiom; `RequestConnectionConcurrencyTests.fs` proves 25 concurrent factory-drawn connections don't crash. See the API-surface **Concurrency** note above.
- **The safe append-only path (xjmda, ADR-0032) has shipped**, so this escape hatch's precondition ("compensating events exist first") holds — the hard `depends_on` is satisfied, not merely planned.

**ADR to write at implementation:** propose **ADR-0034 — "Event surgery guardrails: `VACUUM INTO` backup on a per-request connection, FTS5 rebuild-on-mutate, checkpoint-rewind dirty signal, and stream/global-position gap tolerance"**. ⚠️ Reserved-number history: the original refinement reserved **0030** (taken by administration-cx92m, the interim request-connection gate), then this task's first reconciliation moved to **0033** — which administration-mz6kp then took for the per-request connection factory. **0034** is the next free number as of 2026-07-22 (latest on disk is 0033) — confirm again at write time. It extends/qualifies ADR-0003 (WAL backup) and ADR-0025 (dirty detection), **integrates ADR-0033** (surgery mutations run on their own per-request `use conn = factory ()`, no lock — *not* the retired ADR-0030 `requestDbLock`), and records the delete-gap and FTS-staleness reasoning. Worker writes it — not pre-written here.

**Residual open (non-blocking, worker-resolvable):**
- Confirm `VACUUM INTO ?` accepts a bound filename parameter under `Microsoft.Data.Sqlite` 9.x (vs. requiring interpolation into the SQL text) — a two-line smoke test at implementation start, not a design question.
- *(Resolved in the 2026-07-22 ADR-0033 REFINE)* The `('rebuild')` FTS re-sync and the checkpoint rewind **share the mutation's single transaction** (steps 3–5 under one `BeginTransaction`, mutate-then-rebuild order) — matches `appendToStream`'s atomicity, so a crash can't leave the mutation applied but FTS/checkpoint un-updated. See the **Concurrency** note in `## What`.

**Dependencies now all met (2026-07-22):** administration-xjmda (safe append-only path), administration-qjcp4 (the Rebuild-all the dirty banner reuses), and design-system-001 (styleguide gate) are all in `done/`. The sequencing gate that held this in backlog is cleared — this refinement promotes it to `todo/`. (administration-n8kqw still waits on this task in turn, mirroring how this one waited on xjmda.)

## Outcome

Shipped all three guardrailed operations (edit/delete/rename) behind the
identical three-guardrail protocol (VACUUM INTO backup on a per-request
connection → preview+confirm → checkpoint-rewind dirty signal), plus the
keep-all backup-stats panel and the cross-tab "projections out of sync"
banner.

**Server (`src/Server/EventStore.fs`, `src/Server/Administration.fs`):**
- `EventStore.fs` gained the surgery primitives: `getEventByGlobalPosition`,
  `countEventsOfType`, `sampleEventsOfType`, `editEventData`,
  `deleteEventRow`, `renameEventTypeRows`, `rebuildFtsIndex`,
  `vacuumIntoBackup` (the residual-open "does `VACUUM INTO @path` accept a
  bound parameter under Microsoft.Data.Sqlite 9.x" question — yes, confirmed
  by `EventSurgeryTests.fs`).
- `Administration.fs` gained `ensureBackupsDir`/`newBackupPath`,
  `toSurgeryEventRow`, `runSurgeryMutation` (the shared commit-op body:
  backup → verify → one transaction for mutate+FTS-rebuild+checkpoint-rewind),
  `computeBackupStats`, and seven new `IAdminApi` members
  (`previewEventEdit`/`previewEventDelete`/`previewEventTypeRename`/
  `editEvent`/`deleteEvent`/`renameEventType`/`getBackupStats`), each on its
  own `use conn = factory ()` per ADR-0033 — no lock.
- `Shared.fs` gained `SurgeryEventRow`, `SurgeryDeletePreview`,
  `SurgeryRenamePreview`, `SurgeryResult` (`BackupFailed`/`Applied`),
  `BackupStats`, and the seven `IAdminApi` signatures.

**Client (`src/Client/Pages/AdminSurgery/`):** a new Types/State/Views page
wired into the existing `AdminSurgery` router tab — three operation panels
(edit/delete/rename), each with a paper-overlay confirm dialog
(`Components.ModalPanel`), plus a backup-stats panel. `src/Client/Pages/Admin/`
wires the new page in and adds a cross-tab dirty banner
(`Admin/Views.fs`'s `dirtyBanner`, client-derived from
`AdminProjectionsModel.Stats`'s `Lag`, no new API method) that reloads
immediately after a committed mutation (`Admin/State.fs`'s `Surgery_msg`
handler). Verified via `npm run build` (Fable typecheck) — the client UI
itself is `[human-eye]` per this task's acceptance criteria; a Playwright
e2e spec closing that gap is tracked as backlog item
administration-svq3t.

**Tests:** `tests/Server.Tests/EventSurgeryTests.fs` (9 tests, EventStore.fs
primitives in isolation) and `tests/Server.Tests/AdminSurgeryTests.fs` (13
tests, the full `IAdminApi` surface: backup+verify, preview/cancel
byte-for-byte invariance, FTS resync, checkpoint rewind, rename, delete +
Rebuild-all consistency against a real Movies stream, keep-all backup
stats, and the concurrency proof against a burst of `addFriend` calls).
`tests/Server.Tests/TestDb.fs`'s `TempDb` now places its db file in a
private per-instance subdirectory (exposed via a new `.Path` member) so a
sibling `backups/` directory a test derives from it is scoped to that one
fixture. Full suite: 414/414 passing (392 baseline + 22 new).

**ADR:** `.agentheim/knowledge/decisions/0034-event-surgery-guardrails.md`.

**BC README:** updated with the Event surgery bullet and the extended
`IAdminApi` method list.

**New backlog item:** administration-svq3t (Playwright e2e spec for the
Surgery tab, closing the `[human-eye]` client-UI gap with the project's
existing e2e harness, ADR-0027).
