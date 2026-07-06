---
id: intelligence-dq8rk
title: Dashboard All-tab 3a layout — inline orange-underline tabs + search, media rows, games/books split
status: backlog
type: feature
context: intelligence
created: 2026-07-06
completed:
depends_on: [design-system-001]
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
   title. Restyle the four tabs (All / Movies / TV Series / Games) from filled pill buttons to
   3a's **text tabs with an orange underline** under the active tab. Put the **"Search your
   library"** control on the *same line*, right-aligned, wired to the existing library search
   (`Components/SearchModal.fs` / `searchLibrary`).

2. **Strip the analytics.** Remove the **Activity** section (heatmap + monthly breakdown,
   `activitySection`) and the **yearly / play+watch-time summary** (the play-session summary
   stats; any 12-month/yearly totals) from the All tab.

3. **Media rows.** All-tab content becomes a **full-width TV Series row** (Next Up), followed
   by a **full-width Movies row** (to-watch).

4. **Games / Books split.** Below the rows, a two-column area:
   - **Left — Games:** the games in focus / recently-played you're actively engaging with.
   - **Right — Books placeholder:** a labelled "Books" card matching the games column's chrome,
     in a quiet empty / "coming soon" state (Books is a v2 feature — placeholder only). This
     replaces 3a's recently-played list on the right.

## Acceptance criteria
- [ ] The "Dashboard" `<h1>` page title is gone from the All tab.
- [ ] The four tabs render as 3a text tabs with an **orange (Velvet Lobby secondary/gold) underline** on the active tab — no filled pill/button chrome.
- [ ] A "Search your library" control sits on the **same top line** as the tabs, right-aligned, and triggers the existing working library search (not a dummy).
- [ ] The Activity section (heatmap + monthly breakdown) no longer renders on the All tab.
- [ ] The yearly/cross-media play-time & watch-time summary stats no longer render on the All tab.
- [ ] All-tab body order is: full-width **TV Series** row → full-width **Movies** row → two-column **Games (left) / Books (right)** area.
- [ ] The Games column shows in-focus + recently-played games (no 14-day activity bar chart / summary-stats block).
- [ ] The Books column is a labelled placeholder card with an empty/coming-soon state, matching the games column's chrome.
- [ ] Movies / TV Series / Games tabs still function; only the All-tab composition and the shared tab/header change.
- [ ] Conforms to the design system (typography, velvet-card/paper-overlay, `DesignSystem.fs`), reviewed on the running StyleGuide page. `npm run build` is clean.

## Notes
- **Reference:** direction **3a** in `Mediatheca Directions.html` (the captured design session in
  repo root). The orange underline is the Velvet Lobby secondary/gold accent.
- **Search reuse:** `Components/SearchModal.fs` already wires `searchLibrary`
  (`IMediathecaApi.searchLibrary : string -> Async<LibrarySearchResult list>`). The header search
  should open/trigger that, not reimplement search.
- **Books placeholder shape:** mirror the existing `placeholderTab` chrome
  (`DesignSystem.velvetCard` + quiet empty text). No Books events/API — visual stub only.
- **Current structure to edit:** `allTabView` in `Views.fs` (currently: `activitySection`, then a
  `lg:grid-cols-3` split with hero/Next-Up/Movies on the left and games-chart/in-focus/new-games
  on the right). The page shell (`view`, ~line 4273) holds the title + `tabBar`.

- **Open interpretation for refine/worker:**
  - **Hero spotlight:** the user's spec says "full-width TV Series row" and doesn't mention the
    current `heroSpotlight`. Decide whether to keep the hero as the lead of the TV row or drop it
    for a pure poster row.
  - **Tab restyle home:** the orange-underline tab could either be done inline on the dashboard or
    promoted to a reusable **design-system** tab pattern (like the 3a sidebar work, ADR-0014). If
    it becomes shared vocabulary, that split is a separate design-system task. Conform to the gate
    either way.
  - **"New Games" section:** not mentioned in the 3a spec — confirm whether it survives (likely
    folds away, since the bottom-right becomes Books).
