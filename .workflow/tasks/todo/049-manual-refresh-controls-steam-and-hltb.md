# Task 049: Manual Refresh Controls for Steam Link & HLTB Data

**Status:** Todo
**Size:** Small
**Created:** 2026-05-01
**Milestone:** --
**Dependencies:** --

## Description

Add user-controlled refresh affordances on the Game Detail page for two pieces of metadata that today are only fetched once (or only when missing):

1. **Steam re-link button** — visible regardless of whether the game already has a `SteamAppId`. Today the "Connect with Steam" button only renders when `SteamAppId = None` (`Views.fs:1330–1331`). Extend it to also let the user re-run the Steam search on a *linked* game and pick a different App ID — useful when the auto-attach during Add Game picked the wrong title (remake vs. original, soundtrack vs. game, identical names).
2. **HLTB refresh button** — always visible on the HowLongToBeat card, including when HLTB data is already populated. Today the "Fetch from HowLongToBeat" button only renders when `game.HltbHours = None` and `HltbNoData = false` (`Views.fs:1416–1441`), so once data is in (or once a fetch has returned `None`), the user has no way to re-fetch.

### Why

- Auto-matched Steam links are sometimes wrong; without a re-link option the user has to pry the App ID out via the database.
- HLTB occasionally updates aggregates as more users complete a game; the user wants a one-click way to pull the latest numbers.
- HLTB also occasionally matches the wrong game; refresh is also the relink path here (`HowLongToBeat.searchGame` always picks the top match for the game's current name).

## Acceptance Criteria

### Steam re-link

- [ ] When `game.SteamAppId.IsSome`, the Links section shows the existing "Steam Store" link **and** an adjacent small refresh icon button (or a kebab/secondary button) that triggers the same `Connect_steam_requested` flow used today by the unlinked-state button.
- [ ] Clicking re-link runs the Steam search via `searchSteamForGame`, opens the existing `ConnectSteamPicker` glassmorphic popover, and lets the user pick a different App ID (or auto-attaches if a confident single match is returned).
- [ ] Choosing a candidate calls the existing `attachSteamToGame` endpoint, which already emits `Set_steam_app_id` (no-ops when unchanged via `Games.decide`) and only fills `description` / `short_description` / `website_url` / `play_modes` when those projected fields are empty — so re-linking doesn't clobber user edits.
- [ ] After a successful re-link, the page reloads (`Load_game`) and the Steam Store link reflects the new App ID; trailers refetch via `getGameTrailers`.
- [ ] The "no match" / failure path uses the existing `Failed msg` UI shared with the initial Connect flow.
- [ ] No backend changes required — the existing `searchSteamForGame` and `attachSteamToGame` endpoints already accept any slug regardless of current link state.

### HLTB refresh

- [ ] The HowLongToBeat card always shows a small refresh icon button in the card header (top-right), regardless of whether `HltbHours` is `Some` or `None`.
- [ ] Clicking it dispatches the existing `Fetch_hltb` message — the existing handler (`State.fs:451`) already issues `Set_hltb_hours` which **overwrites** existing values via `Games.decide` (Games.fs:237–239 emits an event whenever the values differ).
- [ ] Spinning icon animation while `HltbFetching = true`; button disabled during fetch to prevent double-clicks.
- [ ] On `Hltb_fetched (Ok None)` after refresh: keep the existing bars on screen (don't overwrite with "No HLTB data available" — the `HltbNoData` flag should only display when there were never any hours to begin with). If there were no hours before, falls back to today's `HltbNoData` text.
- [ ] On `Hltb_fetched (Error err)`: existing top-level error banner is fine (current behaviour); no inline card error needed.
- [ ] No backend changes — `fetchHltbData` (`Api.fs:3908`) already does the right thing.

### Build / test

- [ ] `npm run build` succeeds (Fable compilation clean).
- [ ] `npm test` passes.
- [ ] Design check on the two new buttons: small refresh icons consistent with the existing refresh button on the dashboard "Recently Played" card (Task 039 — `arrowPathSm` icon, circular hover, spin while loading).

## Implementation Notes

### Frontend only — no backend changes

#### Steam re-link

**Views.fs (`~1317–1331`):** Change the Links Steam branch from "Steam Store link OR Connect button" to "Steam Store link AND refresh icon" when linked, falling back to today's Connect button when unlinked.

```fsharp
match game.SteamAppId with
| Some appId ->
    Html.div [
        prop.className "flex items-center gap-2"
        prop.children [
            Html.a [ (* existing Steam Store link *) ]
            // New: small refresh icon button — same dispatcher as Connect
            Html.button [
                prop.className "btn btn-ghost btn-xs btn-circle"
                prop.disabled (model.ConnectSteamState <> Idle)
                prop.onClick (fun _ -> dispatch Connect_steam_requested)
                prop.children [ Icons.arrowPathSm () ]
                // Spin while Searching/Attaching
            ]
        ]
    ]
| None ->
    connectSteamButton model.ConnectSteamState dispatch
```

The picker (`ConnectSteamPicker`, rendered at view root ~line 1843) already anchors to `connect-steam-button` via `getElementById`. Two options:
- (a) Give the new icon button the same DOM id when linked, and remove the id from `connectSteamButton` to avoid duplicates — cleanest, anchor follows whichever button is visible.
- (b) Add a second id (`connect-steam-button-relink`) and have the picker resolve whichever exists.

Pick (a). The picker's anchor logic doesn't care which button it points at.

**State.fs:** No new messages. The existing `Connect_steam_requested` → `Steam_search_completed` → `Steam_candidate_chosen` → `Steam_attach_completed` chain works unchanged. The single-candidate-with-`Score >= 0.95` auto-attach branch (`State.fs:534`) is mildly awkward for re-link (the user clicks refresh and gets silently re-attached to the same App ID with no visible effect) — acceptable for v1, but consider always showing the picker when triggered from a *linked* state. Track this as a follow-up if it actually annoys in practice; for the first cut keep the auto-attach branch.

**Types.fs:** No changes.

#### HLTB refresh

**Views.fs (~1361–1442):** Refactor the HLTB card so the header always renders with a refresh icon, and the body branches on data state. Reuse `sectionCardOverflowWithAction` pattern from Task 039 if applicable, otherwise inline:

```fsharp
glassCard [
    Html.div [
        prop.className "flex items-center justify-between mb-4"
        prop.children [
            Html.h3 [
                prop.className "text-lg font-bold flex items-center gap-2"
                prop.children [ Icons.hourglass (); Html.text "HowLongToBeat" ]
            ]
            Html.button [
                prop.className "btn btn-ghost btn-xs btn-circle"
                prop.disabled model.HltbFetching
                prop.onClick (fun _ -> dispatch Fetch_hltb)
                prop.children [
                    if model.HltbFetching then
                        Html.span [ prop.className "loading loading-spinner loading-xs" ]
                    else
                        Icons.arrowPathSm ()
                ]
            ]
        ]
    ]
    // body: existing match game.HltbHours with Some -> bars | None -> NoData/Fetching/Fetch-button
]
```

When data is present, drop the bottom "Fetch from HowLongToBeat" button entirely — refresh now lives in the header. When data is absent, the existing inline button can stay (or be removed in favour of the header refresh; pick whichever feels cleaner during implementation).

**State.fs:** Adjust `Hltb_fetched (Ok None)` so it doesn't set `HltbNoData = true` when the model already has hours — otherwise a refresh that fails to find updated data on a previously-linked game would erase the bars. Suggested:

```fsharp
| Hltb_fetched (Ok None) ->
    // Only show "no data" when we never had data
    let noData = model.HltbHours.IsNone   // or read from current loaded game
    { model with HltbFetching = false; HltbNoData = noData }, Cmd.none
```

(The hours come from `game.HltbHours`, not the model — the model only has `HltbFetching` / `HltbNoData`. So check the loaded game's hours.)

**Types.fs:** No changes.

### Reused pieces

- `Icons.arrowPathSm` — small refresh icon, already used on Dashboard "Recently Played" card (Task 039).
- `connectSteamButton` / `ConnectSteamPicker` — existing Connect-with-Steam UI, used as-is.
- `searchSteamForGame` / `attachSteamToGame` API endpoints — existing, no changes needed.
- `fetchHltbData` API endpoint — existing, no changes needed; already overwrites via `Set_hltb_hours`.

### Out of scope

- Inline HLTB error display next to the card (user is fine with the top-level banner).
- Manual override of the auto-attach branch when the user explicitly clicks "refresh" on a linked game (could force the picker to always show; deferred unless the auto-re-attach turns out to be confusing).
- HLTB manual search/picker (we still rely on `HowLongToBeat.searchGame` picking the top match by name — no UI to disambiguate). If the wrong-game-matched problem is common enough later, file as a follow-up task.
