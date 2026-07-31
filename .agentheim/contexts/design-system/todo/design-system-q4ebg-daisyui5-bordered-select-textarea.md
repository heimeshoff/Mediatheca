---
id: design-system-q4ebg
title: "DaisyUI 5 dropped the whole `bordered` modifier family — four surviving `select.bordered`/`textarea.bordered` call sites emit FS0039 and throw at render, blanking the SPA root (reintroduced one day after design-system-dib4q fixed the `input` half)"
status: todo
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
here — it is globally-true build tooling, split out as `infrastructure-p1h9a`,
which `depends_on` this task. Note that this task alone no longer leaves that
gate "an already-clean tree", as p1h9a's Notes originally assumed: p1h9a's
mechanism is `dotnet build`, which stays red on `FS0193` until
`infrastructure-npyhb` lands. p1h9a has been given the matching second
`depends_on` edge; nothing about that changes this task's own scope.

## Acceptance criteria

- [ ] `grep -rn "\.bordered" src/Client --include=*.fs` returns zero matches
      (any `file.bordered` introduced later is exempt and must be called out).
- [ ] `dotnet build src/Client/Client.fsproj` emits zero `error FS0039` lines.
      (Baseline today: 16, all four sites, each reported multiple times.)
      **Narrowed 2026-07-31 — this criterion no longer requires exit 0.** With
      the `FS0039`s cleared, the build fails on a pre-existing, previously-masked
      `FS0193` that this task does not own and cannot fix: `Feliz.DaisyUI` 5.3.0's
      prebuilt dll was compiled against Feliz 3.1.1 and binds against the pinned
      Feliz 2.9.0. That is `infrastructure-npyhb`'s (ADR-0036). This task is
      **not** blocked on it and gains no `depends_on` edge.
- [ ] `npm run build` output contains zero `ERROR FS` lines. **This is the
      pathway that ships** — vite-plugin-fable compiles DaisyUI from
      `fable/*.fs` sources and never links the dll that carries the `FS0193`,
      so a clean `npm run build` is the real proof the runtime crash is fixed.
      (Baseline today: exit 0 *despite* the errors — which is exactly the hole
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

## Bounce record — resolved 2026-07-31

Bounced not because the deletion was under-specified — it was exact, mechanical,
and verified correct — but because the then-criterion 2 (`dotnet build` exits 0
with zero `error FS` lines) **could not be satisfied by this task alone**, which
was only discoverable by doing the deletion. That is now settled: criterion 2 has
been narrowed to the `FS0039`s this task owns, `FS0193` travels with
`infrastructure-npyhb` (ADR-0036), and **this task gains no `depends_on` edge**.
It is workable as it stands.

### The deletion is already done — recover it, don't redo it

The worker's four-line deletion survives whole. **The worktree it was written in
was torn down at the 18:06 session end** — the earlier claim that the files were
"left uncommitted in the worktree" is stale and must not be relied on. The
evidence lives in the salvage patch (ADR-0063), and this recovery command was
verified against current `main` this session (`git apply --check` exits 0):

```bash
git apply --include=src/Client/Pages/StreamDetail/Views.fs \
          --include=src/Client/Pages/AdminSurgery/Views.fs \
          .agentheim/salvage/design-system-q4ebg-bounced.patch
```

The `--include` filter is **load-bearing**: the patch also carries a task-file
move and a drafted `design-system-vh931` task file that must not be applied
(see Notes).

### What the worker verified

- `grep -rn "\.bordered" src/Client --include=*.fs` → zero matches. ✅
- `npm run build` → `✓ built`, exit 0, **zero** `ERROR FS` lines (`grep -c
  "ERROR FS"` → 0). The pathway that ships is fully clean, so the `throw 1`
  placeholder blanking `#feliz-app` is fixed. ✅
- The `FS0193` discovery was confirmed deterministic, not flaky: `git stash`
  reproduces the 16-`FS0039` baseline with **no** `FS0193`; `git stash pop` +
  rebuild reproduces `FS0193` across repeated runs. The `FS0039`s were aborting
  the compile pass before it reached the binding failure.
- The two runtime criteria were **not** exercised — that worker's toolset had no
  `chrome-devtools` MCP. Still open; see Notes for how to close them.

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
- **Closing the two runtime criteria.** The bounced worker had no browser
  tooling and correctly reported them unverified rather than proxying them.
  A worker likewise without `chrome-devtools` must do the same — say "not
  exercised", never infer them from a green build, since a clean bundle is
  exactly what this bug already had. The project's `chrome-devtools` MCP is
  available in a main session, so the practical close is a builder/main-session
  pass over `/#/admin/streams/<id>` and `/#/admin/surgery` after the deletion
  lands.
- **Do not create `design-system-vh931`.** The bounced worker drafted such a
  task (it is inside the salvage patch, never landed on `main`, and exists in no
  BC) to carry the `FS0193` finding forward. That finding has since been folded
  into `infrastructure-npyhb` — retargeted from spike to chore, with ADR-0036 —
  and into this task's narrowed criterion 2. Creating vh931 now would duplicate
  both.
- Adjacent and deliberately out of scope, now fully diagnosed:
  `infrastructure-npyhb` (ADR-0036) re-pins `Feliz.DaisyUI` to the exact `5.2.0`,
  the last release built against the Feliz 2 line, clearing both `NU1605` and the
  `FS0193` it causes. Its Fable sources are byte-identical to 5.3.0's, so the
  shipped bundle is provably unchanged — and `.bordered` is absent from
  `select`/`textarea` in **both** versions, so the re-pin does not resurrect the
  member this task deletes. The two tasks are independent and can land in either
  order.
