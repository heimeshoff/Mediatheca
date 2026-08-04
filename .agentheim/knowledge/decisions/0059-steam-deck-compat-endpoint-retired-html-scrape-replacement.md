---
id: 0059
title: The unofficial ajaxgetdeckappcompatibilityreport endpoint is dead — Steam Deck compatibility is scraped from the store app page's embedded data-hardwarecompatibility attribute instead
scope: games
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
related_tasks: [games-b8xnw, games-a7dqx]
related_research: []
---

# ADR 0059: The unofficial ajaxgetdeckappcompatibilityreport endpoint is dead — Steam Deck compatibility is scraped from the store app page's embedded data-hardwarecompatibility attribute instead

## Context

games-b8xnw's task ("What" section) named a specific unofficial Steam endpoint,
`ajaxgetdeckappcompatibilityreport`, as the source for Steam Deck
compatibility (Verified/Playable/Unsupported), and its acceptance criteria
required that endpoint's actual response shape to be verified against a live
fetch before any decoder was written — flagging up front that it "is not
part of Valve's documented Web API and may need cookie/session handling
different from `store.steampowered.com`'s `appdetails`".

Live verification (2026-08-04) found the endpoint itself is gone: every
request tried — plain GET, GET with a session cookie jar obtained by first
visiting the app's store page, POST with form-encoded params, with and
without a browser `User-Agent` — returned a bare `HTTP/1.1 302 Moved
Temporarily` to `https://store.steampowered.com/`, never a JSON body, for
every appId tried (Hades 1145360, Elden Ring 1245620). Nothing in the app
page's own HTML or referenced assets calls that endpoint name either. Valve
has evidently retired it.

The verdict itself is still available, though: every store app page's HTML
(`https://store.steampowered.com/app/<id>/`) embeds it directly in a
`data-hardwarecompatibility="{...}"` attribute (HTML-entity-encoded JSON —
`&quot;` for every `"`). Live-verified against six titles:

| appId | Title | `resolved_category` | Real-world status |
|---|---|---|---|
| 1145360 | Hades | 3 | Verified |
| 892970 | Valheim | 3 | Verified |
| 1245620 | Elden Ring | 3 | Verified |
| 359320 | Elite Dangerous | 2 | Playable |
| 730 | Counter-Strike 2 | 2 | Playable |
| 620980 | Beat Saber | 1 | Unsupported (VR-only, no Deck-native input path) |

No live fixture produced `0`, but it is the only value left once 1/2/3 are
accounted for, and is the honest "never tested" default the codebase already
uses elsewhere as a degradation stance (ADR-0048).

Mature-rated titles (Elden Ring) 302 to `/agecheck/app/<id>/` — a page with
no `data-hardwarecompatibility` attribute at all — unless the request also
carries Steam's three age-verification cookies (`birthtime`,
`lastagecheckage`, `wants_mature_content`). Sending them unconditionally is
harmless and verified not to change the response for non-Mature titles.

The same attribute also carries `steamos_resolved_category`/
`machine_resolved_category`/`frame_resolved_category` — distinct verdicts
for Valve's newer SteamOS-general/Steam Machine/Steam Frame hardware,
unrelated to this task's Deck-specific scope.

## Decision

`Steam.fs` fetches the store app page HTML (not the dead ajax endpoint),
extracts `data-hardwarecompatibility`'s JSON via a targeted regex (not a
general HTML parser — the attribute's own literal-`"` delimiter is safe to
match against because its content is entity-encoded, so no nested `"` can
appear), HTML-decodes it, and reads `resolved_category` with a Thoth
decoder. `mapDeckCompatCategory` maps `0/1/2/3` to
`Unknown`/`Unsupported`/`Playable`/`Verified`; anything else degrades to
`Unknown` rather than guessing. Every request carries the age-gate cookie
header unconditionally.

