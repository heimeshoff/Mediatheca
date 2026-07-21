---
id: design-system-dib4q
title: DaisyUI 5 input-bordered migration — remove the removed modifier from all inputs
status: done
type: bug
context: design-system
created: 2026-07-21
completed: 2026-07-21
depends_on: [design-system-001]
blocks: []
tags: [daisyui, migration, tech-debt, forms, build-health]
related_adrs: []
related_research: []
prior_art: []
---

## Why
DaisyUI 5 **removed the `input-bordered` class** — inputs are bordered by default now. Two stale usages survive from the DaisyUI 4 era, with two different failure modes:

1. **Compile errors (loud):** `src/Client/Pages/EventBrowser/Views.fs` uses the *typed* Feliz.DaisyUI modifier `input.bordered` at lines 126, 175, 189. In Feliz.DaisyUI 5.x the `input` element no longer defines a `bordered` member (only `file.bordered` → `file-input-bordered` remains), so each is an `FS0039: type 'input' does not define a field, constructor or member 'bordered'`. `vite-plugin-fable` treats them as non-fatal so `npm run build` still exits 0 — but the errors print on **every** build and can mask a genuinely new compile error a future change introduces. Confirmed present on plain `main`; not introduced by recent work.

2. **Dead class (silent):** `src/Client/Pages/GameDetail/Views.fs` lines 1463 and 1470 use the *string* form `prop.className "input input-xs input-bordered ..."`. This compiles fine but `input-bordered` is now a no-op CSS class in DaisyUI 5 — same root cause, no error, just a dead token cluttering the className.

Both are one migration: DaisyUI 4 → 5 dropped `input-bordered`.

## What
Remove the obsolete `input-bordered` usage everywhere and confirm the inputs still render with their intended border under DaisyUI 5:

- **EventBrowser/Views.fs** (lines ~126, ~175, ~189): drop the typed `input.bordered` modifier (DaisyUI 5 inputs are bordered by default). If a border is genuinely wanted beyond the default, use the current Feliz.DaisyUI 5 idiom rather than the removed member.
- **GameDetail/Views.fs** (lines ~1463, ~1470): remove the dead `input-bordered` token from the `className` strings, keeping the rest (`input input-xs bg-base-100/50`, `w-20`, etc.) intact.
- Grep the whole client for any other `input-bordered` / `input.bordered` occurrences and fix them in the same pass (note: `file-input-bordered` / `file.bordered` is a *different, still-valid* DaisyUI 5 class — do NOT touch it).

## Acceptance criteria
- [ ] `npm run build` produces **zero** `FS0039 ... 'bordered'` errors (the 3 EventBrowser errors are gone); build still exits 0.
- [ ] No `input-bordered` string or `input.bordered` typed modifier remains anywhere under `src/Client/` (verified by grep); `file-input-bordered`/`file.bordered` usages, if any, are left untouched.
- [ ] The affected inputs (EventBrowser search/filter fields; GameDetail inline number inputs) still render visibly bordered in the running app, consistent with the design system — verified by a design-check pass and a quick visual check.

## Notes
- This is a design-system-owned DaisyUI component-pattern concern that surfaces in two feature BCs' view files (administration's EventBrowser, games' GameDetail) — captured here rather than split, since it's one root cause and the fix should be applied consistently.
- Reference for the current border idiom: the in-app StyleGuide (`src/Client/Pages/StyleGuide`) and `DesignSystem.fs` — reuse whatever input treatment the design system already blesses instead of reintroducing a bespoke border utility.
- Low-risk, mechanical. No new ADR expected. Run the `design-check` skill on the touched views before completion (ADR-0015 frontend gate).

## Outcome
Removed the removed-in-DaisyUI-5 `input-bordered` modifier from every remaining call site: the typed `input.bordered` member on 3 `Daisy.input` elements in `src/Client/Pages/EventBrowser/Views.fs` (search field, From/To date filters — lines ~126, ~175, ~189) and the dead `input-bordered` string token in 2 `prop.className` calls in `src/Client/Pages/GameDetail/Views.fs` (inline play-session date/minutes inputs — lines ~1463, ~1470), keeping the surrounding classes (`input input-xs bg-base-100/50`, `w-20`, etc.) intact. A whole-client grep confirms zero `input-bordered`/`input.bordered` occurrences remain and `file-input-bordered` (a distinct, still-valid DaisyUI 5 class, unused anywhere in this codebase) was left untouched. `npm run build` now transforms cleanly with no `FS0039 ... 'bordered'` errors (previously 3, printed on every build) and exits 0. DaisyUI 5 inputs are bordered by default, so visual appearance of the affected fields is unchanged. No BC README or ADR changes — this is a mechanical removal of a removed API surface, not a new design-system decision.
