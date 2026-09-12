---
id: games-k3vps
title: Selectable search sources in the games search tab — RAWG and Steam checkboxes (RAWG always on by default, Steam always off) that immediately include or exclude each API's results
status: done
type: feature
context: games
created: 2026-08-07
completed: 2026-08-07
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

## Outcome

Shipped both new server endpoints and the client toggle row/merged grid.

**Server** (`src/Shared/Shared.fs`, `src/Server/Api.fs`): `searchSteamGames: string *
int option -> Async<SteamSearchResult list>` is a thin wrapper over
`Steam.searchSteamByName`, mirroring `searchRawgGames`'s shape; `searchSteamForGame`
(slug-bound re-link) is untouched. `addGameFromSteam: AddGameFromSteamRequest ->
Async<Result<AddGameOutcome, string>>` is backed by a new private
`Api.addGameFromSteamCore`, which mirrors the Steam-library import's "no match — create
new game" branch: duplicate check by `steam_app_id` then case-insensitive name (returns
the existing `Duplicate_found`, reusing `AddGameOutcome` so the client's one
duplicate-prompt flow serves both RAWG and Steam imports); on create, `Name`/`Year`/
`Genres` (`[]` — no RAWG lookup in this path, out of scope) ride `Add_game`,
`Game_steam_app_id_set` follows, and description/short_description/website_url/facets
land in `game_metadata_cache` via the creation code path directly — never
`GameProjection.handleEvent` — matching ADR-0043/ADR-0045 exactly as games-v4nqe applied
it. `Mark_as_owned` is deliberately never dispatched (nothing here confirms Steam
ownership, unlike the library sync).

**Client** (`src/Client/Components/SearchModal.fs`, `src/Client/State.fs`): the Games
tab gets a fixed-height toggle row (RAWG/Steam checkboxes, DaisyUI `checkbox.xs`,
RAWG-on/Steam-off on every `initWithGames`, never persisted) that only renders content
when the Games tab is active, avoiding layout jump on tab switch. RAWG and Steam results
merge into one keyboard-navigable grid (`GameSearchEntry = RawgEntry | SteamEntry`),
each carrying a RAWG/Steam source badge; unchecking a source (or a stale response
arriving for a since-unchecked source) is invisible immediately because the merge is
gated on the *current* `IncludeRawg`/`IncludeSteam` flags rather than on anything the
response itself carries — the same "current state wins" principle `SearchVersion`
already applies to the debounced-typing race, so no per-source version field was needed.
Checking a source while the query is non-empty fires that source's search right away
(`Toggle_include_rawg`/`Toggle_include_steam`); typing debounce and `Tab_changed` both
fire whichever sources are checked and lack results, via a new shared
`gamesSearchCmds` helper. The loading indicator composes "Searching RAWG…" / "Searching
Steam…" / "Searching RAWG & Steam…" from whichever checked sources are still in flight.
Clicking or pressing Enter on a Steam result dispatches `Import_steam`, which posts
`AddGameFromSteamRequest` and routes `Duplicate_found` through the same
`Duplicate_prompt_show`/`Duplicate_prompt_force_add` flow as RAWG (`DuplicatePrompt` now
carries a `PendingGameImport = FromRawg of AddGameRequest | FromSteam of
AddGameFromSteamRequest` so "add as duplicate" resubmits the right request shape).
No hover-preview endpoint exists for a Steam search result (only for library games/RAWG/
TMDB candidates) — Steam cards pass no-op hover handlers rather than getting stuck in a
`Loading` popover state; out of this task's scope (ACs don't call for a Steam preview).

Server-side TDD: `tests/Server.Tests/AddGameFromSteamTests.fs` (6 new tests, all
red-then-green against a file-backed `TestDb` + a stub `HttpMessageHandler` routing on
URL) covers `addGameFromSteam`'s create path (identity-card/facet cache writes,
`SteamAppId` set), duplicate detection, `SkipDuplicateCheck` bypass, a failed Steam
lookup degrading to an empty-but-successful create (never throws), and
`searchSteamGames`'s delegation (including the empty-SearchApps-response degrade to `[]`
that mirrors `searchSteamForGame`'s existing behavior). 643/643 tests pass; `npm run
build` (Fable compile gate) passes. Client-side Model/Msg/view wiring has no test
infrastructure in this project (existing gap, not scoped here) — verified by the build
gate plus manual code review; interactive/visual behavior (toggle immediacy, badge
rendering, no layout jump, keyboard nav across the merged grid) is `[human-eye]` per the
task's final AC.

Key files: `src/Shared/Shared.fs` (`AddGameFromSteamRequest`, two new
`IMediathecaApi` members), `src/Server/Api.fs` (`addGameFromSteamCore`,
`searchSteamGames`/`addGameFromSteam` wiring), `src/Client/Components/SearchModal.fs`
(`PendingGameImport`, `GameSearchEntry`, toggle row, merged grid), `src/Client/State.fs`
(`gamesSearchCmds`, all new/updated `SearchModal.Msg` handlers),
`tests/Server.Tests/AddGameFromSteamTests.fs`,
`.agentheim/contexts/games/README.md` (new "Search source toggles" ubiquitous-language
entry).
