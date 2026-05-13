# Task 050: Track Navigation History So Detail-Page Back Buttons Return to the Previous Page

**Status:** Todo
**Size:** Medium
**Created:** 2026-05-01
**Last Refined:** 2026-05-01
**Milestone:** --
**Dependencies:** --

## Description

The in-page "← Back" buttons on Movie, Series, Game, and Friend detail pages currently hard-code navigation to the corresponding section list. This is wrong when the user arrived from somewhere else (e.g. opening a movie from a friend's library, then "Back" drops them on `/movies` instead of returning to the friend).

Keep the buttons. Track navigation history in Elmish state so the back button returns to whichever page the user actually came from — including other detail pages (Friend, Catalog) and any non-list scene.

### Why

- The current behavior is misleading: the destination depends on the section, not the actual previous page.
- Users navigating through cross-context flows (friend → movie, catalog → game, search → series) lose their place when going "Back."
- Browser-back works, but the in-page button is a clearer affordance and matches user expectations when it does the right thing.

## Behavior

- A navigation stack lives in the root Elmish model (e.g. `NavigationHistory: Page list`, head = most recent prior page).
- Every URL change records the *previous* page on the stack (skip duplicates and "navigate to self"). Cap stack size (~20) to prevent unbounded growth.
- Detail-page back button: pop head, navigate there.
- **Empty-stack fallback** (deep link, hard refresh, or stack drained):
  - `Movie_detail` → `Dashboard` with `MoviesTab` active
  - `Series_detail` → `Dashboard` with `SeriesTab` active
  - `Game_detail` → `Dashboard` with `GamesTab` active
  - `Friend_detail` → `Friend_list` (no Friends tab exists on Dashboard)
- Dashboard tab state is local to the Dashboard page module. The fallback must both navigate to `Dashboard` *and* set the active tab — likely via a one-shot `Cmd` that dispatches `Dashboard.SwitchTab` after navigation, or by extending the URL with a tab query param. Decide during implementation; prefer the simpler one.

## Acceptance Criteria

- [ ] Root model carries a `NavigationHistory` stack of `Page` values.
- [ ] URL-change handler pushes the prior page onto the stack (deduped, capped).
- [ ] `MovieDetail/Views.fs` back button (~lines 876–887) dispatches a `GoBack`-style message instead of `Router.navigate "movies"`.
- [ ] `SeriesDetail/Views.fs` back button (~lines 1576–1587) — same change.
- [ ] `GameDetail/Views.fs` back button (~lines 955–966) — same change.
- [ ] `FriendDetail/Views.fs` back button (~lines 426–437) — replace the `emitJsStatement "window.history.back()"` call with the same dispatch path.
- [ ] Empty-stack fallback navigates Movie/Series/Game detail to Dashboard with the right tab active; Friend detail falls back to `Friend_list`.
- [ ] Visual: hero-overlay layout unchanged (no shift around the kept back button).
- [ ] `npm run build` succeeds.
- [ ] `npm test` passes.
- [ ] Manual smoke tests:
  - Friend → Movie detail → Back → returns to that friend.
  - Catalog → Game detail → Back → returns to that catalog.
  - Search → Series detail → Back → returns to search.
  - Hard refresh on Game detail → Back → Dashboard with Games tab active.

## Out of Scope

- Browser-back integration / history syncing beyond the existing Feliz.Router URL mechanism. The Elmish stack is best-effort and can drift from `window.history`; we don't try to intercept native back.
- The `CatalogDetail` breadcrumb ("Catalogs / [name]") — it's a hierarchy indicator, not a back button.
- Error-state recovery links shown when an item slug isn't found ("Back to Movies" / "Back to Series" / etc.) — kept; they recover from invalid deep links where the stack may also be empty.
- Persisting navigation history across reloads.

## Implementation Notes

- New message in root: e.g. `GoBack` (no payload) — handler pops the head and navigates, or applies the category fallback.
- New helper for fallback resolution given the *current* page (the page we're leaving): produces the right `Page` and any companion tab `Cmd`.
- Skip pushing onto the stack when the new page equals the current page (avoids duplicates from re-renders or query-only changes).
- The Friend detail change makes the `emitJsStatement` import unused — remove it if so.
- Consider whether `Not_found` should ever be pushed onto the stack (probably skip).

## Work Log

### 2026-05-01 13:19 -- Work Completed

**What was done:**
- Added `NavigationHistory: Page list`, `SuppressNextHistoryPush: bool`, and `PendingDashboardTab: DashboardTab option` to the root `Model` (src/Client/Types.fs).
- Added `Go_back` to the root `Msg` (src/Client/Types.fs).
- Added `Route.navigateTo: Page -> unit` helper to src/Client/Router.fs (mirror of `toUrl` but issuing `Router.navigate`).
- In `State.update` (src/Client/State.fs):
  - `Url_changed` now pushes the previous page onto `NavigationHistory` (deduped, capped at 20). Skips push when `SuppressNextHistoryPush` is set, when the new page equals the previous page, or when leaving `Not_found`.
  - `Go_back` pops the head of the stack and navigates to it; on empty stack applies the per-detail-page fallback. Sets `SuppressNextHistoryPush = true` so the resulting `Url_changed` does not re-add the page being left.
  - `Dashboard` Url_changed branch now applies `PendingDashboardTab` (if set) by both setting `DashboardModel.ActiveTab` and dispatching `Dashboard_msg (SwitchTab tab)` to load that tab's data.
- Updated root Views to pass an `onBack` callback (`fun () -> dispatch Go_back`) to each detail page view (src/Client/Views.fs).
- Extended each detail page view signature with `(onBack: unit -> unit)` and replaced the back-button onClick to call it:
  - src/Client/Pages/MovieDetail/Views.fs
  - src/Client/Pages/SeriesDetail/Views.fs
  - src/Client/Pages/GameDetail/Views.fs
  - src/Client/Pages/FriendDetail/Views.fs (also removed the `emitJsStatement () "window.history.back()"` call; `emitJsStatement` import was no longer in scope after the change since only `emitJsExpr` remained — left `Fable.Core` / `Fable.Core.JsInterop` opens because `emitJsExpr` is still used elsewhere in the file)
- **Design decision** — chose the **`PendingDashboardTab` field on root Model** approach over the URL query-param option. Rationale:
  - URL query-param approach would require extending `Page` with an optional tab (changing `Dashboard` to `Dashboard of DashboardTab option`), updating `Route.parseUrl`/`toUrl`, and threading through. That couples the URL contract to a one-shot behavior.
  - The `Cmd.ofMsg (Dashboard_msg (SwitchTab tab))` approach alone races with the `Url_changed` handler which calls `Pages.Dashboard.State.init ()` and resets ActiveTab to `All` first. By staging the tab in root `PendingDashboardTab` and consuming it inside the `Url_changed Dashboard` branch, the assignment is deterministic (set ActiveTab on the freshly-initted child model and batch the SwitchTab command for data loading).
  - Net cost: one extra optional field and a small handler addition; no Router/Page-DU surface change.

**Acceptance criteria status:**
- [x] Root model carries a `NavigationHistory` stack of `Page` values -- Types.fs Model field.
- [x] URL-change handler pushes the prior page onto the stack (deduped, capped) -- State.fs `pushHistory` helper, capped at 20, dedup head.
- [x] `MovieDetail/Views.fs` back button dispatches a `GoBack`-style message instead of `Router.navigate "movies"` -- now calls `onBack ()`.
- [x] `SeriesDetail/Views.fs` back button -- same change applied.
- [x] `GameDetail/Views.fs` back button -- same change applied.
- [x] `FriendDetail/Views.fs` back button -- replaced `window.history.back()` call with `onBack ()`.
- [x] Empty-stack fallback navigates Movie/Series/Game detail to Dashboard with the right tab active; Friend detail falls back to `Friend_list` -- handled in `Go_back` branch via `PendingDashboardTab` and `Route.navigateTo Friend_list`.
- [x] Visual: hero-overlay layout unchanged -- only `prop.onClick` swapped on each Back button; markup, classes, and surrounding structure untouched.
- [x] `npm run build` succeeds -- verified, build completed in 32.57s with no errors.
- [x] `npm test` passes -- verified, 255 tests passed.
- [ ] Manual smoke tests -- not run by automation (would require browser session); flow logic verified via the build and the deterministic Url_changed/Go_back state transitions.

**Files changed:**
- src/Client/Types.fs -- added `NavigationHistory`, `SuppressNextHistoryPush`, `PendingDashboardTab` to Model; added `Go_back` to Msg.
- src/Client/Router.fs -- added `Route.navigateTo: Page -> unit` helper.
- src/Client/State.fs -- init for new fields; `Url_changed` pushes history (deduped/capped/suppressible); `Dashboard` branch consumes `PendingDashboardTab`; new `Go_back` handler with per-page fallback.
- src/Client/Views.fs -- pass `onBack = fun () -> dispatch Go_back` to all four detail views.
- src/Client/Pages/MovieDetail/Views.fs -- view takes `onBack`; back-button onClick calls it.
- src/Client/Pages/SeriesDetail/Views.fs -- view takes `onBack`; back-button onClick calls it.
- src/Client/Pages/GameDetail/Views.fs -- view takes `onBack`; back-button onClick calls it.
- src/Client/Pages/FriendDetail/Views.fs -- view takes `onBack`; back-button onClick calls it (replaces `emitJsStatement` window.history.back call).
