---
id: design-system-hs4vm
title: Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail
status: todo
type: bug
context: design-system
created: 2026-09-06
completed:
depends_on: []
blocks: []
tags: [ui, css, scrollbar, dashboard, styleguide]
related_adrs: [0015]
related_research: []
prior_art: []
---

## Why

Every horizontal poster rail on the Dashboard shows a native OS scrollbar under it — most
visibly under **Series Next Up (episodes)** and the **movie rails**, which is what the
builder reported. The rails are already `snap-x snap-mandatory` filmstrips; a browser
scrollbar sitting under the posters is page chrome the design system never asked for and
breaks the flush, editorial feel of the Velvet Lobby surfaces.

The cause is a dead dependency, not a styling oversight. All six rails carry:

```
scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent
```

Those are **`tailwind-scrollbar` plugin utilities**, and the plugin is not installed —
there is no `tailwind-scrollbar` entry in `package.json` and no matching `@plugin`
directive in `src/Client/index.css` (which loads only `daisyui` and `daisyui/theme`).
Tailwind 4 emits nothing for them, so all three classes are inert string noise and the
browser falls back to its default scrollbar. Someone styled the scrollbar in intent only;
the styling never reached the page.

Scrollbar chrome is cross-cutting visual language, so the fix belongs in design-system as
a named primitive rather than as six inline patches in the Dashboard — any future rail in
any BC then gets it for free, and the dead plugin classes stop propagating by copy-paste.

## What

Mint a hidden-scrollbar primitive in the design system and adopt it at the six existing
call sites.

**The primitive** — a `.scrollbar-hidden` rule in `src/Client/index.css` plus a
`DesignSystem.scrollbarHidden` class-string export in `src/Client/DesignSystem.fs`, in the
same shape as the other class-string compositions (`underlineTab`, `filterPill`): the
composition owns only the look, the caller keeps its own `overflow-x-auto` / `snap-*`
layout classes.

It must hide the scrollbar in **both** engines the app ships on — Chromium (dev + the
Photino shell on Windows) and WebKitGTK (the Photino shell on Linux, ADR-0018):

- `scrollbar-width: none` — Firefox and the modern standard property
- `::-webkit-scrollbar { display: none }` — Chromium / WebKit

**The adoption** — replace the three dead `scrollbar-*` classes with the new primitive at
all six rails in `src/Client/Pages/Dashboard/Views.fs` (lines 415, 1084, 1122, 1633, 2306,
2321 at capture time: Series Next Up open scroller, Recently Watched, Movies to Watch,
Series tab Next Up, Recently Played, Recently Added). Nothing else in `src/Client/`
references `scrollbar-*`, so this retires the dead classes tree-wide.

**Do not install `tailwind-scrollbar`.** The design system's answer to scrollbar chrome is
"no scrollbar on a snap rail", not "a thinner scrollbar" — a plain CSS rule is the whole
requirement, and a plugin dependency for two declarations is not worth carrying.

## Acceptance criteria

- [ ] `.scrollbar-hidden` exists in `src/Client/index.css` with both `scrollbar-width: none`
      and a `::-webkit-scrollbar { display: none }` rule.
- [ ] `DesignSystem.scrollbarHidden` exports the class string, following the existing
      class-string composition convention in `DesignSystem.fs`.
- [ ] All six rails in `src/Client/Pages/Dashboard/Views.fs` use the primitive; their
      `overflow-x-auto`, `snap-x`, `snap-mandatory`, `scroll-px-2` and spacing classes are
      unchanged.
- [ ] `grep -rn "scrollbar-thin\|scrollbar-thumb\|scrollbar-track" src/Client/` (excluding
      `fable_modules/`) returns nothing — the dead plugin classes are gone tree-wide.
- [ ] No `tailwind-scrollbar` dependency is added to `package.json`, and no new `@plugin`
      directive is added to `index.css`.
- [ ] Scrolling still works by every non-scrollbar means at each rail: mouse wheel /
      trackpad, touch drag, and keyboard (the rail is still focusable and arrow keys still
      move it) — hiding the bar must not disable the scroll.
- [ ] The StyleGuide page names the primitive (a line or specimen under the existing rail /
      layout material), per ADR-0015 — the running StyleGuide is the authoritative artifact,
      so an unlisted primitive is an undocumented one.
- [ ] `npm run build` compiles clean and `npm test` passes.

## Notes

- **Accessibility.** Hiding a scrollbar removes a visible affordance that content extends
  beyond the viewport edge. It is acceptable here because these rails are snap filmstrips
  whose partially-clipped next poster is itself the overflow signal, and because every
  scroll input is preserved (criterion above). Do **not** apply this primitive to a
  vertical scroll region or to any container where a clipped-edge cue is absent — that is
  a rule worth stating alongside the primitive in `DesignSystem.fs`.
- **Not a Tailwind arbitrary variant.** Tailwind 4 can express `[&::-webkit-scrollbar]:hidden`
  inline, but the design system's convention is a named class in `index.css` fronted by a
  `DesignSystem.fs` export — keep it consistent with `.underline-tab` / `.filter-pill`
  rather than inlining an arbitrary variant at six call sites.
- **Scope boundary.** This task ships the primitive and retires the dead classes at the six
  known call sites. Applying it to rails that may appear on catalog or detail pages later is
  the owning BC's job, per the design-system README's standing rule ("design-system ships
  the primitive, the owning BC wires the application") — the six Dashboard sites are
  included here only because they are the existing carriers of the dead classes this task
  removes.
