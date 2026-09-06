---
id: intelligence-encn4
title: Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"
status: done
type: refactor
context: intelligence
created: 2026-09-06
completed: 2026-09-06
depends_on: [design-system-001]
blocks: [intelligence-wecjh]
tags: [dashboard, layout, frontend, intelligence]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk, intelligence-r4m2p]
---

## Why
The All tab's first two rows — "Next episode" (TV) and "Movies to Watch" — render as **open
sections**: an icon + title line and then the content, flush with the page, no card. The
Games / Books split below them still carries **velvet-card chrome** from the original 3a
build (`intelligence-dq8rk`), so the page reads as two flat rows followed by two boxes. The
card background adds nothing here — the columns are already separated by the grid gap — and
it breaks the vertical rhythm of the landing page.

The "In Focus" qualifier in the heading is also redundant on this surface. The All tab shows
nothing *but* In Focus games (`gamesInFocusPosterSection` renders exactly the InFocus-status
games), so the section is simply "Games" the way its neighbours are "Movies to Watch" and
"Next episode". Shorter heading, same meaning.

## What
In `src/Client/Pages/Dashboard/Views.fs`, for the **All tab only**:

1. `gamesInFocusPosterSection` (~line 1251) — swap `sectionCard` for `sectionOpen`, and change
   the title from `"Games In Focus"` to `"Games"`. Keep `Icons.gamepad`, keep the
   `grid-cols-2 sm:grid-cols-3` poster grid, keep the `Html.none`-when-empty guard.
2. `booksColumnPlaceholder` (~line 1955) — swap `sectionCard` for `sectionOpen`. Keep
   `Icons.catalog`, the `"Books"` title, and the "Books coming soon." empty state.
3. Leave the two-column `grid grid-cols-1 xl:grid-cols-2 gap-4` wrapper (`allTabView`, ~line
   1978) alone — the responsive split behaviour from `intelligence-r4m2p` must survive.

Out of scope: the Games tab's own sections (`sectionCard Icons.gamepad "New Games"`,
"Recently Played", etc.) keep their cards — this is an All-tab composition change only.

## Acceptance criteria
- [ ] On the All tab, the Games column renders with **no card background, no ring, no card
      padding** — icon + title line then the poster grid, flush with the page, matching the
      "Next episode" and "Movies to Watch" rows above it.
- [ ] On the All tab, the Books column renders the same way — no card background — with the
      "Books coming soon." placeholder text intact.
- [ ] The Games section heading reads **"Games"**, not "Games In Focus", on the All tab.
- [ ] The Games column still hides entirely when there are no In Focus games; the Books column
      still always renders.
- [ ] The Games/Books two-column split still stacks to a single column below `xl`
      (`intelligence-r4m2p` behaviour unchanged).
- [ ] The **Games tab** is untouched — its "Games In Focus" / "New Games" / "Recently Played"
      sections keep their velvet-card chrome and their existing headings.
- [ ] Conforms to the design system, reviewed against the running StyleGuide page. `npm run build`
      is clean.

## Notes
- The open-section helper already exists: `sectionOpen` (~line 177) wraps content in
  `.section-open` (`index.css` ~line 701 — "No background, no border — just structural
  spacing") plus `DesignSystem.animateFadeInUp`. No new helper needed, no CSS change needed.
- **Dead code, captured separately as `intelligence-wecjh`:** `gamesInFocusSection` (~line 629,
  the list-row variant with the same `"Games In Focus"` title) has no callers — and it turned out
  to be one of 43 unreferenced definitions in this file. Do **not** remove or rename it here;
  `intelligence-wecjh` sweeps them all and is sequenced to land after this task.
- Prior art: `intelligence-dq8rk` introduced the Games/Books split and deliberately gave the
  Books placeholder "chrome matching the games column" — that criterion still holds, both
  columns just move to open chrome together. `intelligence-r4m2p` owns the responsive stacking
  of this same split.

## Outcome
In `src/Client/Pages/Dashboard/Views.fs`, for the All-tab-only helpers:
- `gamesInFocusPosterSection` (~line 1250): swapped `sectionCard Icons.gamepad "Games In Focus"`
  for `sectionOpen Icons.gamepad "Games"`. Empty-list guard (`Html.none`) and the
  `grid-cols-2 sm:grid-cols-3` poster grid untouched.
- `booksColumnPlaceholder` (~line 1955): swapped `sectionCard Icons.catalog "Books"` for
  `sectionOpen Icons.catalog "Books"`. "Books coming soon." placeholder text untouched.
- `allTabView`'s two-column `grid grid-cols-1 xl:grid-cols-2 gap-4` wrapper (~line 1978) left
  as-is — `intelligence-r4m2p`'s responsive split behaviour survives unchanged.
- The Games tab's own `sectionCard Icons.gamepad "Games In Focus"` (line 633) was left
  untouched, as was the dead `gamesInFocusSection` list-row variant (~line 629) reserved for
  `intelligence-wecjh`.

Verified via `npm run build` (clean, 196 modules transformed, no Fable/TS errors). No new
production logic to unit-test — pure view-composition swap (two helper-name changes plus a
title string), consistent with how `intelligence-dq8rk` and `intelligence-r4m2p` were verified.
No BC README change — no new ubiquitous language, aggregates, events, or invariants introduced.
