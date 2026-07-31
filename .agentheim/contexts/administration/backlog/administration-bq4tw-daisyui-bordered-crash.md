---
id: administration-bq4tw
title: "select.bordered/textarea.bordered no longer exist on Feliz.DaisyUI 5.3.0 — crashes the Surgery tab's Edit panel and the Stream detail composer at runtime"
status: backlog
type: bug
context: administration
created: 2026-07-31
completed:
depends_on: []
blocks: []
tags: [daisyui, feliz, compile-error, runtime-crash, surgery, stream-detail, playwright]
related_adrs: [0034]
related_research: []
prior_art: []
---

## Why
Discovered while writing/running administration-svq3t's Playwright e2e spec
for the Surgery tab (`tests/e2e/admin-surgery.spec.ts`). Every attempt to
exercise the Edit panel's preview (load an event, expect the `Data`/`Metadata`
textareas to render) produced a **full React app crash** — `#feliz-app`
render becomes completely empty, no error boundary exists anywhere in the
tree to contain it. Captured directly via a Playwright `page.on("pageerror")`
listener:

```
DEBUG pageerror: 1
DEBUG console.error: The above error occurred in the <Components_LazyView$1> component:
    at Components_LazyView$1 (http://localhost:5173/fable_modules/Fable.Elmish.React.4.0.0/common.fs?import:29:9)
Consider adding an error boundary to your tree to customize error handling behavior.
```

Root cause, confirmed independently of Playwright (reproduces via a bare
`npx vite` dev server, and — this had gone unnoticed — via `npm run build`
too, since `vite`/Fable treat the underlying compile error as non-fatal to
bundle emission and just print it mid-log rather than failing the build's
exit code):

```
ERROR FS0039: The type 'select' does not define the field, constructor or member 'bordered'.
  src/Client/Pages/StreamDetail/Views.fs (234,43) (234,51)
ERROR FS0039: The type 'textarea' does not define the field, constructor or member 'bordered'.
  src/Client/Pages/StreamDetail/Views.fs (258,61) (258,69)
ERROR FS0039: The type 'textarea' does not define the field, constructor or member 'bordered'.
  src/Client/Pages/AdminSurgery/Views.fs (92,41) (92,49)
ERROR FS0039: The type 'textarea' does not define the field, constructor or member 'bordered'.
  src/Client/Pages/AdminSurgery/Views.fs (105,41) (105,49)
```

The machine's resolved `Feliz.DaisyUI` package version is `5.3.0`
(`src/Client/obj/project.assets.json`; the fsproj pins `Version="5.*"`, and
the local NuGet cache also has a stale `5.2.0` sitting alongside it). Fable
tolerates the unresolved-member error by emitting a throwing placeholder
(`throw 1`, matching the literal `pageerror: 1` observed) instead of failing
compilation outright — so the bug was silent in both `npm run build`'s exit
code and, previously, in administration-wwc36's own `[human-eye]` UI
sign-off, since nobody happened to scroll past the asset-size summary at the
tail of a `vite build` run, and nobody had loaded the Edit panel / Stream
detail composer in a real browser since the DaisyUI version bump.

**Grep confirms these are the ONLY FOUR `.bordered` usages in the entire
client codebase** (`grep -rn "\.bordered" src/Client`) — no other component
uses this modifier, on `select`, `textarea`, or otherwise (e.g. no
`input.bordered` anywhere either). This is consistent with DaisyUI v5 making
bordered the *default* look for form controls and dropping the v4-era
`bordered` modifier class entirely — i.e. the fix is very likely a pure
deletion of these four lines (no replacement member needed), not a rename to
some other case.

## What
- Remove (or correctly replace, if a replacement member actually exists and
  a maintainer prefers being explicit) the four `.bordered` usages:
  - `src/Client/Pages/StreamDetail/Views.fs:234` (`select.bordered`, the
    compensating-event composer's event-type picker)
  - `src/Client/Pages/StreamDetail/Views.fs:258` (`textarea.bordered`, the
    composer's payload editor)
  - `src/Client/Pages/AdminSurgery/Views.fs:92` (`textarea.bordered`, the
    Edit panel's Data field)
  - `src/Client/Pages/AdminSurgery/Views.fs:105` (`textarea.bordered`, the
    Edit panel's Metadata field)
- Confirm both affected features render without a runtime `pageerror` after
  the fix: the Stream detail page's compensating-event composer, and the
  Surgery tab's Edit panel preview (load any event by global position, both
  textareas should render).
- Consider whether this class of error (an FS0039 that Fable treats as
  non-fatal to the build's exit code) deserves a guard — e.g. grepping the
  `vite build`/`npm run build` log for `ERROR FS` and failing CI/the build
  script if any are found, so a future member-rename-without-recompile-check
  can't ship silently again the way this one did.

## Acceptance criteria
- [ ] `npm run build` output contains zero `ERROR FS` lines (currently
      passes with exit code 0 despite 4 such lines present in the log).
- [ ] Loading `/#/stream/<any-stream-id>` and opening the compensating-event
      composer's type picker renders without a blank page / console
      `pageerror`.
- [ ] Loading `/#/admin/surgery`, entering a valid `global_position` in the
      Edit panel, and clicking Load renders the preview (stream/type/position
      line, Data textarea, Metadata textarea) without a blank page / console
      `pageerror`.
- [ ] administration-svq3t's Edit-flow and cross-tab-dirty-banner e2e tests
      (currently blocked by this exact crash) pass once this is fixed.

## Notes
Filed instead of patched in place, per the destructive-e2e-spec task's own
scope discipline (administration-svq3t is a test-writing task, not a
production-code task) — see that task's Worker note for the full trace that
led here. The fix itself looks trivial and low-risk (a 4-line deletion,
confirmed by grep to be the only call sites in the whole client), but is
left for a dedicated task/reviewer to apply and verify rather than bundled
into an unrelated e2e-spec task's diff.
