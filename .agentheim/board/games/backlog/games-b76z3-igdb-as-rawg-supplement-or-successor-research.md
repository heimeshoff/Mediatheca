---
id: games-b76z3
title: Research IGDB as a third game-metadata source next to RAWG and Steam — time-to-beat as a scheduled backfill replacing the manual HowLongToBeat scrape (989 of 1026 games have no estimate), game/multiplayer modes as a cross-check of Steam-derived play facets, franchise/collection links, and external_games as a Steam-appId join key — and weigh Twitch client-credential access against what RAWG plus Steam give
status: backlog
type: spike
context: games
created: 2026-09-23
completed:
depends_on: []
blocks: []
tags: [games, metadata, cache, rawg, igdb, hltb, play-facets, research]
related_adrs: [0043, 0053, 0054, 0055, 0060, 0081, 0084]
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: [games-a7dqx]
---

## Why

The RomM comparison (research `romm-vs-mediatheca-2026-09-23`) found that of RomM's ten
metadata providers, IGDB is the only one that plausibly adds value to a catalogue of owned,
mostly Steam-sourced games — every other provider identifies ROM files by hash or targets
retro/Flash catalogues Mediatheca never holds. IGDB is also what Playnite, Lutris and RomM
converge on as the general-purpose games catalogue.

Mediatheca's Games BC today runs a two-source model. RAWG fills the **identity card** at
creation (name, year, cover/backdrop, genres — event-carried, never re-derived, ADR-0055)
and Steam fills the **cache tier** (description, release date, Deck compatibility, play
facets derived from store categories per ADR-0053/0054), each through its own resumable
throttled backfill job with its own cursor column (ADR-0060, ADR-0081, ADR-0084).
HowLongToBeat hours are the odd one out: fetched only by a manual per-game call
(`fetchHltbData` in `src/Server/Api.fs`) through a fragile
discover-the-Next.js-endpoint-then-POST scraper in `src/Server/HowLongToBeat.fs`, with
no backfill job at all.

The live library (read 2026-09-23, 1026 games) reorders the pressures the capture assumed:

1. **Time to beat is the real gap.** Only 37 games have HLTB hours cached (10 of them
   0.0); 989 have no estimate. IGDB's `game_time_to_beats` endpoint could feed a scheduled
   backfill shaped like the Deck-compat / release-date / description jobs — if its
   coverage and numbers hold up against HLTB for the titles actually in the library.
2. **Play facets are a cross-check, not a gap.** 1021 of 1026 games carry a Steam appId
   and 942 have Steam-derived facets; zero manual overrides are in use. Only five games
   are RAWG-only (DOOM 1993, One Must Fall 2097, Dune: Awakening, The Eternal Life of
   Goldman ×2). IGDB's `game_modes` / `multiplayer_modes` matter first as a second
   opinion on the ADR-0054 derivation table, and only second as a source for the handful
   of Steam-less titles.
3. **Games have no franchise/series concept.** IGDB's `franchises` and `collections` would
   give Games what Series already has, and could feed Curation's virtual-collection idea
   flagged in the same research.

On identity: the Game aggregate's identity is its **slug**. `RawgId` is `int option`
(`src/Server/Games.fs`), attached by its own event after creation — one live game
(Tenebris Somnia, Steam 2121510, unreleased) has no RAWG id at all. "IGDB succeeds RAWG"
therefore cannot mean replacing aggregate identity. It can only mean (a) which source fills
the identity-card *fields* at creation, or (b) which external id the cache tier joins on.
Re-sourcing identity-card fields is expensive under ADR-0055 (a one-shot re-stamp
migration or a genuine ongoing refresh path, either way an ADR amendment) and needs
evidence before anyone models it — evidence is this spike's job, the design is not.

## What

A research spike, executed through the `research` skill (the worker agent has no web
access), producing a report in `.agentheim/knowledge/research/` that answers the
questions below with primary-source citations and, where a claim depends on live data,
checks against the named sample in Notes (ten games from this library: Steam-linked and
RAWG-only, indie, pre-2005, unreleased, couch co-op).

Questions the report must answer:

