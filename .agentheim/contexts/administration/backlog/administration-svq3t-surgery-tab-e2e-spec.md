---
id: administration-svq3t
title: Playwright e2e spec for the Surgery tab (edit/delete/rename + confirm dialogs + dirty banner)
status: backlog
type: feature
context: administration
created: 2026-07-22
completed:
depends_on: [administration-wwc36, design-system-q4ebg]
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

**Start from the salvage patch, do not rewrite from scratch.** The full
291-line spec from the bounced pass survives in
`.agentheim/salvage/administration-svq3t-bounced.patch` (ADR-0063 rescue
artifact) with Delete and Rename already empirically green. Verified
2026-07-31 to apply cleanly against current `main`:

```
git apply --include=tests/e2e/admin-surgery.spec.ts \
  .agentheim/salvage/administration-svq3t-bounced.patch
```

The `--include` filter is load-bearing — the patch also carries two stale
task-file moves (a `doing/` path that no longer exists, and the pre-relocation
`administration-bq4tw` file), so an unfiltered `git apply` fails outright.
Recover the spec first, then work the two remaining flows.

Test order inside the file: Edit → Delete → Rename (+ HTTP rename-back
cleanup) → dirty-banner/Rebuild-all last, so the file ends with clean
projections and an unpolluted event-type namespace for whatever spec file
runs next in the same shared server process.