Per ADR-0043/ADR-0045 (both pre-loaded for this task) and consistent with
ADR-0053's play-facets precedent: this is cache-tier only
(`game_metadata_cache.deck_compat`), with its own resume cursor
(`deck_compat_fetched_at`, deliberately a *different* column from the
play-facets backfill's `fetched_at` — the two backfills run on independent
schedules against different endpoints, and sharing one column would let one
job's stamp silently drop a game off the other job's cursor). No event, no
override, no aggregate involvement at all — unlike play facets, Steam's own
Deck verdict isn't something Marco is likely to know better than Valve's own
testing (the task's own "What" section), so there is no
`DeckCompatOverride` counterpart to `PlayFacetsOverride`.

`GameDeckCompatBackfill.fs` reuses `GameFacetBackfill.fs`'s shape exactly
(same `withLock` discipline, same 300ms throttle, same
leave-cursor-NULL-on-failure retry semantics) per the task's `depends_on`
games-a7dqx.

**The 300ms throttle is inherited unmeasured, not independently observed for
`store.steampowered.com/app/<id>/` page fetches** (verifier iteration 1
flagged this honestly: task acceptance criterion 3 says "throttled to the
endpoint's observed rate limit", and no such observation was made for this
specific source). The six-title live verification in the table above ran
sequential single fetches during manual verification, spaced by normal
human/tool latency between calls — not a sustained backfill-speed burst — so
it demonstrates the scrape *works*, not what Valve's actual rate limit for
that page is. Carrying the 300ms constant over from `GameFacetBackfill.fs`
(which fetches `store.steampowered.com/api/appdetails`, a different endpoint
under the same `store.steampowered.com` origin) is a conservative default,
not a measured one: 300ms is slow enough to be polite to a page-serving
origin, the backfill job is resumable and leaves its cursor NULL on any
failure (so a 429/403 from being too aggressive just means slower catch-up,
never data loss or a crash), and the constant is a single named value in
`GameDeckCompatBackfill.fs` that can be raised without any other code change
if Steam is observed throttling harder in production. If production
operation ever surfaces actual rate-limit behavior (429s, IP blocks, or
similar) for this endpoint, that observation should update this ADR and the
constant, not be assumed away here.

## Alternatives considered

- **Give up and ship no Deck-compat feature at all**, since the task's named
  endpoint doesn't exist. Rejected: the data is genuinely available (just at
  a different URL/shape), and the task's own acceptance criteria anticipated
  exactly this kind of empirical surprise ("verify response shape... it is
  not part of Valve's documented Web API").
- **A full HTML parser (e.g. AngleSharp) instead of a targeted regex.**
  Rejected: adds a dependency for one attribute's value on a page whose
  entity-encoding already makes a literal-`"`-delimited regex safe and
  exact; every other Steam scrape in this codebase (`Steam.fs`'s existing
  JSON decoders) stays dependency-light the same way.
- **Reuse the play-facets backfill's own `fetched_at` cursor column** instead
  of a dedicated `deck_compat_fetched_at`. Rejected: the two backfills fetch
  from different endpoints on different schedules; a shared cursor would
  make each job's success silently exempt the other job's own work for that
  game, an incorrect coupling neither job's semantics intend.

## Consequences

### Positive

- The feature ships despite the named endpoint being dead — the live
  verification step this task's acceptance criteria required is exactly
  what caught the discrepancy before a decoder was written against a
  fabricated shape.
- `deck_compat_fetched_at`'s independence from the facets cursor means
  either backfill can be reworked, retried, or reset without touching the
  other's resume state.

### Negative / accepted tradeoff

- Scraping a store page's HTML is inherently more fragile than a documented
  API: Valve can change the attribute name, the encoding, or the page
  markup at any time without notice, silently breaking the backfill (it
  degrades to leaving `deck_compat_fetched_at` NULL and retrying forever,
  never crashing — but also never succeeding until someone notices and
  updates the regex/decoder).
- Only three of the four `resolved_category` values (1/2/3) have a positive
  live fixture; `0` (`Unknown`) is inferred by elimination, not observed
  directly on any tested title.
- The backfill's 300ms inter-request throttle is inherited from
  `GameFacetBackfill.fs` unmeasured against this endpoint's actual rate
  limit (see the Decision section's honest note above) — an accepted risk
  given the job's resumability and NULL-cursor-on-failure semantics, not a
  verified-safe value.