- **Access and cost.** What does IGDB access require today (Twitch developer app, client
  credentials OAuth, token lifetime, rate limits, terms for a personal self-hosted app)?
  How does that compare with the RAWG key already in Settings? Is there any
  non-commercial restriction that bites a single-user app? What rate-limit budget would a
  one-off backfill over ~1000 games need, and how many requests per game does it take
  (one `games` query with expanded fields, or separate endpoints)?
- **Time to beat.** What does `game_time_to_beats` return for the sample versus the HLTB
  values already cached? Report coverage (how many sample titles have a value at all) and
  drift (how far the numbers differ, per estimate: main / main+extras / completionist).
  Could it back a scheduled backfill mirroring `GameDeckCompatBackfill.fs` /
  `GameReleaseDateBackfill.fs` (cache tier, own `*_fetched_at` cursor, failure backoff
  per ADR-0084), replacing the manual-only HLTB path — or should HLTB stay as a
  manual override on top?
- **Play facets.** Map IGDB's `game_modes` / `multiplayer_modes` vocabulary onto the
  ADR-0054 facet table (`Solo`, `CoopCouch`, `CoopOnline`, `VersusCouch`,
  `VersusOnline`, `RemotePlayTogether`, `Vr`) and name what does not map. For the
  Steam-linked sample titles, does IGDB agree with the Steam-derived facets? For the
  RAWG-only titles, does IGDB carry enough to derive facets at all?
- **Franchise / collection.** What do `franchises` and `collections` look like for the
  sample, and how complete are they? Would they be cache-tier facts under ADR-0043?
- **External ids.** Does `external_games` reliably map Steam appIds to IGDB ids (and
  back) for the sample, including the title with no RAWG id? This decides whether IGDB
  can be joined to existing games automatically on Steam appId — the practical key for
  1021 of 1026 games — without a manual re-link step, and what the fallback is for the
  five Steam-less titles (name+year search, RAWG-id crosswalk, or manual link).
- **Identity-card fields.** Compare IGDB's name / first release year / cover / genres
  against RAWG's for the sample. Is RAWG materially stale or thinner for recent titles
  (Dune: Awakening, Tenebris Somnia)? Only a clear quality gap justifies any re-sourcing
  question at all.
- **Recommendation.** Supplement (new Integration adapter writing cache-tier facts, RAWG
  stays the creation-time identity source), succeed (IGDB fills identity-card fields),
  or do nothing — with the follow-up tasks each path would need, each with its target
  bounded context (Games or Integration). If the recommendation leans "succeed", the
  follow-up list must name a `type: decision` task for the ADR-0055 migration/refresh
  mechanism; the report sketches that cost in one paragraph, never a design.

**Stop-loss** (agentheim spike convention): if, mid-spike, the answer is already known
and cheap — for example IGDB's terms rule out this use, or the sample shows no value over
RAWG plus Steam on every axis — record that finding and stop; do not finish every
question for completeness.

## Acceptance criteria

- [ ] A research report exists at `.agentheim/knowledge/research/igdb-vs-rawg-<date>.md`,
      registered in the research index and the Games knowledge INDEX, and has passed the
      `research-reviewer` gate.
- [ ] Every question in **What** is answered with a primary-source citation (IGDB API docs,
      Twitch developer docs, or a live API response quoted in the report), or is explicitly
      listed under Open questions with the reason it could not be answered.
- [ ] The time-to-beat comparison is a table over the ten-game sample in Notes listing,
      per game, the IGDB main / main+extras / completionist values, the cached Mediatheca
      HLTB values (or "none"), and the source of each; a coverage count and a drift
      summary follow the table.
- [ ] The identity-card comparison is a table over the same sample listing IGDB and
      current Mediatheca name, first release year, cover availability and genres side by
      side. Whether RAWG's data is materially stale or thinner is a stated judgment over
      that table. [human-eye]
- [ ] The play-facet section contains an explicit mapping table from IGDB
      `game_modes` / `multiplayer_modes` values to the seven Mediatheca `PlayFacets`
      fields, with unmapped values named, and an agree/disagree column for each
      Steam-linked sample title against its Steam-derived facets.
