---
id: administration-svq3t
title: Playwright e2e spec for the Surgery tab (edit/delete/rename + confirm dialogs + dirty banner)
status: backlog
type: feature
context: administration
created: 2026-07-22
completed:
depends_on: [administration-wwc36]
blocks: []
tags: [admin-console, surgery, testing, playwright, e2e]
related_adrs: [0027, 0034]
related_research: []
prior_art: []
---

## Why
administration-wwc36 shipped the Surgery tab's server-side guardrail protocol
(VACUUM INTO backup, preview+confirm, checkpoint-rewind dirty signal) fully
TDD'd via Expecto (`EventSurgeryTests.fs`, `AdminSurgeryTests.fs`), but the
client UI (the three operation panels, the paper-overlay confirm dialogs,
the cross-tab "projections out of sync" banner) was only verified via
`npm run build` (Fable typecheck) and manual reasoning — per that task's
acceptance criteria, several client-facing bits are explicitly `[human-eye]`
(the banner's visual placement, the delete dialog's gap-consequence wording).
The project already has a real Playwright e2e harness (ADR-0027,
`event-tail-follow.spec.ts`) proven on exactly this kind of admin-console
interaction (SSE streams, confirm dialogs, live cross-tab state) — this is
the natural place to close the gap, not a request for new UI test
infrastructure.

## What
A new spec (e.g. `tests/e2e/admin-surgery.spec.ts`) mirroring
`event-tail-follow.spec.ts`'s harness usage (fresh temp `DATA_DIR` per run,
direct Fable.Remoting HTTP calls to seed events rather than raw event-store
writes), covering at least:
- Edit: load a preview by global position, edit the payload, confirm via the
  paper-overlay dialog, and assert the result banner + backup path renders.
- Delete: load a preview, assert the gap-consequence copy renders with the
  actual stream position, confirm, assert the result.
- Rename: preview an old event type, confirm, assert the rename result.
- The cross-tab dirty banner appears immediately after a committed surgery
  mutation (without navigating to the Projections tab first) and disappears
  after a Rebuild-all completes.

## Acceptance criteria
- [ ] All four flows above pass headlessly via `npm run test:e2e`.
- [ ] The spec seeds its own isolated events (never the real dev DB) and
      cleans up its own temp `DATA_DIR`, per ADR-0027's existing convention.

## Notes
Not a blocker for administration-wwc36 itself — the server-side guardrail
protocol is fully machine-tested, and the client compiles and was reasoned
through manually; this closes the remaining `[human-eye]` gap with the
project's existing e2e tooling rather than inventing new infrastructure.
