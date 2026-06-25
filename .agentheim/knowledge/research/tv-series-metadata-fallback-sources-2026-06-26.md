---
topic: Fallback / alternative TV-series metadata sources when TMDB lags on a newly-aired season
date: 2026-06-26
requested_by: model
related_tasks: [integration-005]
---

# Research: TV-series metadata fallback sources (TheTVDB vs Trakt vs Jellyfin)

## Question
Mediatheca is fully TMDB-bound for series metadata (`SeriesRefresh.fetchFromTmdb` reads
`details.Seasons` and upserts into `RefreshFetchResult` / `Series.SeasonImportData` /
`EpisodeImportData`). TMDB is community-edited, so a freshly-aired season only appears
once a volunteer adds it — the real trigger being *Interview with the Vampire* S3, which
is airing and already on the user's Jellyfin server but missing from TMDB (still 2
seasons). When TMDB lacks the season, neither the nightly job nor a manual refresh can
surface it, and a Jellyfin watch of an S3 episode has no projection row to attach to.

Which fallback source should back a new-season gap: **TheTVDB**, **Trakt**, or
**Jellyfin-as-source**? (OMDb ruled out up front.) For each: new-season latency vs TMDB,
API access/licensing/rate limits, episode-level quality, and how it maps onto the
existing `SeasonImportData` / `EpisodeImportData` shapes.

## Summary
- **Trakt is disqualified for the core problem**: per Trakt's own FAQ it sources "most
  movie & tv show information" from TMDB, refreshing within ~24h [4]. It therefore
  *inherits* TMDB's lag — if TMDB has no S3, Trakt has no S3. It solves currency only,
  not coverage. (Single source, but it is the authoritative primary source — Trakt
  itself.)
- **TheTVDB has genuinely faster/earlier TV coverage** and is purpose-built for episodic
  TV [3], but the v4 API needs a per-user subscription/PIN (user-supported tier,
  $11.99/yr) or a commercial license, mandates attribution, and brings a season-ordering
  mismatch (Aired vs DVD vs Absolute) you must pin to Aired Order [1][2][6][7]. It is a
  **net-new external adapter + key + auth flow**.
- **Jellyfin-as-source is the strongest fit**: the gap appears *exactly* for titles the
  user is actively watching, which by definition are already in Jellyfin with full
  season/episode metadata. The empirical trigger proves this regardless of which scraper
  supplied it — *Interview with the Vampire* S3 is present in the user's Jellyfin yet
  absent from TMDB [8]. The adapter already
  exists (`src/Server/Jellyfin.fs`), and `/Shows/{id}/Episodes` already returns the
  exact fields `EpisodeImportData` needs (`IndexNumber`, `ParentIndexNumber`,
  `PremiereDate`, `ImageTags.Primary`, `Name`, `Overview`, `RunTimeTicks`) — see
  `.documentation/jellyfinapi.md:109-213`. **No new external dependency, no key.**
- **Recommendation: Jellyfin-as-source, as a SUPPLEMENT (fallback) to TMDB, not a
  replacement.** Keep TMDB as the authoritative metadata source (richer images, ratings,
  overviews, multi-language); materialize a season/episode from Jellyfin only when the
  sync reports an item the TMDB-fed projection lacks. Backfill from TMDB on a later
  refresh once the volunteers catch up.
- **Sizing:** "reuse Jellyfin adapter" is roughly S (small) — the fetch path and most
  decoders exist; "new TheTVDB adapter + key" is M–L (new HTTP client, auth/PIN, decoders,
  season-type handling, settings UI, image download).

## Findings

### Candidate 1 — TheTVDB

