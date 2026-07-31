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
      `file-input-bordered` the exception).
- [ ] The affected controls still read as bordered and consistent with the
      design system in the running app. [human-eye]

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
