---
id: books-h4mq2
title: book-detail-progress.spec.ts must open the hero ActionMenu before clicking "Update progress"
status: backlog
type: bug
context: books
created: 2026-09-17
completed:
depends_on: []
blocks: []
tags: [books, e2e, test-debt, accessibility]
related_adrs: [0076, 0027]
related_research: []
prior_art: [books-jm7aa]
---

## Why

`books-jm7aa` dissolved the book detail page's "Reading Progress"
`panelCard` (its summary row moved into the hero; its "Update progress"
button became an `ActionMenu.ActionMenuItem` inside the hero's hover-reveal
`ActionMenu.heroView`, per ADR-0076's status lifecycle and the task's own
acceptance criteria). `tests/e2e/book-detail-progress.spec.ts` still does
`page.getByRole("button", { name: "Update progress" }).click()` directly,
assuming the button is always visible on the page — it no longer is; it's
inside a dropdown that only renders after the hero's ellipsis-menu trigger
is clicked (see `ActionMenu.heroView`'s `isOpen`/`setIsOpen` gate,
`src/Client/Components/ActionMenu.fs`). The spec will fail (element not
found / not visible) the next time it runs.

`books-jm7aa`'s own task scope was explicitly restricted to
`src/Client/Pages/BookDetail/Views.fs` (plus `Types.fs`/`State.fs` only if
a message was needed) — no server/Shared/e2e changes — so the worker that
did the restructuring correctly did not touch this file; this is the
follow-up to actually fix it.

**Refinement finding (2026-09-17):** the trigger has no accessible name.
`ActionMenu.heroView` (and its siblings `view` / `heroViewSections`) render
the ellipsis trigger as an `Html.button` whose only child is an inline SVG —
no `aria-label`, no `title`, no text. `getByRole("button", { name: … })`
cannot target it, and no other e2e spec drives an `ActionMenu` item yet
(grep of `tests/e2e/*.spec.ts` for `ActionMenu` / `Change format` /
`Event history` / `Remove …` finds nothing), so there is no existing pattern
to copy. The only robust fix is to give the trigger an accessible name in
the component — an icon-only button without one is also an accessibility
gap on every detail page that uses the menu (movies, series, games, books).
The task therefore touches `ActionMenu.fs` as well as the spec.

## What

1. **`src/Client/Components/ActionMenu.fs`** — add `prop.ariaLabel "More actions"`
   to the trigger `Html.button` in all three menu views (`view`, `heroView`,
   `heroViewSections`). Attribute only: no class, markup, or behaviour change,
   no visual difference (`Sidebar.fs` already uses `prop.ariaLabel` the same
   way for its icon-only buttons).

2. **`tests/e2e/book-detail-progress.spec.ts`** — before each of the two
   `page.getByRole("button", { name: "Update progress" }).click()` calls,
   open the hero menu first:

   ```ts
   await page.getByRole("button", { name: "More actions" }).click();
   await page.getByRole("button", { name: "Update progress" }).click();
   ```

   The hero wrapper is `opacity-0 hover:opacity-100`; Playwright treats an
   `opacity: 0` element as visible and moves the pointer onto it before
   clicking, so no explicit `hover()` is needed. The menu item is an
   `Html.button` labelled by its `Label` text (`renderItem`), so the existing
   "Update progress" role locator keeps working once the menu is open;
   clicking it closes the menu (`setIsOpen false`) before the popover opens.
   Add a one-line comment at the first occurrence pointing at `books-jm7aa`
   (why the menu must be opened first).

3. **Nothing else in the spec changes.** The other locators were checked
   against the post-`books-jm7aa` `BookDetail/Views.fs` and still resolve:
   the status badge still renders `Backlog` / `Finished` (`statusLabel`), the
   hero progress line and the history rows both render `"<n>%"` exactly
   (`.first()` already covers the duplicate), the history row's
   `title="Remove observation"` control is unchanged, and the lowercase
   `finished {date}` hero line does not collide with the exact-match
   `"Finished"` locator.

## Acceptance criteria

- [ ] `ActionMenu.fs`: every trigger `Html.button` in `view`, `heroView`, and
      `heroViewSections` carries `prop.ariaLabel "More actions"`; no other
      line in the file changes.
- [ ] `book-detail-progress.spec.ts`: each of the two "Update progress"
      clicks is immediately preceded by
      `page.getByRole("button", { name: "More actions" }).click()`; the spec
      contains exactly two "More actions" clicks and exactly two
      "Update progress" clicks.
- [ ] No other locator, assertion, or timeout in the spec changes (diff
      confined to the two inserted click lines plus one explanatory comment).
- [ ] `npm run build` and `npm run test:client` stay green.
- [ ] The spec passes end-to-end against an **isolated** stack (ADR-0027
      temp `DATA_DIR` cold start): the worker may run
      `CI=1 npm run test:e2e -- tests/e2e/book-detail-progress.spec.ts`
      — `CI=1` disables `reuseExistingServer`, so Playwright either
      cold-starts its own server + Vite on a temp `DATA_DIR` or errors out
      because ports 5000/5173 are already bound. **Never run it without
      `CI=1`**: that reuses the builder's live dev stack and seeds the real
      database, which is forbidden for workers. If the ports are busy and
      the run cannot start, report the spec as "not run — ports bound" in
      the RESULT and leave the e2e pass to the builder's verification.

## Notes

- This is pure test debt from `books-jm7aa`'s intentional UI restructuring
  (ADR-0076-aligned Books/Games parity), not a product regression — the
  popover, its percent/page submission, and the finish-on-100% rule all
  still work; only the DOM path to open the popover changed.
- `books-jm7aa`'s Outcome has the exact button/markup this spec now needs
  to navigate: `ActionMenu.heroView (refreshItem @ restItems)` in
  `BookDetail/Views.fs`, with `Update progress` as the first `restItems`
  entry (`Icon = Some Icons.chartBar`).
- The `aria-label` value is `"More actions"` — a plain generic name; the
  StyleGuide's ActionMenu specimen (`design-system-003`) renders the same
  component, so it picks the label up automatically with no specimen edit.
- Assumption made during refinement (builder can override): adding the
  accessible name to the shared component was preferred over a CSS-class
  locator in the spec (`div.absolute.top-4.right-4 button`), which would be
  brittle and would leave the accessibility gap in place.
- Any future spec that drives a `Change format` / `Event history` /
  `Remove …` menu item should open the menu the same way — this is the
  first and reference example.
