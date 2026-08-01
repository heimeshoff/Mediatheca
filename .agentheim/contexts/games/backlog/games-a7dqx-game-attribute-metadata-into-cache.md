---
id: games-a7dqx
title: Move Game attribute metadata into the cache and stop emitting it — 7668 Game_play_mode_added events are literally Steam Store category tags and make up 43% of the entire event log
status: backlog
type: refactor
context: games
created: 2026-08-01
completed:
depends_on: [administration-c3nvp, games-w4tzc]
blocks: []
tags: [games, metadata, cache, steam, rawg, hltb, event-log]
related_adrs: [0012, 0042]
related_research: []
prior_art: [administration-qk3f7]
---

## Why

Live event counts, out of 17,638 total:

- `Game_play_mode_added` — **7668 across 896 games** (8.6 each, up to 56 for No Man's Sky). These are
  literally `details.Categories` from the Steam Store API (`PlaytimeTracker.fs:550, 583-584`):
  "Single-player", "Multi-player", "PvP", "Full controller support".
- `Game_short_description_set` 133, `Game_description_set` 16, `Game_website_url_set` 61,
  `Game_hltb_hours_set` 34.
- `Game_steam_last_played_set` 160 — **redundant** once `Play_session_recorded` exists: "last played"
  is `MAX(date)`, which `GameProjection` **already computes that way** at lines 870, 877 and 902.
  Recommend dropping the column and deriving.

All fail the re-derivability test in `infrastructure-e4kwm` — they are a third party's description of
the work, re-fetchable at any time.

**The harm today is bloat and bad modeling, not broken determinism**: `GameProjection` is a pure
function of the log, so drift for Games is already 0. That is why this is backlog, not the now-slice.

## What

- Cache the demoted fields in `game_metadata_cache` (built by `administration-c3nvp`).
- Apply the same four-part tolerance rule as `series-r2xhv`: **codec kept** (so the Health tab,
  ADR-0029 NDJSON round-trips and the ADR-0032 composer stay intact), **aggregate arm becomes an
  explicit no-op**, **projection arm deleted and column dropped**, **command deleted so the compiler
  finds every emission site**.
- Cache-join in `GameProjection.getBySlug`'s DTO assembly (`GameProjection.fs:442-520`), exactly the way
  `resolveFriendRefs` already joins `friend_list` (lines 395-409).
- **Write and stop-emitting must be one task** — stopping emission before the cache exists means new
  games get no play modes at all.

## Acceptance criteria

- [ ] To be written during refinement.

## Notes

**Backlog only — refine when scheduled.**

**Sequencing constraint worth honouring:** this must not be scheduled *ahead* of the vision's Steam
Import Enhancement or HowLongToBeat Integration items — but both of those should land *after* it, or
they pour thousands more junk events into a log already 43% play-mode tags, making
`administration-z6ymt` larger.

**Open question for the refiner:** `Game_categorized` (genres) is RAWG-sourced at creation but has a
`Categorize_game` command. Keep it as an event if the UI actually exposes genre editing; move it to
cache if not. Needs a five-minute check nobody has done.
