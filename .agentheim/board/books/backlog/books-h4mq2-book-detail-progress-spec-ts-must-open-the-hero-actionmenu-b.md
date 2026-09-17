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
tags: [books, e2e, test-debt]
related_adrs: [0076]
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

## What

In `tests/e2e/book-detail-progress.spec.ts`, before each
`page.getByRole("button", { name: "Update progress" }).click()` call, open
the hero's action menu first — hover or click its trigger (the ellipsis
button `ActionMenu.heroView` renders, top-right of the hero, same trigger
`Change format`/`Event history`/`Remove book` already sit behind) — then
click the now-visible "Update progress" menu item. Compare with how any
other existing e2e spec that already drives an `ActionMenu.heroView` item
opens the menu first (grep other `*.spec.ts` files for `ActionMenu` or the
ellipsis trigger's accessible name/role) and follow the same pattern for
consistency.

## Acceptance criteria

- [ ] `book-detail-progress.spec.ts`'s two "Update progress" clicks each
      open the hero action menu first, then click the revealed
      "Update progress" item.
- [ ] The spec passes end-to-end against a real dev/CI stack (per its own
      existing `ADR-0027` harness conventions) — not run by this task's
      worker, since worker runs against the live dev database are
      forbidden; verify via the project's normal e2e CI path or a
      throwaway isolated instance.
- [ ] No other assertion in the spec changes — only the two click
      sequences that reach "Update progress".

## Notes

- This is pure test debt from `books-jm7aa`'s intentional UI restructuring
  (ADR-0076-aligned Books/Games parity), not a product regression — the
  popover, its percent/page submission, and the finish-on-100% rule all
  still work; only the DOM path to open the popover changed.
- `books-jm7aa`'s Outcome has the exact button/markup this spec now needs
  to navigate: `ActionMenu.heroView (refreshItem @ restItems)` in
  `BookDetail/Views.fs`, with `Update progress` as the first `restItems`
  entry (`Icon = Some Icons.chartBar`).