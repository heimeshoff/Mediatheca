---
id: intelligence-t8n3q
title: Dashboard library-search control needs a hover affordance — pointer cursor and a "Ctrl + K" tooltip
status: todo
type: bug
context: intelligence
created: 2026-08-07
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, search, header, hover, affordance, keyboard-shortcut]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk, intelligence-r4m2p]
---

## Why
The "Search your library" control in the dashboard header is a real `Html.button`
(`searchLibraryButton`, `src/Client/Pages/Dashboard/Views.fs:75`) that opens the Ctrl+K
search modal — but it does not read as clickable on hover. Browsers default `<button>` to
`cursor: default`, and the control carries no `cursor-pointer`, so the mouse pointer stays
an arrow over it. Everything else clickable in the app already uses `cursor-pointer`
(152 occurrences across 20 client files) — this control is the outlier.

Second gap: the modal is bound to Ctrl+K (`src/Client/Views.fs:16`), but nothing on the
dashboard tells the user that. The shortcut is discoverable only by reading the source.

## What
Give the dashboard's library-search control the two hover affordances it is missing:

1. **Pointer cursor** — add `cursor-pointer` to the button's class list so the mouse turns
   to a pointer on hover, matching every other clickable surface in the app.
2. **Native tooltip** — add `prop.title "Ctrl + K"` so hovering surfaces the keyboard
   shortcut.

Deliberately the **native `title` attribute**, not a styled tooltip: the project has no
tooltip pattern in `DesignSystem.fs` today, and the builder chose not to mint one for this
(2026-08-07). This task must not introduce a new floating-surface pattern — that would be
design-system work behind the styleguide gate, and it is explicitly out of scope here.

Everything else about the control stays as it is: the existing
`hover:text-base-content hover:bg-base-300/40 transition-colors` treatment, the magnifying-glass
icon, the label, the `Open_search_modal` dispatch.

## Acceptance criteria
- [ ] `searchLibraryButton` in `src/Client/Pages/Dashboard/Views.fs` carries `cursor-pointer`
      in its `prop.className`.
- [ ] The same button carries `prop.title "Ctrl + K"` (exactly that string — spaces around
      the `+`, matching the label the builder asked for).
- [ ] No other change to the button: the existing hover colour/background classes, the
      `transition-colors`, the icon, the "Search your library" label, and the
      `dispatch Open_search_modal` handler are all untouched.
- [ ] No tooltip component, CSS class, or `DesignSystem.fs` helper is added — the tooltip is
      the browser-native `title` attribute only.
- [ ] `npm run build` completes without new warnings or errors.
- [ ] Hovering the control on the running dashboard shows the pointer cursor and, after the
      browser's usual delay, a tooltip reading "Ctrl + K". [human-eye]

## Notes
- **Why the last criterion is `[human-eye]` (ADR-0061):** a native `title` tooltip is rendered
  by the browser/OS, not the DOM — it cannot be asserted in Playwright or a unit test. The
  *attribute* is machine-checkable (criterion 2); the tooltip actually appearing is not.
  Do not invent a proxy for it.
- **The shortcut binding is `ctrlKey || metaKey`** (`src/Client/Views.fs:16`), so ⌘K also
  works on macOS. The label stays "Ctrl + K" per the builder's ask — Mediatheca is a
  Windows/Docker self-hosted app (ADR-0018), so Ctrl is the right thing to name.
- Frontend task → `depends_on: [design-system-001]` per the design-system styleguide gate
  (ADR-0015). The gate is satisfied by staying inside existing patterns: this adds one
  Tailwind utility already used across the app and one HTML attribute, no new design vocabulary.
- Prior art on this exact control: `intelligence-dq8rk` introduced it (All-tab 3a layout),
  `intelligence-r4m2p` fixed its right-pinning across tabs.
