---
id: 0038
title: Wipe-first event log import — a separate route, one transaction, and mutual exclusion with projection rebuild
scope: administration
status: accepted
date: 2026-07-31
supersedes: []
superseded_by: []
related_tasks: [administration-n8kqw]
related_research: []
---

# ADR 0038: Wipe-first event log import — a separate route, one transaction, and mutual exclusion with projection rebuild

## Context

administration-vrc56 (ADR-0029) gave the event log its first portable form —
export, and import into a store that is currently *empty*, refusing
otherwise. Overwriting a *populated* store is exactly as destructive as
administration-wwc36's raw event-log surgery (ADR-0034) and deserves the
same three-guardrail protocol (auto-backup first, preview + explicit
confirmation, projections-dirty signal), reused rather than reinvented. The
open question this task resolves is the *shape* of the wipe-then-reimport:
one atomic operation where the transaction, not the backup file, is the
primary restore path.

## Decision

### A separate route, not a flag

`POST /api/stream/wipe-import-events` is a new raw Giraffe SSE route,
sibling to (not a flag on) `/api/stream/import-events`. This keeps the safe
route's "refuses any non-empty store" invariant *literally* true rather than
conditionally true, and keeps the harmless endpoint from ever acquiring the
`dbPath`/`projectionHandlers` dependencies needed to destroy a store.
`EventStore.importNdjson`'s existing signature, semantics, and
`StoreNotEmpty` refusal are untouched — its read-line/decode/explicit-rowid-
insert loop is extracted into `EventStore.importNdjsonRows` (no transaction
of its own; the caller owns commit/rollback) purely so `runWipeAndImport`
below can reuse it inside a *different* transaction, but `importNdjson`
itself still opens its own transaction around the same loop and behaves
exactly as before.

### The protocol inverts ADR-0034's backup/transaction relationship

`Administration.runWipeAndImport` still takes a `VACUUM INTO` backup first,
in autocommit, before any `BeginTransaction` (SQLite refuses `VACUUM` inside
a transaction) — `onBackup` fires on success so the operator learns the
safety-net path before anything is touched, whatever happens next. But
where ADR-0034's surgery ops treat the backup as the *sole* restore path for
a committed mutation, here the wipe, the re-import
(`EventStore.deleteAllEvents` → `EventStore.importNdjsonRows` →
`EventStore.rebuildFtsIndex` → `Projection.saveCheckpoint ... 0L` for every
registered handler) all share **one transaction**, so a malformed line
anywhere rolls back the wipe too and leaves the store byte-identical to
before. The backup is a redundant safety net, not the primary one.

`EventStore.deleteAllEvents` is `DELETE FROM events`, not drop/recreate —
schema, the `events_fts` shadow tables, and the `events_fts_ai` trigger
survive. It deliberately does **not** reset `sqlite_sequence`: if the
discarded log's max `global_position` exceeded the imported log's, a later
ordinary append continues from `(discarded max) + 1`, leaving a permanent
gap — harmless by the exact reasoning ADR-0034 established for delete-gaps
(every cursor uses strict `<`/`>`; lag uses `MAX`, not `COUNT`).
`importNdjson`'s own doc-comment claim that appends continue from
`(imported max) + 1` is therefore corrected to note that holds only on the
empty-store path importNdjson itself governs, not on this wipe-first path.

### A new preview query, not a reuse of `getHealthStats`/`getDistinctStreams`

`EventStore.getEventStoreSummary` (`SELECT COUNT(*), COUNT(DISTINCT
stream_id), MIN(timestamp), MAX(timestamp) FROM events`) backs a new
`IAdminApi.getWipeImportPreview : unit -> Async<WipeImportPreview>` — a
fast aggregate read (plain `Async`, not SSE) for the confirm dialog's
discard-side stats. `getHealthStats` (90-day-bounded daily counts, no
distinct-stream count, an images-directory disk walk on every call) and
`getDistinctStreams` (materializes every stream id just to count them) are
both the wrong shape and the wrong cost for a confirm dialog that only needs
four numbers. `MIN`/`MAX` over the `timestamp` TEXT column are
chronologically correct, not merely lexicographically coincidental, because
every writer stamps `DateTimeOffset.ToString("o")` (ISO-8601, fixed-width,
sortable) — the doc comment says so explicitly, so a later edit doesn't "fix"
it into `datetime()`. The incoming file's own non-blank line count is
computed **client-side** from the `File` object (via `File.text()`) before
upload — no staging area, no second upload phase.

### A new coherence guard: `WipeImportInProgress` ↔ `RebuildingProjections`

