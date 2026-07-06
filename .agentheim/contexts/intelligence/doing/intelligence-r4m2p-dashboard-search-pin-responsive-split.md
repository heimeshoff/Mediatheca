---
id: intelligence-r4m2p
title: Dashboard header search must stay pinned right on every tab; Games/Books split stacks when tight
status: doing
type: bug
context: intelligence
created: 2026-07-06
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, responsive, layout, header, search]
related_adrs: []
related_research: []
prior_art: [intelligence-dq8rk]
---

## Why
The dashboard header is not consistent across its four tabs. On the **Games** tab the
"Search your library" control stays tied to the right edge of the viewport as the window
narrows — correct behaviour. On **All / Movies / TV Series** it slides off-screen to the
right once the viewport gets a little smaller, so the search becomes unreachable without
horizontal scrolling. The header is shared code (`headerLine`, Views.fs:87), so the tabs
should behave identically — the divergence is a bug, not a design difference.

Root-cause hypothesis: the header uses `flex … justify-between` (Views.fs:89), which pins
search to the right of the *content box*, not the viewport. When a tab's content is wider
than the viewport (the full-width TV Series / Movies poster rows overflowing horizontally
at narrow widths), the page container grows past the viewport and carries the
right-aligned search button off-screen with it. The Games tab's content fits, so its
search stays put. The worker should confirm this — whichever tab content overflows
horizontally is the real culprit — and fix the overflow / width containment so the header
never exceeds the viewport width.

Separately, the All-tab **Games / Books** split (`grid grid-cols-1 lg:grid-cols-2`,
Views.fs:1854) stays a two-up row across a range of mid widths where both columns get
cramped. It should prefer stacking into a single column sooner when the screen is tight,
rather than squeezing two columns.

## What
Make the dashboard header identical and viewport-anchored across all four tabs, and make
the All-tab Games/Books split stack to one column when space is tight.

- Search control stays flush to the **viewport's** right edge on every tab at every width.
- Track down and contain the horizontal overflow on the All / Movies / TV Series tabs
  (likely the full-width poster rows) so the page/header never grows wider than the
  viewport.
- Raise / adjust the Games/Books split so it stacks to a single column at tight widths
  instead of remaining a cramped two-column row.

## Acceptance criteria
- [ ] On All, Movies, TV Series, and Games tabs, the "Search your library" control is
      pinned to the right edge of the viewport and fully visible at every viewport width
      down to mobile — it never scrolls off-screen to the right.
- [ ] No tab introduces horizontal page scroll at narrow widths (the page container never
      exceeds the viewport width on any tab).
- [ ] The tabs-left / search-right header layout renders identically across all four tabs.
- [ ] The All-tab Games/Books split renders as a single stacked column at tight/narrow
      widths and only becomes two columns when there is comfortable room for both.
- [ ] `npm run build` is clean (Fable compiles, no type errors).

## Notes
- Shared header: `tabBar` Views.fs:54, `searchLibraryButton` Views.fs:75, `headerLine`
  Views.fs:87 (the `justify-between` row), mounted once for all tabs at Views.fs:4271.
- Games/Books split: `allTabView` Views.fs:1842, the split grid at Views.fs:1854
  (`gamesInFocusPosterSection` + `booksColumnPlaceholder`).
- Full-width poster rows to check for horizontal overflow: `seriesNextUpOpenScroller`
  (Views.fs:1847) and `moviesToWatchPosterSection` (Views.fs:1850) — these are the All-tab
  rows; the Movies/TV tabs have their own scrollers. Look for a scroller that overflows
  its container instead of clipping (`overflow-x-auto` on a `min-w-0` parent is the usual
  fix so the flex row can shrink below its content width).
- Frontend task: styleguide gate met (`depends_on: design-system-001`, done). Verify any
  header/layout change against the live in-app StyleGuide page per ADR-0015.
- Built on top of `intelligence-dq8rk`, which introduced this header + Games/Books split.
