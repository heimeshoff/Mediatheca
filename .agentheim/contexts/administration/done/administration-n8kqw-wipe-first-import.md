---
id: administration-n8kqw
title: Event log import — wipe-first path for a non-empty store: backup, preview + confirm, then wipe and re-import in one transaction
status: done
type: feature
context: administration
created: 2026-07-22
completed: 2026-07-31
depends_on: [administration-jrflk, design-system-001]
blocks: []
tags: [admin-console, event-store, backup, import, surgery, concurrency-guard]
related_adrs: [0016, 0025, 0029, 0033, 0034, 0035, 0038]
related_research: []
prior_art: [administration-vrc56, administration-wwc36, administration-jrflk]
---

## Why
administration-vrc56 covers import into an *empty* store only and refuses otherwise. Overwriting a populated store is exactly as destructive as administration-wwc36's raw event-log surgery — it deserves the same three-guardrail protocol (auto-backup first, preview + explicit confirmation, projections-dirty flag), reused rather than reinvented.

Both original prerequisites (vrc56, wwc36) are done; the question this task actually resolves is the *shape* of the wipe-then-reimport: one atomic operation where the **transaction**, not the backup file, is the primary restore path.

## What

### Server: one new route, one transaction, one new preview query

- **Route** — `POST /api/stream/wipe-import-events`, a new raw Giraffe SSE route, sibling to (not a flag on) `/api/stream/import-events`. `importEventsStreamHandler`'s doc comment already scopes this out of itself; a separate route keeps the safe route's "refuses any non-empty store" invariant *literally* true rather than conditionally true, and keeps the harmless endpoint from acquiring the `dbPath`/`projectionHandlers` dependencies needed to destroy a store. Handler: `Administration.wipeImportEventsStreamHandler factory dbPath projectionHandlers guards`, wired in `Composition.fs` beside the existing import route.