WAL + `busy_timeout` (ADR-0033) already makes concurrent writers *safe*, but
this transaction is uniquely long-lived and store-wide, so file-level
serialization stops being a complete answer for one interleaving: a
projection rebuild that started against the pre-wipe log and is still
replaying when the wipe-import commits would write a checkpoint pointing
into a discarded log, leaving `isAnyProjectionDirty` silently reporting
*clean* over content that no longer exists. This is a **coherence** guard,
not a safety guard — the first documented bound on ADR-0033's "WAL +
busy_timeout is sufficient" reasoning.

`AdminGuards` (ADR-0035) gains a third field, `WipeImportInProgress`. Both
directions of the mutual exclusion are extracted as plain, directly-testable
functions rather than left inline in the SSE handlers (unlike
`driftCheckStreamHandler`'s and `projectionRebuildStreamHandler`'s own
existing single-flight checks, which stayed inline and untested at the
handler level): `Administration.decideAndClaimWipeImportGuard` encodes the
wipe-import side's check-then-claim, and `Administration.wipeImportInFlight`
the rebuild side's check. This choice was made specifically so the guard
*order* — which is load-bearing — is verifiable without spinning up SSE/HTTP.

The order is deliberate and was corrected at refinement:
`wipeImportEventsStreamHandler` checks `guards.RebuildingProjections`
non-empty **first**, refusing with **no claim ever made** on
`WipeImportInProgress`, and only *then* attempts `TryAdd` on
`WipeImportInProgress`. It never claims-then-releases. This mirrors
`driftCheckStreamHandler`'s existing shape (checks its cross-cutting
condition before touching its own guard, never claims-then-releases) — an
earlier draft of this task's refinement had proposed "claim first, release
if blocked" as supposedly matching that handler, which was incorrect: no
existing handler in this codebase uses a claim-then-release pattern.
`projectionRebuildStreamHandler` symmetrically gains a check —
`wipeImportInFlight guards` — before its own existing `TryAdd`.

No mutex beyond the two `ConcurrentDictionary`s. The check-and-claim pair is
TOCTOU-racy in the abstract, but the losing interleaving needs two clicks
landing within microseconds in two tabs of a single-operator app — the
window is documented, not engineered around.

### Result type is server-internal, not a reuse of `ImportFailure`/`ImportOutcome`

```fsharp
type WipeImportResult =
    | WipeBackupFailed of reason: string
    | WipeImportFailed of backupPath: string * lineNumber: int * message: string
    | WipeImportApplied of backupPath: string * eventsDiscarded: int * eventsImported: int
```

`StoreNotEmpty` is meaningless here (the whole point is overwriting a
non-empty store), and neither existing type has a case for "backup failed,
nothing touched." Nothing here crosses the wire as a typed value — SSE
payloads are hand-built JSON via `Sse.sseFrame`, matching every other admin
SSE route's vocabulary (`rejected` / `backup` / `error` /`complete`).

### Client: extends the Projections tab's Backup section, not the Surgery tab

A second file-input control sits beside the existing Export/Import controls
in `AdminProjections`. On file selection: count the file's non-blank lines
client-side, call `getWipeImportPreview`, and open a paper-overlay confirm
modal (`Components.ModalPanel`, ADR-0016) showing both the discard-side
server stats and the incoming-side client stat together — the confirm-dialog
copy states explicitly when the incoming line count is 0 that the store will
be wiped to empty (an empty incoming file is allowed to proceed; nothing
about the guardrail protocol requires blocking it). Cancel is a model-only
`Msg` — no `Cmd.ofEffect`, so "untouched" holds by construction, not
rollback. Confirm streams the new route, rendering the `backup` path
immediately and then the terminal outcome; on `complete`, `Cmd.ofMsg Load`
reloads `ProjectionsModel.Stats`, which is what flips the existing cross-tab
dirty banner (ADR-0034) — no new plumbing. A successful Wipe & Import does
**not** auto-navigate, consistent with ordinary import and every surgery
mutation.

## Alternatives considered

- **A flag on `/api/stream/import-events` instead of a separate route.**
  Rejected — see "A separate route, not a flag" above.
- **Claim-then-release guard ordering** (an earlier refinement draft).
  Rejected — invents a pattern no existing handler in this codebase uses,
  with one more release path to get wrong, for no benefit over check-first.
- **Reuse `EventStore.ImportFailure`/`ImportOutcome` for the wipe-import
  result.** Rejected — `StoreNotEmpty` doesn't apply, and neither type has a
  "backup failed" case; a small dedicated `WipeImportResult` is clearer than
  overloading types whose cases mean something different here.