**New-season latency vs TMDB.** TheTVDB was built TV-first; community consensus across
media-center ecosystems (Simkl, Sonarr/Plex/Kodi forums) is that it adds and corrects new
episodes/seasons faster and with fewer mis-identifications than TMDB, which is movie-first
and often updates current shows only "after several days" [3]. This is forum/community
testimony, not a benchmark — treat the direction (TVDB ahead) as well-supported, the exact
lead time as anecdotal. ⚠️ UNVERIFIED (depends on the user's Jellyfin config): TheTVDB is
an *optional* Jellyfin plugin, not the default — TMDB is Jellyfin's built-in default TV
provider (TheTVDB was removed from Jellyfin core around v10.7) [8]. So whether "what
Jellyfin already shows" equals "what TheTVDB has" depends on the user having the TVDB
plugin installed and preferred; do not assume it.

**API access / licensing / rate limits.** The v4 API has two tiers, selected at key
application time [1][2]:
- *Licensed* — a contract with TheTVDB; fees scale with revenue/usage. Cross-checked
  figures from the api-information page: free under ~$50k revenue (with attribution),
  ~$1,000/yr ($50k–$250k), ~$10,000/yr ($250k–$1M), custom above [1]. (These tier numbers
  came from a single fetched render of the api-information page — flag as single-source;
  the page itself warns terms can change without notice.)
- *User-supported* — the project passes cost to the end user: each user creates a
  subscription (~$11.99/yr, or free by contributing data) which yields a **PIN** the app
  must collect and send in the auth call alongside the API key [6][7]. A developer can
  start immediately with their own single subscription/PIN [6].
- **Attribution** to TheTVDB.com is required for end users viewing metadata [1].
- **Rate limits:** not published on the api-information page, the GitHub README, or the
  Swagger landing — TheTVDB instead recommends caching/mirroring rather than hammering the
  API [1][2]. Treat "no documented hard limit, be polite + cache" as the working
  assumption.

**Episode-level quality.** Strong: per-episode name, overview, aired date, episode image
(noted as 4:3 or 16:9 depending on broadcast), season number, episode number, runtime, and
an `absolute_number` [2][5][6]. Image/still coverage for current shows is generally good.

**Data-shape mapping & the ordering gotcha.** An episode base record carries season+episode
numbers *within the series' default season order*, and the series record names which order
is default — "generally Aired Order" [5][6]. TheTVDB also exposes **DVD order**, **Absolute
order**, and named/alternate orders, and `absolute_number` ignores seasons entirely [5][6].
Mediatheca's projection keys episodes by `(season_number, episode_number)` (see
`series_episodes` upsert in `SeriesRefresh.applyToProjection`), which matches TMDB's aired
numbering. **You must request the Aired/Official order from TheTVDB and ignore DVD/Absolute,
or numbering will silently diverge from existing TMDB-seeded rows.** Mapping otherwise is
direct: episode `name→Name`, `aired→AirDate`, `image→StillRef` (download), `overview`,
`runtime`, `seasonNumber→SeasonNumber`, `number→EpisodeNumber`.

### Candidate 2 — Trakt

**New-season latency vs TMDB.** Disqualifying for this problem. Trakt's official FAQ:
"We use TMDB for most movie & tv show information," with automatic refresh "usually within
24 hours" pulling new/updated English seasons, episodes, titles, specials and air dates
*from TMDB* [4]. So Trakt cannot have *Interview with the Vampire* S3 before TMDB does — it
reduces staleness once data exists upstream but adds no coverage TMDB lacks. (A forum note
adds that Trakt syncs season/episode numbers from TVDB but air dates from TMDB [4] — even so,
the season has to exist on TMDB to appear.) This is essentially a single authoritative
source — Trakt about itself — but it is the strongest kind of single source.

**API access / licensing / rate limits.** OAuth app (client id/secret), free to register;
rate limit commonly cited as ~1,000 calls / 5 min, with separate authed (user) vs
unauthed (app) buckets, and some endpoints VIP-gated (426 when not VIP) [5, secondary].
Commercial terms on the free plan are an open question raised in Trakt's own forums without
a clear public answer [5] — flag as unresolved.

