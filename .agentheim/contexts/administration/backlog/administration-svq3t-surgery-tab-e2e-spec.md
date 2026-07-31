---
id: administration-svq3t
title: Playwright e2e spec for the Surgery tab (edit/delete/rename + confirm dialogs + dirty banner)
status: backlog
type: feature
context: administration
created: 2026-07-22
completed:
depends_on: [administration-wwc36, administration-bq4tw]
blocks: []
tags: [admin-console, surgery, testing, playwright, e2e]
related_adrs: [0027, 0034]
related_research: []
prior_art: [administration-a4d9b, administration-da908, administration-wwc36]
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
A new spec `tests/e2e/admin-surgery.spec.ts` mirroring
`event-tail-follow.spec.ts`'s harness usage (fresh temp `DATA_DIR` per run,
direct Fable.Remoting HTTP calls to seed events rather than raw event-store
writes), covering the four flows below. **This is the suite's first
destructive spec** — edit/delete/rename mutate real `events` rows — so it
must self-gate against ever running on a reused developer server (see
criterion 1); unlike `addFriend`-only specs there is no restore path
(administration-n8kqw, wipe-first import, is still open).

Test order inside the file: Edit → Delete → Rename (+ HTTP rename-back
cleanup) → dirty-banner/Rebuild-all last, so the file ends with clean
projections and an unpolluted event-type namespace for whatever spec file
runs next in the same shared server process.

## Acceptance criteria
- [ ] **Destructive-spec safety gate:** the spec skips unless the server run
      is guaranteed isolated — `test.skip(!process.env.CI, ...)` (or an
      equivalent explicit gate) at the top of the file/describe block, so
      the destructive flows can never hit a developer's live dev DB via
      ADR-0027's `reuseExistingServer` convenience path.
- [ ] **Edit flow:** seed a friend via `addFriend`, discover its
      `GlobalPosition`/`StreamId`/`StreamPosition` via direct
      `POST /api/admin/getEventPage` (one-element JSON-array body carrying an
      `EventPageQuery` with `StreamFilter: "Friend-" + slug`, PascalCase
      fields); in the Edit panel (locator scoped via
      `.velvet-card` + heading filter — see Notes), load the preview and
      assert the exact `"${streamId} @ Friend_added (stream position ${n})"`
      text; edit the Data textarea, confirm via the "Confirm edit" dialog
      (assert body contains `"Edit event ${globalPosition} on ${streamId}"`
      and the edited payload in a `<pre>`); assert the page-level result
      banner matches `/^Applied — 1 row affected\. Backup: .+/`.
- [ ] **Delete flow:** seed a separate friend; in the Delete panel, load the
      preview and assert the pre-confirm warning contains
      `"is currently at position ${streamPosition}"` and
      `"permanent gap in ${streamId}'s position sequence"`; confirm via the
      "Confirm delete" dialog (assert `"Delete event ${globalPosition}
      (Friend_added) on ${streamId}"`, `"hard delete"`, and
      `"no trash or undo"` render); assert the result banner.
- [ ] **Rename flow:** seed a fresh friend (keeps it inside the preview's
      20-row oldest-first `Sample` bound); preview renaming `Friend_added`
      to a unique disposable type name, asserting count `>= 1` (never an
      exact count — earlier tests also seed `Friend_added`) and that the
      sample table shows the seeded `Friend-<slug>` stream; confirm via the
      "Confirm rename" dialog; assert the result banner; then **rename back
      to `Friend_added` via a direct HTTP `renameEventType` call** — the
      UPDATE is store-wide, so restoring the namespace must not depend on
      alphabetical spec-file ordering (load-bearing cleanup, not hygiene).
- [ ] **Cross-tab dirty banner:** running last, first drive Rebuild-all to a
      known-clean baseline (banner absent), then commit one fresh surgery
      mutation while staying on the Surgery tab and assert
      `/Projections out of sync — rebuild/` becomes visible without
      navigating; follow the banner's "Go to Projections" link, click
      "Rebuild all" (wait for it to be enabled first), and assert the banner
      disappears and the button's accessible name reverts to exactly
      "Rebuild all" (the completion signal — there is no done-toast).
      Generous `test.setTimeout` (~60s): ADR-0034 rewinds every checkpoint
      to 0 and Rebuild-all replays handlers sequentially over SSE.
- [ ] All four flows pass headlessly via `npm run test:e2e` (with the gate
      env set), seeding only their own isolated events into the harness's
      per-run temp `DATA_DIR`, per ADR-0027's existing convention.
- [ ] The BC README's "Playwright e2e harness" bullet gains a sentence
      recording the destructive-spec CI-gate precedent (same place the
      tj8n2/cx92m/nf3wk harness findings are recorded), so the next
      destructive spec doesn't rediscover it.

## Notes
- **Locator ambiguity is a real shipped-UI hazard:** Edit and Delete panels
  render simultaneously and share byte-identical "Global position" labels,
  `global_position` placeholders, and "Load" buttons via the shared
  `globalPositionInput` helper. Scope every locator to its panel:
  `page.locator(".velvet-card").filter({ has: page.getByRole("heading",
  { name: "Edit event", exact: true }) })` — each `sectionCard` wraps
  exactly one `h3` ("Edit event" / "Delete event" / "Rename event type" /
  "Backups").