- **Leave the mutual-exclusion guard checks inline in the SSE handlers**
  (matching `driftCheckStreamHandler`'s/`projectionRebuildStreamHandler`'s
  own existing untested inline shape). Rejected for the wipe-import side
  specifically: this feature's acceptance criteria require the guard
  *order* to be verifiable, and the project's explicit "no SSE-handler-level
  test" convention (ADR-0029's precedent, kept here) rules out testing that
  order via HTTP. Extracting `decideAndClaimWipeImportGuard`/
  `wipeImportInFlight` as plain functions resolves the tension without
  spinning up SSE/HTTP for either side of the check.
- **Reset `sqlite_sequence` after `deleteAllEvents`.** Rejected — see
  "The protocol inverts ADR-0034's backup/transaction relationship" above;
  the gap is harmless by the same reasoning ADR-0034 already established.

## Consequences

### Positive
- Overwriting a populated store now has a real, guarded path — one atomic
  transaction, with a redundant `VACUUM INTO` safety net taken and reported
  before anything is touched.
- `importNdjson`'s existing empty-store contract (signature, semantics,
  `StoreNotEmpty` refusal) is untouched — `EventStoreNdjsonTests.fs` and
  `importEventsStreamHandler` needed zero changes, verified by the full
  suite staying green.
- The mutual-exclusion guard order is unit-testable without SSE/HTTP,
  closing a testability gap this feature would otherwise have inherited
  from `driftCheckStreamHandler`/`projectionRebuildStreamHandler`'s existing
  untested inline guard checks.

### Negative / accepted tradeoffs
- `AdminGuards` grows a third field — every call site building one already
  goes through `makeGuards ()`, so this was a zero-touch change at every
  existing call site, but it is one more thing a future guard addition must
  remember to wire through `Composition.fs`.
- The TOCTOU window on the check-and-claim pair is knowingly unclosed (see
  "A new coherence guard" above) — acceptable for a single-operator app,
  worth revisiting only if this app ever grows multiple concurrent admin
  operators.
- Every wipe-import holds the write lock for its whole (potentially large)
  duration by design — concurrent writers may legitimately hit
  `SQLITE_BUSY` for the duration, not merely a brief retry window. No test
  asserts "concurrent writers always succeed" during a wipe-import, since
  that would encode a false invariant.

### Neutral
- `runWipeAndImport` does not take `guards` — the guard check/claim/release
  lifecycle is entirely the SSE handler's responsibility, matching
  `runSurgeryMutation`'s own guard-free shape (ADR-0034's guard-adjacent
  logic also lives one layer up, in `isAnyProjectionDirty`'s callers).

## References

- `src/Server/EventStore.fs` — `importNdjsonRows`, `importNdjson`
  (refactored, signature/semantics unchanged), `deleteAllEvents`,
  `EventStoreSummary`, `getEventStoreSummary`.
- `src/Server/Administration.fs` — `AdminGuards.WipeImportInProgress`,
  `WipeImportGuardDecision`, `decideAndClaimWipeImportGuard`,
  `wipeImportInFlight`, `WipeImportResult`, `runWipeAndImport`,
  `wipeImportEventsStreamHandler`; `projectionRebuildStreamHandler` gains
  the `wipeImportInFlight` check; `create` gains `getWipeImportPreview`.
- `src/Server/Composition.fs` — `/api/stream/wipe-import-events` route
  wiring, sibling to `/api/stream/import-events`.
- `src/Shared/Shared.fs` — `WipeImportPreview`,
  `IAdminApi.getWipeImportPreview`.
- `src/Client/Pages/AdminProjections/{Types,State,Views}.fs` — the Backup
  section's Wipe & re-import control and confirm dialog
  (`wipeImportConfirmDialog`, `Components.ModalPanel`).
- `tests/Server.Tests/AdminWipeImportTests.fs` — backup fidelity,
  malformed-line rollback, post-wipe content/position/FTS/checkpoint
  assertions, preview stats, and both directions of the mutual-exclusion
  guard.
- ADR-0016 — paper overlay, the confirm modal's visual language.
- ADR-0025 — `isAnyProjectionDirty`'s not-dirty guard idiom, reused verbatim
  for the checkpoint-rewind dirty signal.
- ADR-0029 — the NDJSON export/import contract this task builds on and
  keeps byte-identical; the "no SSE-handler-level test" precedent this task
  keeps parity with.
- ADR-0033 — per-request connection factory; the WAL + busy_timeout
  reasoning this task's coherence guard is the first documented bound on.
- ADR-0034 — the three-guardrail protocol this task reuses, with the one
  documented inversion (transaction, not backup, as the primary restore
  path).
- ADR-0035 — the `AdminGuards` composition-root-owned record this task adds
  a field to.
- administration-n8kqw — the task that shipped this feature.