- **`importNdjson` split** (vrc56's contract stays byte-identical):
  - `EventStore.importNdjsonRows (conn) (reader: TextReader) : Result<ImportOutcome, ImportFailure>` — today's read-line/decode/explicit-rowid-INSERT loop extracted **without** its own transaction; the caller owns commit/rollback. Public, so both callers reach it. The inline `try/with` that wraps a mid-loop exception as `MalformedLine(lineNumber, ex.Message)` is load-bearing and moves with the loop.
  - `EventStore.importNdjson` — collapses to the empty-store check, `BeginTransaction`, `importNdjsonRows`, commit/rollback. **Same signature, same semantics, same `StoreNotEmpty` behaviour.**
  - `EventStore.deleteAllEvents (conn) : int` — `DELETE FROM events` (not drop/recreate: schema, FTS trigger and shadow tables must survive), returning the row count.
  - `Administration.runWipeAndImport (conn) (dbPath) (projectionHandlers) (onBackup: string -> unit) (reader: TextReader) : WipeImportResult` — lives in `Administration.fs`, mirroring `runSurgeryMutation`'s layering (`EventStore.fs` owns storage verbs; `Administration.fs` owns the guardrail protocol that composes them, and needs `dbPath`/`projectionHandlers`, neither of which is a storage-layer concept).

- **The protocol** (`runWipeAndImport`):
  1. `EventStore.vacuumIntoBackup conn backupPath` in **autocommit**, before any `BeginTransaction` (SQLite refuses `VACUUM` inside a transaction). On success call `onBackup backupPath` so the operator learns the safety-net path *before* anything is touched, whatever happens next.
  2. **ONE transaction**: `deleteAllEvents` → `importNdjsonRows` → `EventStore.rebuildFtsIndex` (reuse wwc36's shipped helper, don't re-inline the `('rebuild')` insert) → `Projection.saveCheckpoint conn handler.Name 0L` for every registered handler. A malformed line anywhere rolls back the wipe too, so the store ends byte-identical to before.

  This is the one place the protocol **inverts** wwc36's precedent: there the backup was the sole restore path for a committed mutation; here the transaction is the primary restore path and the backup is a redundant net.

- **Result type** (server-internal, `Administration.fs` — nothing here crosses the wire as a typed value; SSE payloads are hand-built JSON). Not a reuse of `ImportFailure`/`ImportOutcome`: `StoreNotEmpty` is meaningless here and there is no existing case for "backup failed, nothing touched".

  ```fsharp
  type WipeImportResult =
      | WipeBackupFailed of reason: string
      | WipeImportFailed of backupPath: string * lineNumber: int * message: string
      | WipeImportApplied of backupPath: string * eventsDiscarded: int * eventsImported: int
  ```

- **SSE vocabulary** (via the shared `Sse.sseFrame`):

  | event | payload | when |
  |---|---|---|
  | `rejected` | `{"message":"…"}` | a guard refused — nothing attempted, no backup taken |
  | `backup` | `{"backupPath":"…"}` | `vacuumIntoBackup` succeeded, before `BeginTransaction` |
  | `error` | `{"phase":"backup"\|"import","lineNumber":N,"message":"…"}` | backup failure, or malformed line / exception during import |
  | `complete` | `{"eventsImported":N,"eventsDiscarded":M}` | committed — deliberately does **not** repeat `backupPath`; the client already has it from `backup` |

- **Preview endpoint** — a new `IAdminApi` member, plain `Async` (a fast aggregate read, not SSE), `use conn = factory ()` per ADR-0033:

  ```fsharp
  type WipeImportPreview = {
      EventCount: int
      DistinctStreamCount: int
      OldestTimestamp: string option
      NewestTimestamp: string option }

  getWipeImportPreview: unit -> Async<WipeImportPreview>
  ```

  Backed by one new `EventStore.getEventStoreSummary` — `SELECT COUNT(*), COUNT(DISTINCT stream_id), MIN(timestamp), MAX(timestamp) FROM events`. The lexicographic `MIN`/`MAX` over TEXT is chronologically correct because every writer stamps `DateTimeOffset.ToString("o")` (fixed-width, sortable) — say so in the doc comment so a later edit doesn't "fix" it into `datetime()`. Do **not** reuse `getHealthStats` (90-day-bounded daily counts, no distinct-stream count, and an images-directory disk walk on every call — wrong shape and wrong cost for a confirm dialog) or `getDistinctStreams` (materializes every stream id just to count them).

  The incoming file's non-blank line count is computed **client-side** from the `File` object before upload — no staging area, no second upload phase.

### Concurrency: a new guard, born inside jrflk's `AdminGuards`

WAL + `busy_timeout` (ADR-0033) already makes concurrent writers *safe*, but this transaction is uniquely long-lived and store-wide, so file-level serialization stops being a complete answer for one interleaving: **a projection rebuild that started against the pre-wipe log and is still replaying when the wipe-import commits writes a checkpoint pointing into a discarded log**, leaving `isAnyProjectionDirty` silently reporting *clean* over content that no longer exists. That is precisely the invariant guardrail 3 exists to protect, so wipe-import and projection rebuild must be mutually exclusive. This is a **coherence** guard, not a safety guard — the first documented bound on ADR-0033's "WAL + busy_timeout is sufficient" reasoning.

administration-jrflk is retiring `Administration.fs`'s module-level `ConcurrentDictionary` guards into composition-root-owned state. This guard must be read by **two different handlers**, so no closure can own it — it settles jrflk's own closure-vs-record choice in favour of the explicit `AdminGuards` record. Building it before jrflk lands means either a fourth ambient module-level dictionary jrflk then has to retire too, or this task inventing `AdminGuards` and jrflk inheriting a half-finished migration — and this task's Expecto tests are the most destructive in the suite, so a cross-test-file key collision (jrflk's whole motivating defect) would corrupt an entire test store rather than merely skip a job run. Hence the hard `depends_on`.

Once jrflk lands, the work here is one record field plus two checks:

- `AdminGuards` gains `WipeImportInProgress: ConcurrentDictionary<string, unit>`.
- `wipeImportEventsStreamHandler`: check `guards.RebuildingProjections` non-empty **first** → `rejected` "A projection rebuild is in flight — wait for it to finish", with no claim ever made; **then** `TryAdd` on `WipeImportInProgress` fails → `rejected` "An event log import is already running"; else `try/finally TryRemove` around the whole body. This order is deliberate and was corrected at refinement: `driftCheckStreamHandler` (`Administration.fs:602-641`) checks its cross-cutting condition *before* touching its own guard and never claims-then-releases, so the original "TryAdd first, release if blocked" wording was not in fact "the same shape as `driftCheckStreamHandler`" — it invented a claim-then-release pattern no existing handler uses, with one more release path to get wrong. It also now mirrors the check this task already specifies for `projectionRebuildStreamHandler`'s side.
- `projectionRebuildStreamHandler`: gains a check — `WipeImportInProgress` non-empty → `rejected` "An event log import is in flight", before its existing `TryAdd`.
- No mutex beyond the two dictionaries. The check-and-claim pair is TOCTOU-racy in the abstract, but the losing interleaving needs two clicks landing within microseconds in two tabs. **Document the accepted window; don't engineer around it.**

### `sqlite_sequence` is deliberately not reset

`deleteAllEvents` doesn't touch `sqlite_sequence`. If the discarded log's max `global_position` exceeded the imported log's, a later ordinary append continues from `(discarded max) + 1`, leaving a permanent gap — harmless by the same reasoning wwc36 established for delete-gaps (every cursor uses strict `<`/`>`; lag uses `MAX`, not `COUNT`). vrc56's `importNdjson` doc-comment claim that appends continue from `(imported max) + 1` is therefore true only on the empty-store path; correct that line while in the file.

### Client: extends the Projections tab's Backup section

Not the Surgery tab. In `AdminProjections/{Types,State,Views}.fs`, beside the existing Export/Import controls:

- On file selection: count the file's non-blank lines client-side, call `getWipeImportPreview`, and open a paper-overlay confirm modal (`Components.ModalPanel`, ADR-0016 — the idiom the Surgery tab's confirm dialog and the compensating-event composer both use) showing the discard-side server stats and the incoming-side client stat together.
- **Cancel** closes the dialog with no request ever sent (a model-only `Msg`, no `Cmd.ofEffect`) — "untouched" holds by construction, not by rollback.
- **Confirm** streams the new route via `runWipeImportStream` (sibling to `runImportStream`, same reader/buffer/`data: ` framing), rendering the `backup` path immediately, then the terminal outcome.
- On `complete`, `Cmd.ofMsg Load` — exactly what the existing `Import_completed` handler does. That is what flips the cross-tab "projections out of sync" banner (client-derived from `ProjectionsModel.Stats`'s `Lag`, ADR-0034) with no tab revisit and no new plumbing.

## Acceptance criteria
- [x] `EventStore.importNdjson` keeps its exact current signature and semantics after the `importNdjsonRows` extraction; `EventStoreNdjsonTests.fs` and `importEventsStreamHandler` need zero changes.
- [x] Wipe & Import creates a valid backup file — opens on a throwaway connection, `PRAGMA integrity_check` returns `ok`, and its `events` content matches the pre-wipe store's full content (not merely the count) — before any deletion.
- [x] A malformed line anywhere in the uploaded NDJSON rolls back the whole wipe-and-import transaction: a full dump of `events` + `projection_checkpoints` before vs. after is byte-for-byte identical.
- [x] After a successful Wipe & Import, `events` content matches the imported NDJSON exactly (fidelity + `global_position` preservation, the same guarantee vrc56 proved for the empty-store path), and the `complete` payload's `eventsDiscarded`/`eventsImported` match the pre-wipe count and the NDJSON's row count.
- [x] A subsequent ordinary append after Wipe & Import succeeds with a `global_position` strictly greater than every imported position — not necessarily `(imported max) + 1`, since `sqlite_sequence` is deliberately not reset.
- [x] `events_fts` is searchable for newly imported content **and not** searchable for discarded content; the negative direction is what catches a missing `rebuildFtsIndex`.
- [x] After Wipe & Import, every registered projection's checkpoint is `0` and `Administration.isAnyProjectionDirty` reports all of them dirty.
- [x] `getWipeImportPreview` returns discard-side stats matching a direct query against the store, and returns `None` timestamps for an empty store.
- [x] A wipe-import already in flight refuses a second concurrent wipe-import (`rejected`, and no backup is taken for the second request).
- [x] A wipe-import in flight refuses a concurrent projection rebuild, and a rebuild in flight refuses a wipe-import — both directions of the `WipeImportInProgress` ↔ `RebuildingProjections` mutual exclusion.
- [x] `/api/stream/import-events` is unaffected: still refuses any non-empty store, with its existing tests passing unchanged.
- [ ] The confirm modal shows both the server discard-side stats and the client-computed incoming line count before Confirm is enabled; Cancel produces zero network requests; Confirm renders the `backup` path and then both `complete` counts, and the cross-tab "projections out of sync" banner appears without a tab revisit. [human-eye] — implemented and Fable-typechecked (`npm run build`), but not live-browser-verified in this session; same gap wwc36 left for the Surgery tab (closed there by administration-svq3t's Playwright spec).

## Notes

**Premises settled since capture:**
- The original `depends_on: [administration-vrc56, administration-wwc36]` are both done — that gate is cleared and replaced by the harder `administration-jrflk` gate (see Concurrency). `design-system-001` is the BC's standing frontend gate, already done.
- Backup retention: wwc36 settled **keep-all** — `backups/` beside the db file, never pruned, `getBackupStats` a plain directory walk. This task inherits it; nothing left open.
- The FTS resync reuses `EventStore.rebuildFtsIndex` (shipped by wwc36) rather than hand-inlining `INSERT INTO events_fts(events_fts) VALUES ('rebuild')`. The original note's "double-check FTS5 external-content semantics during implementation" is resolved — wwc36's shipped delete path proved it, and the FTS criterion above asserts both directions anyway.
- The original `What` contradicted itself, saying both "reset `projection_checkpoints` to 0" and "leave projections dirty exactly as vrc56's import does". **Only rewind-to-0 is correct here.** vrc56 leaves checkpoints untouched because on a fresh store they are already 0; after a wipe, untouched checkpoints would point into a log that no longer exists.

**Testability** — every server-side criterion is plain Expecto over `Administration.runWipeAndImport`, in a new `tests/Server.Tests/AdminWipeImportTests.fs` built on `TestDb.withTempDbFactory` plus the `bootstrapAdmin`/`fullDump`/`cleanupBackups` fixture shape from `AdminSurgeryTests.fs`, feeding `new StringReader(ndjson)` and a real `db.Path` (`VACUUM INTO` needs a real sibling directory). Deliberately **not** covered:
- No concurrency test analogous to wwc36's: this transaction holds the write lock for its whole duration by design, so asserting "concurrent writers always succeed" would encode a false invariant. Document the past-5s `SQLITE_BUSY` behaviour instead.
- No forced-backup-failure test — `VACUUM INTO` is hard to force-fail against a real path, `AdminSurgeryTests.fs` has none either, and the path is one shared `match` arm with wwc36's shipped `BackupFailed`.
- No SSE-handler-level test — `Sse.sseFrame`'s framing is covered by `SseTests.fs`; vrc56 added none for `import-events`. Keep parity.
- **No Playwright spec.** administration-svq3t's destructive-spec gate exists because `reuseExistingServer` can aim a spec at a live dev `DATA_DIR`; wipe-import destroys the *entire* store, so the blast radius of getting that gate wrong is total, while what a spec would uniquely cover (file picker, client line count, modal interaction) is small. Plain Expecto plus the one `[human-eye]` criterion is the right coverage. If e2e is ever wanted it is a follow-up task reusing svq3t's gate verbatim.

**Not split.** The `importNdjsonRows` extraction is mechanical, has no independent user-visible value, and is fully regression-gated by vrc56's existing tests — a separate task would carry exactly one criterion ("existing tests still pass"), which isn't a task boundary. The only available seam is server vs. client, which is worse: a server-only half ships a destructive endpoint with no confirm dialog in front of it.

**File-order gotcha:** `ensureBackupsDir`/`newBackupPath` are private helpers (currently `Administration.fs:1062`/`:1072` — **anchor by symbol name at implementation time, not by these line numbers; they have already drifted once since capture**), *after* the import/export handlers (~750–816). The new composite and handler therefore can't sit next to `importEventsStreamHandler` — open a new section (`// ── Wipe-first event log import (administration-n8kqw, ADR-0038) ──`, matching whatever number the ADR actually lands on) after `computeBackupStats` and before `create`.

**Two UX calls, settled at refinement (2026-07-31) — directives, not recommendations:**
- An empty incoming file **is allowed** to proceed (net effect: wipe to empty). Nothing objects — the guardrail protocol's backup plus explicit confirm already make this exactly as safe as any other wipe-import. The confirm-dialog copy **must state that outcome explicitly** when the client-computed incoming line count is 0, rather than silently blocking or silently proceeding.
- A successful Wipe & Import **does not auto-navigate** anywhere — rely on the existing cross-tab dirty banner, consistent with how ordinary import and every surgery mutation already surface their aftermath.

**Vision-boundary acknowledgment (non-blocking, informational).** This is Administration-BC (generic, operator-tooling) work while the media-experience v1 arc (In Focus, Unified Dashboard, Steam Import, HLTB) remains entirely unbuilt. Per `vision.md`'s Operability **Boundary**, media-experience scope would normally win a competing-priority call; this task is refined and promoted on the builder's explicit direction, which is the documented override path.

**Drive-by, not an acceptance criterion:** `importEventsStreamHandler`'s doc comment argues there is no `start` event because an empty-payload SSE event would emit `{"type":"start",}` and break `JSON.parse`. `Sse.sseFrame` now special-cases the empty payload, so that reasoning is stale — worth a one-line correction while in the file.

**ADR-0038 to be written by the worker.** `0035` is `administration-jrflk` (admin-guard composition-root ownership) and `0036` is `infrastructure-npyhb` (Feliz.DaisyUI pin) — both already on disk, so the draft below is **not** ADR-0036 as originally written. `0036` is the highest ADR on disk; `infrastructure-p1h9a` nominally claims `0037`, so re-confirm the next free number at write time in case that task lands first. Only the number changes — the draft body's cross-references (0033/0034/0035) are all still accurate:

> **Wipe-first event log import — a separate route, one transaction, and mutual exclusion with projection rebuild.**
>
> Overwriting a non-empty event store is exposed as its own SSE route rather than a flag on `/api/stream/import-events`, so the safe route's refusal stays literally true and the harmless endpoint never receives the dependencies needed to destroy a store. The operation runs ADR-0034's three-guardrail protocol with one inversion: `VACUUM INTO` still takes a verified backup first, in autocommit, and streams its path as a `backup` event before any mutation — but the primary restore path is the transaction itself, since the wipe, the re-import, the FTS rebuild and the checkpoint rewind all share one transaction. Because that transaction is long-lived and store-wide, ADR-0033's WAL + `busy_timeout` serialization is no longer sufficient alone: it keeps concurrent writes *safe* but not *coherent*, since a rebuild started against the pre-wipe log would checkpoint into a discarded log and leave projections reading as clean. Wipe-import and projection rebuild are therefore mutually exclusive via a `WipeImportInProgress` guard on the composition-root-owned `AdminGuards` record (ADR-0035), with the microsecond check-and-claim race knowingly accepted for a single-operator app.
>
> Consequences to record: `sqlite_sequence` is deliberately not reset; concurrent writers may legitimately hit `SQLITE_BUSY` for the duration of a large import; `importNdjson`'s "continues from `(imported max) + 1`" claim holds only on the empty-store path.

## Outcome

Shipped exactly as specified. Server: `EventStore.importNdjsonRows` extracted
(no transaction of its own; `importNdjson`'s own signature/semantics/
`StoreNotEmpty` behaviour unchanged — verified by `EventStoreNdjsonTests.fs`
needing zero edits), `EventStore.deleteAllEvents` (`DELETE FROM events`,
`sqlite_sequence` deliberately untouched), `EventStore.getEventStoreSummary`/
`EventStoreSummary` (the preview query). `Administration.fs`:
`AdminGuards.WipeImportInProgress`, `WipeImportResult`, `runWipeAndImport`
(VACUUM INTO backup in autocommit → one transaction: delete → import rows →
FTS rebuild → checkpoint rewind), `wipeImportEventsStreamHandler` at
`POST /api/stream/wipe-import-events` (sibling route to
`/api/stream/import-events`, wired in `Composition.fs`), and
`IAdminApi.getWipeImportPreview`. The mutual-exclusion guard (both
directions of `WipeImportInProgress` ↔ `RebuildingProjections`) was
extracted as two plain, directly-testable functions —
`decideAndClaimWipeImportGuard` (the corrected order: `RebuildingProjections`
checked first with no claim ever made, then `TryAdd` on
`WipeImportInProgress`, never claim-then-release) and `wipeImportInFlight`
(the `projectionRebuildStreamHandler`-side check) — specifically so the
guard order is unit-testable without SSE/HTTP, which the task's own
"no SSE-handler-level test" exclusion would otherwise have left unverifiable;
this is documented as a deliberate design choice in ADR-0038 (not itself an
acceptance criterion, but load-bearing for satisfying criteria 9–10 within
the stated testing constraints). Client: a second file-input control in
`AdminProjections`' existing Backup section (Types/State/Views), a
paper-overlay confirm dialog (`Components.ModalPanel`) showing both the
discard-side server stats and the client-computed incoming line count
(explicitly calling out a 0-line file as "wipes to empty"), Cancel as a
model-only `Msg`, Confirm streaming `backup` then the terminal outcome with
no auto-navigation on success (reloads `ProjectionsModel.Stats`, flipping
the existing cross-tab dirty banner).

Both drive-by doc-comment corrections were made: `importNdjson`'s
"(imported max) + 1" claim is now scoped to the empty-store path only, and
`importEventsStreamHandler`'s stale "empty-payload SSE breaks JSON.parse"
reasoning is corrected to note `Sse.sseFrame` (administration-h4k2p) already
special-cases that.

Tests: `tests/Server.Tests/AdminWipeImportTests.fs`, 11 new Expecto tests
covering backup fidelity (full content match, not just count), malformed-
line rollback (byte-for-byte `events`+`projection_checkpoints` dump),
post-wipe content/global-position/FTS/checkpoint-rewind correctness, the
`sqlite_sequence`-not-reset gap behaviour, `getWipeImportPreview` against a
direct query (both non-empty and empty-store cases), both directions of the
mutual-exclusion guard, and a regression check that
`EventStore.importNdjson`'s `StoreNotEmpty` refusal is unaffected. Full
suite: 427/427 passing (416 baseline + 11 new). `npm run build` (client
Fable typecheck + bundle) green.

ADR: `.agentheim/knowledge/decisions/0038-wipe-first-event-log-import.md`
(0038 was confirmed free — 0036/0037 were already taken by same-day tasks).

BC README updated: the Event log export/import bullet now points at the
wipe-first path instead of calling it out of scope; a new Wipe-first event
log import bullet; the Admin guard ownership bullet mentions the third
`AdminGuards` field; the `IAdminApi` method list gained
`getWipeImportPreview`; the "Backup / restore into a non-empty store" open
question is resolved and removed.

Key files: `src/Server/EventStore.fs`, `src/Server/Administration.fs`,
`src/Server/Composition.fs`, `src/Shared/Shared.fs`,
`src/Client/Pages/AdminProjections/{Types,State,Views}.fs`,
`tests/Server.Tests/AdminWipeImportTests.fs`,
`tests/Server.Tests/Server.Tests.fsproj`,
`.agentheim/knowledge/decisions/0038-wipe-first-event-log-import.md`,
`.agentheim/contexts/administration/README.md`.