- **The confirm dialog closes optimistically** (`Confirm_pending` clears
  `PendingAction` synchronously, before the network call resolves) — the
  modal disappearing is *not* a persistence signal. The only completion
  signal for all three commits is the single page-level result banner
  (`model.LastResult`, rendered once above all three panels); assert on it
  with Playwright's auto-retrying `toBeVisible()`, never a fixed sleep.
  `LastResult` is one field — a second commit in the same page session
  overwrites the first banner.
- **Delete needs no second lookup:** for a fresh single-event stream, the
  `EventDto.StreamPosition` from `getEventPage` is the same number
  `SurgeryDeletePreview.StreamCurrentPosition` reports.
- **Rebuild-all is client-side orchestration** (ADR-0024): a queue over
  every registered projection, one SSE round-trip each via
  `/api/stream/rebuild-projection/{name}`, reloading stats between steps.
  The button is disabled until the initial `getProjectionStats` load
  resolves — `await expect(button).toBeEnabled()` before clicking, same
  pattern as `event-tail-follow.spec.ts`'s pagination buttons.
- **Server/DB state is shared across `test()` blocks** (same `webServer`,
  one temp `DATA_DIR` per run); only Elmish client state resets with each
  fresh page. That's why the rename-back cleanup and the banner test's
  explicit clean-baseline-first structure are both load-bearing.
- Convention note (ADR-0059): the destructive-spec CI-gate precedent this
  task establishes is recorded **prose-only, unenforced** — a README
  sentence, not a lint; a future destructive spec relies on review, not
  tooling, to inherit the gate.
- Orchestrator scope check (2026-07-31): one task, no split — all four
  flows share one page, one seeding harness, and one precedent spec file;
  splitting would fragment shared setup for no isolation benefit. No new
  ADR — rides ADR-0027/0034 conventions.

## Worker note (2026-07-31, bounced)
The environment blocker from the first pass (missing `@playwright/test` in
the shared `node_modules`) was fixed by the conductor mid-task (a real
`npm install` at the main-tree level, confirmed via
`require.resolve('@playwright/test')`). Resumed and wrote/iterated the full
spec (`tests/e2e/admin-surgery.spec.ts`, left in place in the worktree, all
four flows plus the CI gate).

**2 of 4 flows are fully written and empirically verified passing** against
a real cold-started (`CI=1`) server: Delete and Rename (including the
load-bearing HTTP rename-back cleanup). Along the way, fixed a real spec bug
of my own: `getEventPage`'s `GlobalPosition`/`StreamPosition` come back over
the wire as **signed STRINGS** (`"+0"`, `"+1"`), not JSON numbers — an
empirically-confirmed `Fable.Remoting.Json`/`FableJsonConverter` int64
encoding quirk, not documented anywhere I could find; normalized via
`Number(...)` in the spec (see the `RawEventDto` comment in the spec file).

**The other 2 required flows (Edit, and the cross-tab dirty banner, which
also drives an Edit) cannot pass — not because the spec is wrong, but
because the Surgery tab's Edit panel genuinely crashes the whole app at
runtime** on this checkout. Root-caused via a `page.on("pageerror")`
listener (not guessed): `select.bordered`/`textarea.bordered` (used in
`StreamDetail/Views.fs` and `AdminSurgery/Views.fs`) don't exist on the
resolved `Feliz.DaisyUI 5.3.0`, Fable treats the FS0039 as non-fatal to
bundle emission (present in `npm run build`'s own log too — just easy to
miss past the asset-size tail — so it silently shipped with
administration-wwc36), and at runtime Fable's placeholder for the
unresolved member throws, which — with no error boundary anywhere in this
app's React tree — unmounts the entire `#feliz-app` root. Confirmed
independent of Playwright via a bare `npx vite` dev server. Filed as
**administration-bq4tw** (`type: bug`), with the exact 4 call sites, the
captured `pageerror`, and a very-likely-trivial fix (grep confirms these are
the *only* four `.bordered` usages in the whole client — probably a pure
deletion, DaisyUI v5 having dropped the v4-era modifier). Per this task's
own scope (a test-writing task, not a production-code task) and explicit
conductor guidance mid-task ("prefer fixing the spec... if you find a
genuine product bug, file it, do NOT patch it here"), I did not touch
`AdminSurgery/Views.fs` or `StreamDetail/Views.fs`.

Bouncing rather than failing outright because this is squarely the
"genuinely unworkable as specified" case the conductor's own message
pre-authorized a BOUNCE for — not an under-refined task, a newly-discovered
external blocker. Added `administration-bq4tw` to `depends_on`. Once that
bug is fixed, this task should need only a quick re-verification pass (the
spec file is already written and 50% empirically green) rather than a
rewrite — re-running `CI=1 npm run test:e2e -- tests/e2e/admin-surgery.spec.ts`
after the fix is the fastest way to confirm.

Remaining acceptance criteria not yet done for this same reason: the BC
README destructive-spec-gate sentence (criterion 6) and the `npm test`
Expecto sanity check (deferred — no code under test changed, low risk, but
left for the next pass since the task isn't complete).