## Acceptance criteria
- [ ] **Destructive-spec safety gate:** the spec skips unless the server run
      is guaranteed isolated — `test.skip(!process.env.CI, ...)` at the top of
      the file/describe block, so the destructive flows can never hit a
      developer's live dev DB via ADR-0027's `reuseExistingServer` convenience
      path. `process.env.CI` is the deliberate choice, not a placeholder: it is
      the exact inverse of the `reuseExistingServer: !process.env.CI` switch in
      `playwright.config.ts` that creates the hazard, so gate and hazard can
      never drift apart. **This repo has no CI pipeline** (no
      `.github/workflows`), so the consequence — accepted by the builder on
      2026-07-31 — is that these four flows are opt-in only and contribute
      nothing to a default `npm run test:e2e` run. Do not "improve" this into
      an inferred-isolation check; that alternative was considered and declined.
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
- [ ] All four flows pass headlessly with `CI` truthy — `$env:CI=1; npm run test:e2e`
      in PowerShell (this project's primary dev shell) or `CI=1 npm run test:e2e`
      in POSIX shell / Git Bash. **The env var, not the shell syntax, is what is
      load-bearing:** `CI=1 npm run …` is POSIX-only and silently fails as a
      command prefix in PowerShell, and without `CI` set criterion 1's gate skips
      the whole file and a green run proves nothing. There is no `cross-env`
      dependency in this project, so both forms are documented rather than one
      being picked. Seeds only its own isolated events into the harness's per-run
      temp `DATA_DIR`, per ADR-0027's existing convention.
- [ ] The BC README's "Playwright e2e harness" bullet gains sentences recording
      **two** harness findings (same place the tj8n2/cx92m/nf3wk findings are
      recorded), so neither is rediscovered: (a) the destructive-spec CI-gate
      precedent and why `process.env.CI` specifically; (b) that
      `IAdminApi`'s int64 fields (`GlobalPosition`, `StreamPosition`) arrive
      over Fable.Remoting as **signed strings** (`"+0"`, `"+1"`), not JSON
      numbers, so any spec reading them must `Number(...)`-normalize. (b) was
      established empirically in the bounced pass and is undocumented upstream —
      it will bite every future spec that touches positions, not just this one.

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
spec (`tests/e2e/admin-surgery.spec.ts`, all four flows plus the CI gate).

> **Stale-pointer correction (modeling, 2026-07-31):** this note originally
> said the spec was "left in place in the worktree". That worktree was torn
> down at the 18:06 session end. The spec is **not** lost — it survives whole
> in `.agentheim/salvage/administration-svq3t-bounced.patch`. See the recovery
> command in `## What`.

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
**administration-bq4tw** (`type: bug`) — since relocated to
**design-system-q4ebg**, see the modeling note below — with the exact 4 call sites, the
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

**Modeling note (2026-07-31, refinement of the blocker):** `administration-bq4tw`
was relocated to **`design-system-q4ebg`** and this task's `depends_on` now
points there. Two corrections to the worker's account above, both established
by verification rather than disagreement: (1) the crash is *not* a `5.2.0 →
5.3.0` regression — `bordered` is absent from `select`/`textarea` in 5.2.0 too,
and `Client.fsproj` has pinned `Feliz.DaisyUI 5.*` since the first commit, so
those four lines were never valid; (2) it is the unfixed half of
`design-system-dib4q`, which cleared every `input.bordered` on 2026-07-21 —
`administration-xjmda` and `administration-wwc36` reintroduced the same class
on sibling element types the very next day. The worker's diagnosis of the
*mechanism* (non-fatal FS0039 → throwing placeholder → unmounted root) was
exactly right and is what made the rest findable. The missing build gate is now
`infrastructure-p1h9a`.

Remaining acceptance criteria not yet done for this same reason: the BC
README destructive-spec-gate sentence (criterion 6) and the `npm test`
Expecto sanity check (deferred — no code under test changed, low risk, but
left for the next pass since the task isn't complete).

## Modeling note (2026-07-31, second refinement)

Refined after the bounce. No split, no new ADR — the four flows still share one
page, one seeding harness, and one spec file, and the task still rides
ADR-0027/0034. Four things changed:

1. **The spec was recovered, not rewritten.** `## What` now carries the exact
   `git apply --include=…` command, verified against current `main` on
   2026-07-31 (`git apply --check` exits 0). Roughly half the remaining work
   was at risk of being redone from scratch on the strength of a stale
   "in the worktree" pointer.
2. **The CI gate is now a recorded decision, not an open shape.** The builder
   was asked directly and chose to keep `test.skip(!process.env.CI, …)`, in
   full knowledge that this repo has no CI and the flows are therefore
   opt-in-only. The declined alternative (gate on an inferred isolation
   invariant — empty store / planted marker — so the flows run by default on
   any cold start) is recorded in criterion 1 so it isn't re-proposed as an
   improvement by the next worker or verifier.
3. **The int64-over-the-wire finding was promoted out of the bounce note** into
   criterion 6. `"+0"`/`"+1"` signed-string encoding of `GlobalPosition`/
   `StreamPosition` is a property of the `IAdminApi` transport, not of this
   spec, and belongs in the README where the next spec author will find it.
4. **Criterion 5 gained its literal command** (`CI=1 npm run test:e2e`). With
   criterion 1's gate in place, a bare `npm run test:e2e` skips the entire file
   and still reports green — a verifier running the old wording could have
   passed the task without executing a single flow.

**Dependency now satisfied.** Both `depends_on` edges are met:
`administration-wwc36` and `design-system-q4ebg` are in `done/`.
`design-system-q4ebg` landed the four-line `.bordered` deletion on `main`
(commit `b59728c`, 2026-07-31), which is exactly the runtime fix this task
needed — the Edit-panel crash that blocked flows 3 and 4 was purely on the
vite/Fable pathway Playwright loads, and that pathway is now clean
(`npm run build`: zero `ERROR FS`). q4ebg's criterion 2 was narrowed to the
`FS0039` scope it owned and resolved independently of `infrastructure-npyhb`'s
separate `FS0193` fix, exactly as anticipated. Nothing left to resolve here.

## Modeling note (2026-07-31, third refinement — post-unblock)

**This is a recover-and-re-verify task, not a write-the-spec task.** Verified
this pass by reading the salvage patch's spec content directly: **all four
flows are already fully written** in
`.agentheim/salvage/administration-svq3t-bounced.patch` — Edit (patch lines
326–356), Delete (358–387), Rename (389–433), and the cross-tab dirty banner
(435–492), each a complete `test(...)` body with real assertions, not a stub.
The bounced worker's own framing confirms it: the flows "cannot pass — *not
because the spec is wrong*, but because the Surgery tab's Edit panel genuinely
crashes." That blocker is now fixed.

Every selector and text string the Edit and banner tests depend on was
re-checked against the **current** `src/Client/Pages/AdminSurgery/Views.fs` and
still matches exactly (panel heading "Edit event", "Data"/"Metadata" labels,
"Save edit…" button, `"Edit event %d on %s"` dialog body, the
`/^Applied — 1 row affected\. Backup: .+/` banner regex). q4ebg's fix deleted
only the `.bordered` modifier from two `Daisy.textarea` calls — no text, no
labels, no structure changed. So nothing the spec asserts on has drifted.

**The only genuinely unstarted work is the last criterion** (the two BC README
sentences) — confirmed still unfulfilled: the README's "Playwright e2e harness"
bullet records the tj8n2/cx92m/nf3wk findings but says nothing yet about the
destructive-spec CI-gate precedent or the signed-string wire quirk.

**Criterion numbering:** this task has **seven** checkbox criteria, not six.
Earlier prose in this file refers to "criterion 6" meaning the README addition —
that is bullet **7** in the actual list (1 safety gate, 2 Edit, 3 Delete,
4 Rename, 5 banner, 6 headless run, 7 README). Read by description, not number.

No split (the four flows share one `git apply`, one file, and one load-bearing
test order — splitting could not even let the halves verify separately). No new
ADR; still rides ADR-0027/0034. The ADR-0059 "prose-only, unenforced" marker in
Notes was re-checked and is correctly worded as-is.
