---
id: administration-n8kqw
title: Event log import — wipe-first path for a non-empty store, gated behind wwc36's surgery-grade auto-backup
status: backlog
type: feature
context: administration
created: 2026-07-22
completed:
depends_on: [administration-vrc56, administration-wwc36]
blocks: []
tags: [admin-console, event-store, backup, import, surgery]
related_adrs: [0002, 0003]
related_research: []
prior_art: []
---

## Why
administration-vrc56 covers import into an *empty* store only and refuses otherwise. Overwriting a populated store is exactly as destructive as administration-wwc36's raw event-log surgery — it deserves the same three-guardrail protocol (auto-backup first, preview + explicit confirmation, projections-dirty flag), reused rather than reinvented.

## What
- A "Wipe & Import" action, surfaced from vrc56's import UI when the target store is non-empty (where vrc56 currently just refuses), that:
  1. Runs wwc36's auto-backup guardrail against the *current* store before touching anything; aborts if backup fails.
  2. Shows a preview — current event count/streams/date range being discarded, and the incoming file's event count — requiring explicit confirmation in a paper-overlay dialog. Cancelling leaves the store untouched.
  3. Wipes: `DELETE FROM events`, resets `projection_checkpoints` to empty/0 (same as `rebuildProjection`'s pre-replay reset), and **explicitly rebuilds `events_fts`** (`INSERT INTO events_fts(events_fts) VALUES ('rebuild')`) — the `events_fts` trigger set only covers `AFTER INSERT`; a wipe is exactly the "rows disappear" case that trigger doesn't handle, so skipping this step leaves a stale FTS index.
  4. Delegates to vrc56's `EventStore.importNdjson` against the now-empty store.
  5. Leaves projections dirty exactly as vrc56's fresh-store import does (checkpoint lag detection, no auto-rebuild) — operator runs Rebuild-all afterward.

## Acceptance criteria
- [ ] Wipe & Import creates a valid backup file (opens as valid SQLite, contains the pre-wipe event count) before any deletion — same provable-before-mutation shape as wwc36's guardrail test.
- [ ] Wipe & Import refuses to proceed without explicit confirmation in the preview dialog; cancelling leaves the store byte-for-byte untouched.
- [ ] After Wipe & Import, `events` table content matches the imported NDJSON exactly (same fidelity guarantee as vrc56's fresh-store import, including `global_position` preservation).
- [ ] After Wipe & Import, `events_fts` is searchable for the newly imported content (a distinctive payload substring is found via `queryEventPage`'s `Search` filter) — proving the explicit post-wipe FTS rebuild ran, not just the insert-time trigger.
- [ ] After Wipe & Import, every projection shows dirty (checkpoint lag) until an explicit Rebuild-all completes, and that rebuild then produces projections consistent with the newly imported log.

## Notes
Blocked until both administration-vrc56 (the fresh-store import path this delegates to) and administration-wwc36 (the reusable auto-backup module) land. Inherits whatever backup-retention policy wwc36 settles on ("keep last N?" — wwc36's own Notes flag this as open) rather than defining a separate one here.

The `events_fts`-goes-stale-after-DELETE risk was found while reading `EventStore.fs`'s `createFtsIndex` comment directly — worth double-checking against actual SQLite FTS5 external-content semantics during implementation, not just trusting this note.
