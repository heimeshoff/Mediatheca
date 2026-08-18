---
id: integration-n3vqa
title: Incremental Steam Family import — answer "what's new in the family library since I last checked" and only enrich the newcomers
status: todo
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

**Reframed 2026-08-18 — this is now an account-safety task as much as a feature.** Valve has
twice alerted the builder's Steam account after a family import — *"your accounts appear to be
using the Steam API in the same way a certain brand of account hijacking does"* — and earlier
flagged it as "probably being used by another user", plausibly revoking the Web API key
(integration-r8kwd's trigger). The likely enumeration signature is the import itself: it fetches
`GetSharedLibraryApps` (one cheap call) and then does **per-app enrichment for every app**,
including ones already in the library — a Steam Store `appdetails` fetch per matched game
(`Api.fs:584`, `:623`, `:657`), RAWG search + cover/backdrop download for new ones. For a family
library of a few hundred titles that is hundreds of Steam requests per click: the
burst-enumerate-everything shape that reads as credential abuse, and incidentally slow and
rate-limit-prone (~200 requests / 5 min). *(Valve's heuristics are undocumented — the specific
mechanism is speculation. The call volume is not.)*

**The fix and the feature turn out to be the same thing.** The question the builder actually
asks is *"what has been added to the family library since I last looked?"* — not *"re-import
everything"*. Diffing the list against what we already know, enriching only the newcomers, and
reporting arrivals by name makes the steady-state import a handful of requests — account-safe
by construction — and surfaces the one number today's import buries, *N new games*, instead of
"gamesProcessed: 412".

integration-w7ktb (Adapter-owned storefront throttle) is the complement, not a substitute: it
lowers the *rate*, this task lowers the *count*. Neither subsumes the other, they touch
independent code paths, and after w7ktb lands a full re-enrich becomes slow enough (~1.5s/app)
that making it *rare* is what keeps the feature usable.

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
- [ ] **Total outbound Steam requests** for a default import with zero new apps is a fixed
      constant — enumerate the expected calls explicitly in the test (the family/list/owned-games
      calls only) — and grows by exactly one `appdetails` call per new app. Assert the *exact
      total* over the fake `HttpClient` counter, not merely the per-app figure above: a per-app
      assertion can pass while an unnoticed extra sweep slips in, which is the failure mode this
      task exists to prevent. (Request *count* is this task's to own; request *spacing* belongs
      to integration-w7ktb — the two must not double-own the same assertion.)
- [ ] The result names each newly acquired game (name, acquired date, which family member
      added it when mapped) and the count of arrivals since the previous
      `steam_family_last_sync`; the Settings UI renders that list.
- [ ] Known games still get `Set_steam_library_date` / family-owner updates on the default
      path (no regression of integration-hebjs ownership behaviour).
- [ ] The last import result is persisted and shown on Settings after a reload.
- [ ] A "full re-enrich" action reproduces today's behaviour for all apps.
- [ ] `npm test` and `npm run build` green.

## Notes

- **Both dependencies are now satisfied** (integration-r8kwd done 2026-08-15;
  design-system-001 done) — they stay recorded as history. Deliberately **not** dependent on
  integration-w7ktb: independent code paths, and this task carries the larger risk reduction,
  so it must not queue behind the throttle.
- **A store-details response cache was considered and deliberately left out of
  integration-w7ktb**, on the grounds that this task's "skip known apps entirely" supersedes
  most of its value — skipping a call beats caching its answer. If, once this ships, a residual
  need survives (e.g. the explicit full re-enrich path still refetching everything it already
  has), capture a cache task then. Note for whoever picks that up: **there is no store-details
  cache today**, despite a stale comment at `Steam.fs:966-967` claiming one —
  `storeMetaCache` is search-only. integration-w7ktb corrects that comment.
- **Builder gate ordering:** integration-r8kwd's own deferred gate ("a live family import
  succeeds end to end") is discharged by *this* task's first live import — which, by design,
  will be a small incremental one rather than a full sweep. See r8kwd's amended Builder-gate
  section.
- Blocked on integration-r8kwd: an import that dies on the API-key supplement can't report
  arrivals; and that task's typed error shape is what this task's result surface builds on.
- **Open questions — all settled 2026-08-18, none left blocking:**
  - *Where arrivals surface* — **Settings only** (builder's call). The import result in
    Settings → Steam names the new games; that is what the acceptance criteria pin. A "New in
    family" filter/badge on the Games catalog was considered and deliberately deferred: this
    task is on the account-safety critical path and less surface means it lands sooner. If the
    Games-page home is wanted later (the vision does favour media experience over admin
    console), capture it as its own follow-on task depending on this one.
  - *Is "arrival" event-worthy* — **no**. Under ADR-0043 it is a third party's description
    (Steam says when a title was acquired), so it is cache territory, and
    `Set_steam_library_date` already records the acquired date on the aggregate. The persisted
    last-result blob is enough; do not add an event.
  - *`GetSharedLibraryApps` flags* (`include_own`, `include_excluded`, `include_free`,
    `include_non_games`) — worth reading the response shape to see whether the current call
    silently omits titles the builder expects, but **do not schedule extra live imports to
    A/B the flags**: that is exactly the traffic under suspicion. One call's response is
    already enough to inspect, and this endpoint is the one cheap call in the whole flow.
    Leave the flags as they are unless the single response shows an obvious omission.
  - *Scheduled cadence* — **not folded in here.** A cheap daily diff that only enriches
    newcomers is far easier to justify than a daily full re-enrich, so this task is what makes
    that conversation possible; capture it as a follow-on once this has proven itself. Note
    any recurring cadence needs integration-p2hxn's risk framing considered first — automated
    periodic Steam traffic is a different risk profile from a manual click.
