---
id: administration-wwc36
title: Event surgery — raw edit/delete/rename with auto-backup, preview, and projections-dirty flag
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-qjcp4, design-system-001]
blocks: []
tags: [admin-console, event-store, surgery, backup]
related_adrs: [0002, 0003]
related_research: []
prior_art: []
---

## Why
Single-user app, owner is the operator: sometimes the honest fix is editing the log itself — a typo'd payload, an event appended by a buggy import, an event type renamed in code that left old names stranded in the store. This must exist, but only behind guardrails that make it hard to lose data.

## What
Surgery tab (`/admin/surgery`) operations, each with the same three-guardrail protocol:
1. **Auto-backup first:** before any mutation, copy `mediatheca.db` (plus WAL checkpoint) to a timestamped backup file in the data dir; the operation aborts if backup fails.
2. **Preview:** show exactly the affected rows (count + sample) and require explicit confirmation in a paper-overlay dialog.
3. **Projections dirty:** after mutation, flag all projections dirty; the UI banners "projections out of sync — rebuild" until a rebuild (administration-qjcp4) runs.

Operations:
- Edit a single event's `data` / `metadata` JSON.
- Delete a single event (with the stream-position gap consequence stated in the preview).
- Rename an event type store-wide (`UPDATE events SET event_type = ... WHERE event_type = ...`) — the schema-migration verb for DU renames.

## Acceptance criteria
- [ ] Every mutation path provably writes a backup file first (test: backup exists and opens as valid SQLite before mutation applied).
- [ ] Preview counts match what actually changes; cancel changes nothing.
- [ ] After a mutation, the dirty banner shows until rebuild-all completes.
- [ ] Rename migrates all occurrences and is reflected in the explorer's event-type filter.
- [ ] Deleting an event and rebuilding produces projections consistent with the edited log.

## Notes
Needs refinement: backup retention policy (keep last N?); whether delete should renumber `stream_position` or leave gaps (leaving gaps is simpler and honest — but `appendToStream`'s expected-position check uses MAX, so gaps are tolerated; verify). Dirty flag can live in `projection_checkpoints` (e.g. reset checkpoints) or a separate flag table — decide during refinement. Build order: after compensating events (administration-xjmda) exists, so the safe path is always available first.
