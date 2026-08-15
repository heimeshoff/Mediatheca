---
id: integration-n3vqa
title: Incremental Steam Family import — answer "what's new in the family library since I last checked" and only enrich the newcomers
status: backlog
type: feature
context: integration
created: 2026-08-15
completed:
depends_on: [integration-r8kwd, design-system-001]
blocks: []
tags: [steam, steam-family, import, sync, settings, games]
related_adrs: [0043, 0061]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: [integration-hebjs, integration-004]
---

## Why

The question the builder actually asks of the family import is *"what has been added to the
family library since I last looked?"* — not *"re-import everything"*. Today the import is
all-or-nothing: it fetches the full `GetSharedLibraryApps` list (one cheap call) and then does
**per-app enrichment for every app**, including ones already in the library — a Steam Store
`appdetails` fetch per matched game (`Api.fs` ~575), RAWG search + cover/backdrop download for
new ones. For a family library of a few hundred titles that is hundreds of store calls per
click, slow, rate-limit-prone (the store API throttles around ~200 requests / 5 min), and it
buries the one interesting number — *N new games* — inside "gamesProcessed: 412".

An incremental import is both the answer to "what's new" and the way to make the interface
lighter and more stable: fetch the list once, diff it against what we already know, enrich
only the newcomers, and report the arrivals by name.

## What

- **Diff, don't re-import.** After `GetSharedLibraryApps` (unchanged, still one call — Steam
  offers no server-side "since" filter for this endpoint; `rt_time_acquired` per app is the
  cursor we get), classify each app:
  - *known* — `GameProjection.findBySteamAppId` hits → cheap path only: set family owners /
    library date (already events); **skip** `getSteamStoreDetails` and all downloads.
  - *new* — no hit → full creation path as today.
  - *newly acquired* — `rt_time_acquired` later than the stored `steam_family_last_sync`
    (or the app is *new*): this is the "what's new" set to report, whether or not a matching
    game already existed (someone in the family bought a game you already owned is still news).
- **Report arrivals.** Extend `SteamFamilyImportResult` with the arrivals list (appid, name,
  acquired date, owner steamids → friend slugs where mapped) and show it in the Settings import
  result as a named list — "7 new since 2026-08-01: …" — instead of only counts. Persist the
  last result (`steam_family_last_result`, mirroring the Jellyfin `SettingsStore` last-result
  pattern) so it survives a reload.
- **Full refresh stays available** as an explicit second action ("Re-enrich all family
  games") so the current behaviour isn't lost — but it is not the default click.
- Progress SSE (`SteamFamilyImportProgress`) keeps working; `Total` becomes the number of apps
  actually processed on the chosen path.

## Acceptance criteria

- [ ] Default "Import family library" makes exactly one Steam Store `appdetails` request per
      *new* app and zero for known apps (Expecto over a fake `HttpClient` handler / call
      counter).
- [ ] The result names each newly acquired game (name, acquired date, which family member
      added it when mapped) and the count of arrivals since the previous
      `steam_family_last_sync`; the Settings UI renders that list.
- [ ] Known games still get `Set_steam_library_date` / family-owner updates on the default
      path (no regression of integration-hebjs ownership behaviour).
- [ ] The last import result is persisted and shown on Settings after a reload.
- [ ] A "full re-enrich" action reproduces today's behaviour for all apps.
- [ ] `npm test` and `npm run build` green.

## Notes

- Blocked on integration-r8kwd: an import that dies on the API-key supplement can't report
  arrivals; and that task's typed error shape is what this task's result surface builds on.
- Open questions to settle in REFINE:
  - Where else "new in family library" should surface — Settings only (minimal), or also as
    a filter/badge on the Games catalog ("New in family", based on the persisted arrivals or
    on `steam_library_date` ≥ last sync). The vision favours media experience over admin
    console; a Games-page surface may be the better home. Decide with the builder.
  - Whether "arrival" is event-worthy. Under ADR-0043 it is a third party's description
    (Steam says when it was acquired) — cache territory, and `Set_steam_library_date` already
    records the acquired date on the aggregate; a persisted last-result blob is probably enough.
  - `GetSharedLibraryApps` accepts `include_own`, `include_excluded`, `include_free`,
    `include_non_games` — check whether any of these trims the list usefully (e.g. excluding
    non-games) or, conversely, whether the current call silently omits titles the builder
    expects. Verify live against the builder's family before deciding.
  - Should the scheduled cadence (README open question — "scheduled Steam Family sync … a
    natural next capture") ride on this incremental path? A cheap daily diff that only enriches
    newcomers is much easier to justify than a daily full re-enrich; consider capturing that as
    a follow-on rather than folding it in here.