**Episode-level quality & mapping.** Adequate (titles, numbers, air dates; images are
weaker and often deferred to TMDB), but moot given the latency disqualifier — adopting
Trakt would mean a new adapter for data that is, by construction, no fresher than TMDB.

### Candidate 3 — Jellyfin-as-source (leading hypothesis)

**New-season latency vs TMDB.** Best-aligned to the actual failure mode. The gap only ever
manifests for a series the user is *watching*, and that series is in their Jellyfin library
with the season already scraped. The empirical trigger is the proof: IWTV S3 is present in
the user's Jellyfin while TMDB still lists 2 seasons — so at the exact moment the sync
reports an S3 episode the projection lacks, Jellyfin already holds that season/episode
locally — zero additional latency, no external round-trip.

⚠️ UNVERIFIED — which provider scraped it: TheTVDB is an *optional* Jellyfin plugin, not the
default. TMDB is Jellyfin's built-in default TV provider (TheTVDB was removed from core
~v10.7) [8]. The S3 case demonstrates this user's Jellyfin currently holds the season
*however that came to be* — but IF this user's Jellyfin runs the default TMDB scraper, it
could in principle carry the same lag as TMDB on a future title. The observed trigger shows
it does not today; the structural guarantee depends on the user's Jellyfin config (which
provider plugins are installed and preferred). Worth confirming before relying on Jellyfin
to *always* lead TMDB.

**API access / licensing / rate limits.** None new. The Jellyfin adapter
(`src/Server/Jellyfin.fs`) is already integrated, authenticated (with the integration-002
re-auth-and-retry policy), and used by the nightly sync. No key, no third-party terms, no
external rate limit — it's the user's own server.

**Episode-level quality & mapping.** `/Shows/{seriesId}/Episodes` already returns exactly
what `EpisodeImportData` wants (`.documentation/jellyfinapi.md:109-213`):
- `IndexNumber` → `EpisodeNumber`
- `ParentIndexNumber` → `SeasonNumber` (so the season grouping for `SeasonImportData` is
  free)
- `Name` → `Name`, `Overview` → `Overview`
- `PremiereDate` → `AirDate`
- `RunTimeTicks` → `Runtime` (ticks→minutes)
- `ImageTags.Primary` → still: fetch `/Items/{id}/Images/Primary` and store as `StillRef`
- `TmdbRating` → not provided by Jellyfin → `None` (acceptable; TMDB backfills later)

Current adapter limitations to note for the follow-up task (not blockers):
`JellyfinBaseItem`'s decoder only reads `Tmdb`/`Imdb` provider IDs and the episode fetch
requests `Fields=ProviderIds` only — it does **not** yet decode `PremiereDate`,
`ImageTags`, `Overview`, or `RunTimeTicks` for episodes (`Jellyfin.fs:220-230`). The
record already has `Overview`/`RunTimeTicks` fields; you'd add `PremiereDate` +
`ImageTags` to the decoder and widen the episode `Fields=` query string. Numbering matches
TMDB's aired convention (Jellyfin uses S/E aired numbering), so **no ordering remap is
needed** — a meaningful advantage over TheTVDB.

### (a) Comparison at a glance

