---
id: integration-w7ktb
title: Steam storefront calls are paced by the caller, not the Adapter — the family import paces not at all; move throttling into `Steam.fs` so every storefront caller inherits it
status: doing
type: bug
context: integration
created: 2026-08-18
completed:
depends_on: []
blocks: []
tags: [steam, steam-family, import, adapter, rate-limit, throttle]
related_adrs: [0019, 0061, 0065]
related_research: []
prior_art: [integration-r8kwd, integration-hebjs, integration-004]
---

## Why

Valve has **twice** alerted the builder's Steam account after a Mediatheca family import:
*"Your accounts appear to be using the Steam API in the same way a certain brand of account
hijacking does."* Earlier the same account was flagged as "probably being used by another
user" and its Web API key was (hypothesised) revoked — the trigger for integration-r8kwd,
which made the import *survive* that 401 but did nothing about why the account keeps being
flagged.

Valve's detection heuristics are undocumented, so any claim about what specifically trips
them is **speculation**. This task does not rest on that. It rests on a fact readable
straight from the source: **`runSteamFamilyImport` calls the Steam storefront once per app
across the entire family library with no delay whatsoever**, and the project already
considers that call rate-limit-sensitive everywhere else.

Concretely, verified 2026-08-18:

- `Steam.getSteamStoreDetails` (`src/Server/Steam.fs:805`) hits
  `https://store.steampowered.com/api/appdetails`. It has **11 call sites** across 5 files:
  `Api.fs:584`, `:623`, `:657` (the three family-import paths), `Api.fs:1193`, `:1302`,
  `:3778`, `:3818`, `:3920`, `GameFacetBackfill.fs:51`, `GameReleaseDateBackfill.fs:45`,
  `PlaytimeTracker.fs:289`.
- Exactly **3 of those 11** are paced, each by its own copy-pasted
  `do! Async.Sleep 300 // Rate limit Steam Store API calls` (`Api.fs:3919`,
  `GameFacetBackfill.fs:50`, `GameReleaseDateBackfill.fs:44`). A fourth 300ms sleep
  (`GameDeckCompatBackfill.fs:44`) paces store *page* fetches — same host, different call.
- **The family import's three call sites are among the eight that are not paced at all.**

That is the shape of the defect: pacing is **caller-owned**, so it fails open on every new
caller. The family import is simply the caller that forgot — and it happens to be the one
that iterates the largest collection.

Two further facts sharpen it:

1. **300ms was never the right number.** The storefront's informal ceiling is ~200 requests
   per 5 minutes, i.e. **1.5 s per request**. The existing precedent is ~5× too fast; copying
   it would leave the defect in place while looking fixed.
2. **There is no store-details cache**, despite a comment at `Steam.fs:966-967` claiming one
   ("Separate from the full-response cache used by `getSteamStoreDetails` callers"). No such
   cache exists — `storeMetaCache` (`Steam.fs:973`) is search-only and `achievementsCache`
   (`:736`) is unrelated. The comment is stale/aspirational and has been actively misleading;
   fix it here.

A vendor's rate ceiling is vendor knowledge. In this BC that belongs **inside the Adapter**
— the anticorruption layer that already owns everything else about talking to Steam — not
scattered across eleven call sites, three of which happen to remember.

## What

- **Add an Adapter-owned throttle to `Steam.fs`**: a minimum-interval gate that serializes and
  spaces all Steam *storefront* calls — `getSteamStoreDetails` (`appdetails`), the store-trailer
  fetches, and the search store-meta fetch. Default interval **1500ms** (one call per
  ceiling-interval); the interval must be injectable/settable so tests can drive it fast.
- **Route every existing storefront caller through the gate**, and delete the now-redundant
  call-site sleeps at `Api.fs:3919`, `GameFacetBackfill.fs:50`, `GameReleaseDateBackfill.fs:44`.
  Leave `GameDeckCompatBackfill.fs:44` (store *page* fetches) alone unless it routes trivially
  — see Notes.
- **Keep the family import strictly sequential.** The per-app loop is sequential today (`let!`
  inside `for`); the gate must not introduce concurrency, and this should be pinned so a later
  "optimisation" can't quietly parallelise the burst.
- **Fix the stale cache comment** at `Steam.fs:966-967`.
- **Write an ADR (scope: integration)**: the Adapter owns pacing for an external system's rate
  ceiling; callers never pace themselves. Record the 1500ms derivation and the eleven-call-site
  history that motivated it.

## Acceptance criteria

- [ ] A minimum-interval gate lives in `Steam.fs`, is applied to `getSteamStoreDetails`, and its
      interval is injectable for tests.
- [ ] Expecto (fake `HttpMessageHandler` recording a timestamp per `SendAsync`, following
      `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`'s established shape —
      `TestDb.withTempDbFactory` + the `createApi` helper): `importSteamFamily` against a stub
      returning 3 shared apps issues exactly 3 `appdetails` requests, with consecutive
      timestamps spaced ≥ the configured test interval.
- [ ] The same test asserts **no two storefront requests are ever in flight at once** (the stub
      tracks concurrent entries and fails if it ever exceeds 1) — this is the sequential-loop
      guarantee, pinned.
- [ ] A direct unit test on the gate itself: two back-to-back gated calls are spaced ≥ interval.
- [ ] No `Async.Sleep` remains adjacent to any `getSteamStoreDetails` call site (the three
      deleted above).
- [ ] The comment at `Steam.fs:966-967` no longer claims a cache that does not exist.
- [ ] `npm test` and `npm run build` green. **No test makes a live Steam call.**

## Notes

- **Do NOT verify this with a live family import.** Running one is precisely the act under
  suspicion. Every criterion above is deliberately satisfiable against a fake `HttpClient`.
- **Caching is deliberately out of scope.** An `appdetails` response cache was considered and
  rejected here: it introduces staleness, eviction and storage decisions, and
  integration-n3vqa's "skip known apps entirely" design supersedes most of its value —
  skipping a call beats caching its answer. A pointer note is recorded on n3vqa; revisit only
  if a residual need survives that design.
- **Throttle and incremental import are complementary, not alternatives.** This task lowers the
  *rate*; integration-n3vqa lowers the *count*. Neither subsumes the other, and they touch
  independent code paths — no dependency either way.
- **Known consequence:** at 1500ms a full re-enrich of a several-hundred-title family library
  takes minutes (400 apps ≈ 10 min). That is the correct trade for a rarely-used full sweep,
  and it is exactly why n3vqa (making the steady-state import a handful of calls) matters more
  after this lands, not less. If the worker judges the full-sweep UX unacceptable, surface it —
  do not quietly lower the interval below the derived ceiling.
- RAWG search and cover/backdrop downloads hit different hosts (RAWG API, Steam CDN) and are
  out of scope; the storefront gate serialises the loop between them in any case.
- No automatic retries exist on this path today — nothing to remove, but do not add any.
- An identifying `User-Agent` on Steam requests is cheap hygiene and may be folded in; not
  required.
- **Open question for the worker:** should `GameDeckCompatBackfill`'s HTML store-*page* fetches
  share the same gate (same host, different path)? Cheap if yes, not required for this fix.

## Ubiquitous language addition

- **Throttle** — Adapter-owned minimum spacing between calls to one external endpoint family.
  Lives inside the Adapter, because a vendor's rate ceiling is vendor knowledge; callers never
  pace themselves.