- [ ] The external-ids section states, per sample game, the IGDB id found via
      `external_games` for its Steam appId (or "no mapping"), and names the fallback for
      the RAWG-only titles.
- [ ] The report closes with one recommendation (supplement / succeed / do nothing) and,
      for the recommended path, the list of follow-up tasks to capture, each with its
      target bounded context (Games or Integration); a "succeed" recommendation names a
      `type: decision` follow-up for the ADR-0055 migration mechanism and caps its cost
      sketch at one paragraph.
- [ ] If the stop-loss fires, the report states which finding ended the spike and why the
      remaining questions no longer matter.

## Notes

- Trigger: the RomM comparison, `knowledge/research/romm-vs-mediatheca-2026-09-23.md`,
  section 3 ("aligned but narrower") and the 2026-09-23 follow-up discussion that ranked
  IGDB as the only RomM provider worth adopting.
- **Sample (ten games from the live library, read 2026-09-23).** Current Mediatheca
  values are given so the researcher never needs database access:

  | Game | Steam appId | RAWG id | Cached HLTB (main / +extras / 100%) | Why it is in the sample |
  |---|---|---|---|---|
  | Disco Elysium (2019) | 632470 | 262382 | 23.7 / 33.1 / 48.2 | InFocus; identity + time-to-beat anchor |
  | Satisfactory (2024) | 526870 | 58806 | 111.0 / 145.9 / 222.1 | InFocus; online co-op facets |
  | Grounded (2022) | 962130 | 391397 | 32.0 / 70.7 / 102.1 | InFocus; co-op survival |
  | Stardew Valley (2016) | 413150 | 654 | none | indie; couch co-op facet |
  | It Takes Two (2021) | 1426210 | 455597 | none | mandatory co-op; facet-mapping stress test |
  | Brotato (2022) | 1942280 | 857690 | none | indie; couch co-op facet |
  | DOOM (1993) | none | 52884 | 11.6 / 16.4 / 27.1 | RAWG-only, pre-2005 |
  | One Must Fall 2097 (1994) | none | 32507 | 7.0 / 22.5 / 24.4 | RAWG-only, pre-2005 |
  | Dune: Awakening (2025) | none | 840775 | none | RAWG-only, recent — tests RAWG staleness on new titles |
  | Tenebris Somnia (2026) | 2121510 | none | none | unreleased (16 Oct 2026), no RAWG id — tests IGDB where RAWG has nothing |

  Genres and years for each are in `game_detail`; the researcher may quote them from the
  RAWG/Steam pages if needed. Stated numbers are hours, rounded to one decimal.
- Prior art `games-a7dqx` built the play-facets cache tier and the Steam derivation
  (ADR-0053/0054). Any IGDB-sourced facet derivation should land in that same cache slice
  and merge through `PlayFacets.merge`, never as new events.
- ADR-0081 already settled "Steam wins over RAWG" for descriptions when both ids are
  present. A third source needs an explicit place in that precedence, per field — the
  follow-up tasks, not this spike, decide it.
- Constraints any follow-up adapter task must respect (Integration BC pattern): its own
  `Igdb.fs` module in `src/Server/`, Settings-held credentials exposed through the API
  the way `getRawgApiKey` / `setRawgApiKey` / `testRawgApiKey` are (`IMediathecaApi`),
  a plain `HttpClient`, an adapter-owned throttle gate mirroring
  `Steam.throttleStorefrontCall` if IGDB has a rate ceiling, and cache-tier writes only
  (`game_metadata_cache`, ADR-0043) with their own `*_fetched_at` cursor.
- Informational, not this spike's fix: the Games README's "Carries a RAWG id (canonical
  metadata)" wording overstates things — `RawgId` is optional and one live game has
  none. Surface it in the report's Open questions if the README wording misled anything.
- Adjacent, deliberately out of scope here: SteamGridDB for hero/logo/grid artwork, and
  RetroAchievements as a play-session source for emulated titles. Capture separately if
  wanted.
- Execution: run `/research` with this task's questions and sample rather than dispatching
  a worker. After the report ships, REFINE this task's outcome into build tasks per the
  recommendation.
