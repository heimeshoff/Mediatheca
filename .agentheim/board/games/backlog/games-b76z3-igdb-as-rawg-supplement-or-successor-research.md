---
id: games-b76z3
title: Research IGDB as a supplement to, or successor of, RAWG for game metadata — game modes / multiplayer modes for non-Steam play facets, franchise and collection links, time-to-beat as a HowLongToBeat scraping replacement, external-id mapping to Steam appIds — and weigh the Twitch client-credential cost against the RAWG identity-card migration question (ADR-0055)
status: backlog
type: spike
context: games
created: 2026-09-23
completed:
depends_on: []
blocks: []
tags: [games, metadata, cache, rawg, igdb, hltb, play-facets, research]
related_adrs: [0043, 0053, 0055, 0081]
related_research: [romm-vs-mediatheca-2026-09-23]
prior_art: [games-a7dqx]
---

## Why

The RomM comparison (research `romm-vs-mediatheca-2026-09-23`) found that of RomM's ten
metadata providers, IGDB is the only one that plausibly adds value to a catalogue of owned,
mostly Steam-sourced games — every other provider identifies ROM files by hash or targets
retro/Flash catalogues Mediatheca never holds. IGDB is also what Playnite, Lutris and RomM
converge on as the general-purpose games catalogue.

Mediatheca's Games BC today runs a fixed two-source model: RAWG is the **identity card**
(name, year, cover/backdrop, genres — event-carried, never re-derived per ADR-0055) and
Steam fills the cache tier (description, release date, Deck compatibility, play facets
derived from store categories per ADR-0053/0054), with HowLongToBeat hours scraped through
a fragile discover-the-Next.js-endpoint-then-POST path in `src/Server/HowLongToBeat.fs`.
Three concrete pressures point at IGDB:

1. **Non-Steam games have no play-facet source.** Facet derivation is Steam-only; a
   RAWG-only game needs a manual `Override_play_facets` for every facet. IGDB's
   `game_modes` and `multiplayer_modes` could fill that gap through the same cache tier.
2. **Games have no franchise/series concept.** IGDB's `franchises` and `collections` would
   give Games what Series already has, and could feed Curation's virtual-collection idea
   flagged in the same research.
3. **HLTB scraping is brittle.** IGDB exposes a `game_time_to_beats` endpoint that could
   replace the scraper — if its coverage and numbers hold up against HLTB for the titles
   actually in the library.

The open architectural question is whether IGDB **supplements** RAWG (a new Integration
adapter writing cache-tier facts, RAWG id stays the identity) or **succeeds** it (IGDB id
becomes the canonical id, a migration of every game's identity card and the RAWG re-link
flow). The second is expensive under ADR-0055 and needs evidence before anyone models it.

## What

A research spike, executed through the `research` skill (the worker agent has no web
access), producing a report in `.agentheim/knowledge/research/` that answers the
questions below with primary-source citations and, where a claim depends on live data,
checks against a sample of games from this library (a mix of Steam-linked and RAWG-only
titles, including at least one indie, one pre-2005 title and one unreleased title).

Questions the report must answer:

- **Access and cost.** What does IGDB access require today (Twitch developer app, client
  credentials OAuth, token lifetime, rate limits, terms for a personal self-hosted app)?
  How does that compare with the RAWG key already in Settings? Is there any
  non-commercial restriction that bites a single-user app?
- **Play facets.** Do `game_modes` / `multiplayer_modes` carry enough to derive
  Mediatheca's `PlayFacets` (Solo / Co-op / Versus / Couch, online vs. couch sub-labels)
  for games Steam does not cover? Map IGDB's vocabulary onto the ADR-0054 facet table and
  name what does not map.
- **Franchise / collection.** What do `franchises` and `collections` look like for the
  sample, and how complete are they? Would they be cache-tier facts under ADR-0043?
- **Time to beat.** What does `game_time_to_beats` return for the sample versus the HLTB
  values already cached? Report coverage (how many sample titles have a value at all) and
  drift (how far the numbers differ), so the replace-or-keep decision rests on numbers.
- **External ids.** Does `external_games` reliably map to Steam appIds (and back) for the
  sample? This decides whether IGDB can be linked automatically to existing games without a
  manual re-link step.
- **Identity card.** Compare IGDB's name / first release year / cover / genres against
  RAWG's for the sample. Is RAWG's data materially stale or thinner for recent titles? Only
  a clear quality gap justifies the successor path.
- **Recommendation.** Supplement, succeed, or do nothing — with the migration cost of the
  successor path sketched against ADR-0055 (RAWG id as identity, genres never re-derived,
  the existing RAWG re-link flow) and the follow-up tasks each path would need.

**Stop-loss (ADR-0065):** if, mid-spike, the answer is already known and cheap — for
example IGDB's terms rule out this use, or the sample shows no value over RAWG plus Steam
on every axis — record that finding and stop; do not finish every question for
completeness.

## Acceptance criteria

- [ ] A research report exists at `.agentheim/knowledge/research/igdb-vs-rawg-<date>.md`,
      registered in the research index and the Games knowledge INDEX, and has passed the
      `research-reviewer` gate.
- [ ] Every question in **What** is answered with a primary-source citation (IGDB API docs,
      Twitch developer docs, or a live API response quoted in the report), or is explicitly
      listed under Open questions with the reason it could not be answered.
- [ ] The time-to-beat and identity-card comparisons are backed by a table over a named
      sample of at least eight games from this library, listing the IGDB value, the current
      Mediatheca value and the source of each.
- [ ] The play-facet section contains an explicit mapping table from IGDB
      `game_modes` / `multiplayer_modes` values to Mediatheca `PlayFacets` fields, with
      unmapped values named.
- [ ] The report closes with one recommendation (supplement / succeed / do nothing) and,
      for the recommended path, the list of follow-up tasks to capture, each with its
      target bounded context (Games or Integration).
- [ ] If the stop-loss fires, the report states which finding ended the spike and why the
      remaining questions no longer matter.

## Notes

- Trigger: the RomM comparison, `knowledge/research/romm-vs-mediatheca-2026-09-23.md`,
  section 3 ("aligned but narrower") and the 2026-09-23 follow-up discussion that ranked
  IGDB as the only RomM provider worth adopting.
- Prior art `games-a7dqx` built the play-facets cache tier and the Steam derivation
  (ADR-0053/0054). Any IGDB-sourced facet derivation should land in that same cache slice
  and merge through `PlayFacets.merge`, never as new events.
- ADR-0081 already settled "Steam wins over RAWG" for descriptions when both ids are
  present. A third source needs an explicit place in that precedence, per field.
- Adjacent, deliberately out of scope here: SteamGridDB for hero/logo/grid artwork, and
  RetroAchievements as a play-session source for emulated titles. Capture separately if
  wanted.
- Execution: run `/research` with this task's questions rather than dispatching a worker.
  After the report ships, REFINE this task's outcome into build tasks per the
  recommendation.
