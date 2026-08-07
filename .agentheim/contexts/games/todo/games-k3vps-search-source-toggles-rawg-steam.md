---
id: games-k3vps
title: Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results
status: todo
type: feature
context: games
created: 2026-08-07
completed:
depends_on: [design-system-001]
blocks: []
tags: [search, steam, rawg, search-modal, import]
related_adrs: []
related_research: []
prior_art: []
---

## Why

The search modal's Games tab searches RAWG only. RAWG's catalog lags or misses
some titles (especially newer or niche Steam releases), while Steam's store
search finds them immediately — but today the only way to reach Steam search is
the slug-bound re-link flow on an already-imported game's detail page. The user
wants to choose, per search, which external sources feed the Games results.

## What

When the search modal's **Games** tab is active, render a second row below the
tab bar with two checkboxes: **RAWG** and **Steam**.

- RAWG starts **checked** on every modal open; Steam starts **unchecked** on
  every modal open. The toggles are session-local UI state — never persisted.
- Checking or unchecking a source takes effect immediately: a newly checked
  source fires its search for the current query right away; an unchecked
  source's results disappear from the grid at once.
- Results from both sources merge into the existing Games poster grid. Each
  external result carries a small source badge (RAWG / Steam) so provenance is
  visible when both are on.
- Clicking (or Enter on) a Steam result imports the game just like a RAWG
  result does — server-side via the existing Steam creation machinery
  (`Steam.getSteamStoreDetails` → `Games.Add_game`, setting `SteamAppId`),
  including the duplicate prompt.

Server surface: one new query-based endpoint on `IMediathecaApi`
(e.g. `searchSteamGames: string * int option -> Async<SteamSearchResult list>`),
a thin wrapper over the existing `Steam.searchSteamByName` — mirroring
`searchRawgGames`'s shape (query + optional year from `FuzzyMatch.extractYear`).
Plus an import endpoint for a Steam search result (new `addGameFromSteam`
or equivalent), reusing the store-details → `Add_game` path the Steam library
import already exercises.

## Acceptance criteria

- [ ] When the Games tab is active in the search modal, a source-toggle row with two checkboxes (RAWG, Steam) renders below the tab bar; the row does not render on the Library, Movies, or Series tabs.
- [ ] On every modal open, RAWG is checked and Steam is unchecked, regardless of what was toggled in a previous open — no persistence.
- [ ] Unchecking a source immediately removes that source's results from the grid; a response arriving for a source that has since been unchecked is discarded (version-guarded, same pattern as the existing `SearchVersion` debounce guard).
- [ ] Checking a source while the query is non-empty immediately fires that source's search; debounced typing re-searches only the currently checked sources.
- [ ] A new `IMediathecaApi` endpoint performs a query-based Steam store search reusing `Steam.searchSteamByName`; `searchSteamForGame` (slug-bound re-link) is left unchanged.
- [ ] With both sources checked, results render merged in the poster grid, each external result showing a RAWG or Steam source badge.
- [ ] Steam results already in the library are filtered out by the same name+year match applied to RAWG results.
- [ ] Clicking or pressing Enter on a Steam result imports the game via the store-details → `Add_game` path with `SteamAppId` set, and triggers the same duplicate prompt flow (`Duplicate_found` → open existing / add anyway / cancel) that RAWG imports have.
- [ ] The Games tab loading indicator reflects in-flight state of whichever checked sources are searching (e.g. "Searching RAWG…", "Searching Steam…", or both).
- [ ] The source-toggle row reads as part of the search modal's chrome — checkbox styling per the design system, no layout jump when switching tabs. [human-eye]

## Notes

- Cross-source dedup is deliberately **out of scope**: the same game appearing
  in both RAWG and Steam results is shown twice, distinguished by source badge.
  The user picks which source to import from. Capture a follow-up if this
  proves noisy in practice.
- `SteamSearchResult` (`AppId`, `Name`, `ReleaseYear`, `HeaderImageUrl`,
  `Score`) already exists in `Shared.fs` and is Fable-visible — reuse it.
- The Steam import path must respect the metadata-cache doctrine
  (ADR-0043/ADR-0045 discipline as applied in games-v4nqe): identity-card
  fields ride the `Add_game` event; description/short-description-style fields
  go to `game_metadata_cache` via the creation code path, exactly as the Steam
  library import does today. No new events needed.
- Both sources unchecked + non-empty query is a legal state: the Games tab
  simply shows no external results ("No results").
- Frontend gate: `depends_on` design-system-001 (done — dependency met).