| Axis | TheTVDB | Trakt | Jellyfin-as-source |
|---|---|---|---|
| New-season latency vs TMDB | Faster / earliest [3] | Same as TMDB (sources TMDB) [4] | Already present locally for watched titles [8] |
| New external dependency | Yes (adapter + key/PIN) | Yes (adapter + OAuth) | **No — adapter exists** |
| Access / cost | Sub/PIN $11.99/yr or license; attribution [1][6] | Free OAuth; commercial terms unclear [5] | None (user's own server) |
| Rate limits | Undocumented; cache advised [1][2] | ~1000/5min, VIP gating [5] | N/A |
| Episode quality | Strong; stills 4:3/16:9 [2][6] | Adequate; weak images | Whatever provider Jellyfin used (TMDB default / TVDB if plugin) [8] |
| Numbering mismatch risk | **Yes** — Aired/DVD/Absolute; pin Aired [5][6] | Low (TMDB-aligned) | **None** — aired S/E like TMDB |
| Maps to EpisodeImportData | Direct (after order pin) | Direct | **Direct, fields already returned** |

### (b) Recommendation

**Adopt Jellyfin-as-source as the fallback, SUPPLEMENTING TMDB (do not replace TMDB).**

Rationale:
1. It targets the real failure mode precisely — the missing season is always one the user
   is watching, hence always already in Jellyfin.
2. Zero new external dependency, key, subscription, attribution obligation, or rate-limit
   surface — the adapter, auth, and re-auth policy already exist.
3. Numbering is aired-order S/E, matching the existing TMDB-seeded `series_episodes` rows,
   so a Jellyfin-materialized episode slots in without a remapping layer (TheTVDB would
   require pinning Aired Order to avoid silent divergence).
4. It reuses data the user already holds locally, without taking on any external API or
   licensing cost. (Note: do *not* assume this data is TheTVDB-sourced — TheTVDB is an
   optional Jellyfin plugin, while TMDB is Jellyfin's default TV provider [8]. The
   recommendation rests on the *empirical* fact that the user's Jellyfin holds the missing
   season — proven by the IWTV S3 trigger — not on any assumed TheTVDB provenance.)

Keep TMDB authoritative: it has richer imagery, ratings, overviews, and multi-language
support, and the nightly refresh will backfill/overwrite the Jellyfin-materialized rows
once volunteers add the season upstream. Design the fallback as **"materialize the
season/episode from Jellyfin when the sync reports one the projection lacks,"** marking
those rows so a later TMDB refresh can enrich them (and so a missing `TmdbRating`/still is
expected, not an error).

Why not TheTVDB directly: it is the better *raw* source, but it costs a new adapter, a
subscription/PIN auth flow, attribution, and explicit season-order handling — to obtain
data the user already has locally. Revisit TheTVDB only if a future need arises for
series *not* in Jellyfin (e.g. wishlist/discovery of unaired or unowned titles).

Why not Trakt: it cannot be fresher than TMDB for this problem by its own design [4].

### (c) Sizing note for the follow-up implementation task

- **Reuse Jellyfin adapter (recommended): ~S (small).** Work: (1) widen the episode
  decoder/`Fields=` to include `PremiereDate`, `ImageTags`, `Overview`, `RunTimeTicks`
  (`Jellyfin.fs:220-230`, `decodeBaseItem`); (2) a small mapper Jellyfin episode →
  `EpisodeImportData` / group into `SeasonImportData` by `ParentIndexNumber`; (3) fetch +
  store the Primary image as a still via the existing `ImageStore`; (4) wire a
  "materialize missing season/episode from Jellyfin" branch into the sync path (where the
  sync already detects an episode with no projection row); (5) flag/event so a later TMDB
  refresh enriches it. No settings UI, no key, no new HTTP client.
- **New TheTVDB adapter + key: ~M–L (medium-large).** Work: new authenticated HTTP client
  (login → bearer; user-supported needs PIN), settings UI for key/PIN, episode+season
  decoders, **season-order selection (pin Aired Order)**, image/still download with
  attribution handling, and fallback orchestration — all to fetch data the user already
  holds locally.

## Sources
1. [TheTVDB — API and Data Licensing](https://www.thetvdb.com/api-information) — official access tiers, attribution, "terms may change". Tier $ figures from a single page render (flagged).
2. [thetvdb/v4-api README (GitHub)](https://github.com/thetvdb/v4-api/blob/main/README.md) — season types, episode image formats, caching recommendation, no published rate limit.
3. [Simkl — Why is TVDB better for TV shows than TMDB?](https://docs.simkl.org/how-to-use-simkl/getting-started-with-simkl/basic-navigation/tv-shows-tracking/why-is-tvdb-better-for-tv-shows-than-tmdb) + Sonarr/Plex/Kodi forum threads — community testimony that TVDB updates new TV faster than TMDB (anecdotal, directionally consistent).
4. [Trakt FAQ — How does metadata get updated? / sync to TMDB](https://forums.trakt.tv/t/how-does-movie-tv-show-information-metadata-get-updated-how-can-i-refresh-or-sync-trakt-to-tmdb/22124) — Trakt sources most TV info from TMDB; ~24h refresh. Authoritative for the latency disqualifier.
5. [Trakt API docs (Apiary)](https://trakt.docs.apiary.io/) + [Trakt forum: commercial use on free plan](https://forums.trakt.tv/t/asking-about-api-commercial-uses-on-free-plan/99367) — ~1000 calls/5min, VIP gating; commercial terms unresolved (secondary/single-source).
6. [TheTVDB support — Licensed vs User-supported keys & subscriptions](https://support.thetvdb.com/kb/faq.php?id=62) + [id=81](https://support.thetvdb.com/kb/faq.php?id=81) + [id=82](https://support.thetvdb.com/kb/faq.php?id=82) — PIN/end-user subscription model, ~$11.99/yr or free via contribution.
7. [koditips — TVDB paid subscription model](https://koditips.com/tvdb-paid-subscription-kodi/) — secondary corroboration of the subscription/PIN shift.
8. [Jellyfin metadata identifiers](https://jellyfin.org/docs/general/server/metadata/identifiers/) + [jellyfin-plugin-tvdb (DeepWiki)](https://deepwiki.com/jellyfin/jellyfin-plugin-tvdb/4.1-series-metadata-provider) + Jellyfin GitHub issues [#7550](https://github.com/jellyfin/jellyfin/issues/7550) / [#13294](https://github.com/jellyfin/jellyfin/issues/13294) — TheTVDB is an *optional installable plugin*, removed from Jellyfin core (~v10.7); TMDB is the built-in default TV provider and is preferred for shows. Which provider supplied a given library's metadata depends on the user's installed/preferred plugins. The empirical claim used here — that *this* user's Jellyfin holds IWTV S3 while TMDB does not — is from the task's stated trigger, not from provider provenance.
9. In-repo: `src/Server/Jellyfin.fs` (existing adapter, `getEpisodes`, decoders) and `.documentation/jellyfinapi.md:109-213` (episode endpoint fields: `IndexNumber`, `ParentIndexNumber`, `PremiereDate`, `ImageTags.Primary`, etc.).

## Open questions
- **Exact TheTVDB lead time** for a specific airing season (e.g. IWTV S3) vs TMDB is not
  benchmarked — community claims are directional only [3]. Low importance given the
  recommendation routes around external sources.
- **TheTVDB licensed-tier $ figures** came from a single page render [1]; verify against a
  live quote if a TheTVDB direction is ever pursued. Not blocking for the Jellyfin path.
- **Trakt free-plan commercial terms** remain unanswered in public docs [5]. Moot under the
  recommendation.
- **Which metadata provider the user's Jellyfin actually uses** is unconfirmed (TMDB is
  Jellyfin's default; TheTVDB is an optional plugin) [8]. The IWTV S3 trigger proves the
  season is present locally today, but whether Jellyfin will *structurally* lead TMDB on
  future titles depends on the user having the TVDB (or another non-TMDB) provider plugin
  installed and preferred. If Jellyfin runs only the default TMDB scraper, it could share
  TMDB's lag on the next title. Worth a one-line check of the user's Jellyfin library
  config before assuming Jellyfin is a guaranteed-fresher source. Does not change the
  recommendation (the fallback still strictly improves on TMDB-only and costs nothing new).
- **Jellyfin season-level metadata** (season name/overview/poster): the episode endpoint
  gives per-episode data and `ParentIndexNumber`; confirm whether a season title/poster is
  wanted for materialized seasons or whether a synthetic `SeasonImportData` (number-only,
  TMDB to enrich later) is acceptable. Likely the latter.
