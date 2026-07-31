---
id: design-system-q4ebg
title: "DaisyUI 5 dropped the whole `bordered` modifier family — four surviving `select.bordered`/`textarea.bordered` call sites emit FS0039 and throw at render, blanking the SPA root (reintroduced one day after design-system-dib4q fixed the `input` half)"
status: backlog
type: bug
context: design-system
created: 2026-07-31
completed:
depends_on: [design-system-001]
blocks: [administration-svq3t]
tags: [daisyui, feliz, forms, build-health, tech-debt, runtime-crash]
related_adrs: [0015, 0016]
related_research: []
prior_art: [design-system-dib4q]
---

## Why

Four call sites use `select.bordered` / `textarea.bordered`. Neither member
exists on Feliz.DaisyUI 5.x, so each is an `FS0039`. `vite-plugin-fable`
treats FS0039 as non-fatal to bundle emission and emits a throwing placeholder
(`throw 1`) in its place. At render the placeholder throws, and since there is
no error boundary anywhere in the tree, the whole `#feliz-app` root unmounts —
a blank page, not a degraded control.

**This is the second half of `design-system-dib4q`, reintroduced one day after
that task closed.** The timeline is the point:

| Date | Event |
|---|---|
| 2026-07-21 | `design-system-dib4q` removes every `input.bordered`; build goes clean. Its Notes warn the errors "can mask a genuinely new compile error a future change introduces." |
| 2026-07-22 | `administration-xjmda` ships `select.bordered` (StreamDetail composer, `Views.fs:234`) + `textarea.bordered` (`Views.fs:258`) |
| 2026-07-22 | `administration-wwc36` ships two more `textarea.bordered` (AdminSurgery `Views.fs:92`, `:105`) |

dib4q fixed `input` by grep and left the build clean; it could not stop the
same mistake landing on sibling element types the next day, because a one-time
cleanup is not a gate. Both affected features have therefore been **dead on
arrival since the day they shipped** — the compensating-event composer's type
picker and payload editor, and the Surgery tab's Edit panel.

**Correcting the original capture's root-cause claim:** this is *not* a
regression from a `5.2.0 → 5.3.0` version bump. `bordered` is absent from
`select`/`textarea` in the locally cached **5.2.0 as well** (verified against
`~/.nuget/packages/feliz.daisyui/{5.2.0,5.3.0}/fable/Modifiers.fs` — both
expose only `ghost`, the eight colors, and `xs…xl`), and `Client.fsproj` has
pinned `Feliz.DaisyUI Version="5.*"` since the project's first commit. These
four lines were never valid in this project's history. DaisyUI 5 made bordered
the default look for form controls and dropped the v4-era `bordered` modifier
across the family — so the fix is a **pure deletion**, with no replacement
member to reach for.

## What

Delete the four `.bordered` modifiers. A whole-client grep
(`grep -rn "\.bordered" src/Client --include=*.fs`) confirms these are the only
remaining occurrences — dib4q already cleared the `input` ones, and
`file.bordered` → `file-input-bordered` (a distinct, still-valid DaisyUI 5
class) is unused in this codebase.

- `src/Client/Pages/StreamDetail/Views.fs:234` — `select.bordered`, the
  compensating-event composer's event-type picker
- `src/Client/Pages/StreamDetail/Views.fs:258` — `textarea.bordered`, the
  composer's payload editor
- `src/Client/Pages/AdminSurgery/Views.fs:92` — `textarea.bordered`, the Edit
  panel's Data field
- `src/Client/Pages/AdminSurgery/Views.fs:105` — `textarea.bordered`, the Edit
  panel's Metadata field

Then record the family-level rule in this BC's README so a third reintroduction
has something to hit: DaisyUI 5 retired the `bordered` modifier on `input`,
`select`, and `textarea` alike — form controls are bordered by default, and
`file-input-bordered` is the sole survivor.

The build gate that would have caught this is deliberately **not** in scope
here — it is globally-true build tooling, split out as `infrastructure-p1h9a`
(which `depends_on` this task, so the gate lands on an already-clean tree).

## Acceptance criteria

- [ ] `grep -rn "\.bordered" src/Client --include=*.fs` returns zero matches
      (any `file.bordered` introduced later is exempt and must be called out).
- [ ] `dotnet build src/Client/Client.fsproj` exits 0 with zero `error FS`
      lines. (Baseline today: exit 1, 16 errors — all four sites, each
      reported multiple times.)
- [ ] `npm run build` output contains zero `ERROR FS` lines. (Baseline today:
      exit 0 *despite* the errors — which is exactly the hole
      `infrastructure-p1h9a` closes.)
- [ ] Loading `/#/admin/streams/<any-stream-id>` and opening the
      compensating-event composer's type picker renders the picker and payload
      editor with no `pageerror` and a non-empty `#feliz-app`.
- [ ] Loading `/#/admin/surgery`, entering a valid `global_position` in the
      Edit panel and clicking Load renders the preview (stream/type/position
      line + both textareas) with no `pageerror` and a non-empty `#feliz-app`.
- [ ] The design-system README's DaisyUI component-pattern language states the
      family-level rule (`bordered` retired on input/select/textarea;
      `file-input-bordered` the exception). **Prose-only, unenforced** in this
      task by design (ADR-0059): the standing enforcement is the build gate in
      `infrastructure-p1h9a`, which `depends_on` this task and therefore cannot
      ship inside it. The grep criterion above is a one-time check, not a lint —
      that distinction is precisely what let `design-system-dib4q`'s fix regress.
