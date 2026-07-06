---
id: intelligence-dq8rk
title: Dashboard All-tab 3a layout — underline tabs + library search, media rows, games/books split
status: backlog
type: feature
context: intelligence
created: 2026-07-06
completed:
depends_on: [design-system-001, design-system-k9p3v]
blocks: []
tags: [dashboard, layout, frontend, 3a]
related_adrs: []
related_research: []
prior_art: []
---

## Why
The current dashboard All-tab is analytics-heavy — it opens with an Activity section
(heatmap + 12-month breakdown), carries cross-media/play-time summary stats, and buries the
"what's next" media the vision says the dashboard exists to surface. Direction **3a** in
`Mediatheca Directions.html` shows a leaner landing page: a single inline header line, then
media rows, with charts stripped out. This task brings the All-tab layout in line with 3a so
the dashboard reads as an intent-driven "what should I watch/play right now" surface, not a
stats console. (Vision: *Unified Dashboard → All tab*; design principle *intent-driven, not a
catalog*.)

## What
Restructure the Dashboard **All tab** and its shared header (`src/Client/Pages/Dashboard/Views.fs`)
to match 3a:

1. **Header line (replaces the title + tab-bar block).** Drop the "Dashboard" `<h1>` page
   title (`view`, ~line 4281). Re-point the four tabs (`tabBar`, line 54) from filled pills onto
   the **reusable underline-tab pattern** delivered by `design-system-k9p3v` (text tabs, gold
   underline on the active tab). Put the **"Search your library"** control on the *same line*,
   right-aligned, wired to the existing library search.

2. **Strip the analytics.** Remove the **Activity** section (`activitySection`, line 1800 —
   heatmap + monthly breakdown) and the **games 14-day play chart + summary stats**
   (`gamesRecentlyPlayedChartWithStats`) / any yearly play+watch-time totals from the All tab.

3. **Media rows.** All-tab content becomes a **full-width TV Series row** (Next Up) followed by
   a **full-width Movies row** (to-watch). **Drop the `heroSpotlight`** — the TV Series row is a
   pure, equal-weight poster row (no featured lead card). *(Resolved at refine.)*

4. **Games / Books split.** Below the rows, a two-column area:
   - **Left — Games:** the games In Focus + recently-played you're actively engaging with
     (`gamesInFocusPosterSection`). **`newGamesSection` is dropped from the All tab** — recently-
     added games stay available on the dedicated Games tab. *(Resolved at refine.)*
   - **Right — Books placeholder:** a labelled "Books" card matching the games column's chrome,
     in a quiet empty / "coming soon" state (Books is a v2 feature — placeholder only). This
     replaces 3a's recently-played list on the right.

## Acceptance criteria
- [ ] The "Dashboard" `<h1>` page title is gone from the All tab.
- [ ] The four tabs render using the shared **underline-tab pattern** (`design-system-k9p3v`) — text tabs with a gold underline on the active tab, no filled pill/button chrome. The dashboard does **not** carry bespoke tab CSS; it consumes the DesignSystem composition.
- [ ] A "Search your library" control sits on the **same top line** as the tabs, right-aligned, and triggers the existing working library search (not a dummy).
- [ ] The Activity section (heatmap + monthly breakdown) no longer renders on the All tab.
- [ ] The games 14-day play chart / summary-stats block and any yearly cross-media play-time & watch-time totals no longer render on the All tab.
- [ ] All-tab body order is: full-width **TV Series** row → full-width **Movies** row → two-column **Games (left) / Books (right)** area.
- [ ] The TV Series row is a pure full-width poster row (Next Up) — **no `heroSpotlight` lead card**.
- [ ] The Games column shows In Focus + recently-played games only — **no `newGamesSection`**, no 14-day activity chart / summary-stats block.
- [ ] The Books column is a labelled placeholder card with an empty/coming-soon state, matching the games column's chrome.
- [ ] Movies / TV Series / Games tabs still function; only the All-tab composition and the shared tab/header change.
- [ ] Conforms to the design system (typography, velvet-card/paper-overlay, `DesignSystem.fs`), reviewed on the running StyleGuide page. `npm run build` is clean.

## Notes
- **Reference:** direction **3a** in `Mediatheca Directions.html` (the captured design session in
  repo root). The underline accent is the Velvet Lobby secondary/gold.
- **Tab pattern is now a dependency, not inline work.** Per the refine decision, the orange-
  underline tab was promoted to a reusable design-system component (`design-system-k9p3v`,
  mirroring the 3a sidebar → ADR-0014 precedent). This task **consumes** it: re-point `tabBar`
  onto the shared composition. Do not re-style tabs inline here.
- **Search wiring (cross-MVU — important).** The library search modal lives in the **root** MVU
  (`model.SearchModal`, `src/Client/Views.fs`), not the Dashboard page. Sibling pages open it by
  dispatching their **own** `Open_search_modal` message that root `State.fs` intercepts and turns
  into `SearchModal = Some (SearchModal.initWithGames …)` (see Games/Movies/Series in
  `src/Client/State.fs`, and the global Ctrl/Cmd-K handler in `Views.fs`). Follow that exact
  pattern: add an `Open_search_modal` case to `Pages.Dashboard.Types.Msg`, intercept it in the
  root `Dashboard_msg` branch of `State.fs` to open the modal. Do **not** reimplement search.
- **Books placeholder shape:** mirror the existing `placeholderTab` chrome
  (`DesignSystem.velvetCard` + quiet empty text, line 4248). No Books events/API — visual stub only.
- **Current structure to edit:** `allTabView` (`Views.fs:1815`) — currently `activitySection`,
  then a `lg:grid-cols-3` split with hero/Next-Up/Movies on the left and games-chart/in-focus/
  new-games on the right. Collapses to: header line, TV row, Movies row, Games/Books two-column.
  The page shell (`view`, ~line 4273) holds the title + `tabBar`.