- [ ] The affected controls still read as bordered and consistent with the
      design system in the running app. [human-eye]

## Worker note

Bounced, not because the deletion itself was under-specified — it was exact,
mechanical, and verified correct — but because **acceptance criterion 2
(`dotnet build src/Client/Client.fsproj` exits 0 with zero `error FS` lines)
cannot be satisfied by this task alone**, and that was only discoverable by
actually doing the deletion.

**What was done and verified (evidence, left uncommitted in the worktree —
`src/Client/Pages/StreamDetail/Views.fs`, `src/Client/Pages/AdminSurgery/Views.fs`):**
the four `select.bordered` / `textarea.bordered` modifiers named in the task
were deleted exactly as specified.
- `grep -rn "\.bordered" src/Client --include=*.fs` → zero matches. ✅
- `npm run build` (the actual deploy pathway) → `✓ built`, exit 0, **zero**
  `ERROR FS` / ` error FS` lines (confirmed via `grep -c "ERROR FS"` on full
  output → 0). This is the pathway that ships, and it is now fully clean —
  the runtime crash this task exists to fix (`throw 1` placeholder blanking
  `#feliz-app`) is fixed. ✅
- Runtime criteria (loading `/#/admin/streams/<id>` and `/#/admin/surgery`
  with no `pageerror`) were **not** exercised — no `chrome-devtools` MCP tool
  was available in this worker's toolset (Read/Write/Edit/Grep/Glob/Bash/Agent
  only). Build-level verification only; say so plainly per the task's own
  runtime-criteria guidance.

**What blocks the task:** with the four sites deleted, `dotnet build
src/Client/Client.fsproj` does **not** exit 0. It fails with a single,
different, pre-existing error that was previously masked by the 16 `FS0039`
`.bordered` errors:

```
FSC : error FS0193: The module/namespace 'Feliz' from compilation unit
'Feliz' did not contain the namespace, module or type 'HtmlHelper'
```

This is the exact latent hazard `infrastructure-npyhb` already describes and
filed as a spike: `Client.fsproj` pins `Feliz 2.*` while `Feliz.DaisyUI 5.3.0`
requires `Feliz >= 3.1.1`, so NuGet silently resolves `Feliz` down to `2.9.0`
(`NU1605`, currently a warning-only build note). Confirmed deterministic, not
flaky: `git stash` (reverting the deletion) reproduces the documented
baseline — exactly 16 `FS0039` errors, **no** `FS0193` — and `git stash pop`
+ rebuild reproduces `FS0193` consistently across repeated runs. The
`FS0039` errors were apparently aborting `dotnet build`'s compile pass before
it reached whatever triggers the `HtmlHelper` resolution failure; clearing
them exposes it.

**This invalidates two adjacent tasks' stated assumptions**, which the next
refinement pass should reconcile:
- `infrastructure-npyhb` states "Nothing is currently known to be broken by
  this" — that is no longer true. The downgrade actively breaks
  `dotnet build`, not just a NuGet warning, once the `.bordered` errors are
  out of the way.
- `infrastructure-p1h9a` (the build gate, `depends_on: [design-system-q4ebg]`)
  assumes this task leaves it "an already-clean tree" — it does not; `dotnet
  build` still fails, now on `FS0193`.

**Recommendation for re-refinement:** either (a) resequence so
`infrastructure-npyhb` lands before this task closes and this task formally
`depends_on` it for criterion 2, or (b) narrow criterion 2's wording to the
`.bordered`/`FS0039` errors this task actually owns (matching what `npm run
build`, the real deploy pathway, already fully clears) and let `FS0193`
travel with `infrastructure-npyhb`/`infrastructure-p1h9a` instead. Filed
`design-system-<new>` (see conductor's `NEW_BACKLOG_ITEMS`) to carry this
finding forward without touching either infrastructure task file directly
(worker scope rule: don't touch task files other than the one assigned).

The two edited view files are left as-is (uncommitted) in this worktree as
verified-correct evidence for whoever re-refines and re-dispatches this task
— the deletion does not need to be redone, only the `dotnet build` criterion
needs to be reconciled with `infrastructure-npyhb`.

## Notes

- Relocated from `administration-bq4tw` during refinement, following dib4q's
  explicit precedent: a DaisyUI component-pattern concern is design-system-owned
  even when every call site sits in another BC's view files. The four sites are
  all administration views; the *knowledge* is design-system's, and parking it
  there is what stops a third recurrence.
- Verification aid: `.agentheim/salvage/administration-svq3t-bounced.patch`
  holds the bounced Surgery e2e spec with 2 of 4 flows empirically green. It is
  a rescue artifact (ADR-0063), not a deliverable — the remaining two flows are
  `administration-svq3t`'s to land, not this task's.
- Run the `design-check` skill on both touched view files before completion
  (ADR-0015 frontend gate).
- Low-risk and mechanical. No new ADR expected — this is removal of an API
  surface that no longer exists, not a design decision.
- Adjacent but deliberately out of scope: `NU1605` reports Feliz downgraded
  `3.1.1 → 2.9.0` (Feliz.DaisyUI 5.3.0 wants `>= 3.1.1`; `Client.fsproj` pins
  `Feliz 2.*`). Filed as `infrastructure-npyhb`.
