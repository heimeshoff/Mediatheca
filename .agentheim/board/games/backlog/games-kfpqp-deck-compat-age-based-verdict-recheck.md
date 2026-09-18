---
id: games-kfpqp
title: Deck-compat job — re-check already recorded verdicts once they pass an age limit (Unknown 30 days, Playable/Unsupported 90, Verified 180), capped per night, so Valve's verdict changes reach existing games
status: backlog
type: feature
context: games
created: 2026-09-18
completed:
depends_on: [games-wkyf0]
blocks: []
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0059, 0060, 0066]
related_research: []
prior_art: [games-b8xnw, games-ev65k]
---

## Why

The Deck-compat job only ever picks games with `deck_compat_fetched_at IS NULL`. The first
successful fetch stamps the game and removes it from the job permanently; nothing else clears the
stamp or fetches again. So a verdict Valve changes later never reaches an existing game — not
Unknown becoming Verified/Playable as Valve tests more of the catalogue (97 games sit at Unknown on
the dev database), not a Playable-to-Verified upgrade after a developer patch, not a downgrade
after an anti-cheat change. Marco confirmed on 2026-09-18 that changed verdicts should be imported,
with the timer remaining the only trigger.

## What

Give the job a second cohort next to "never fetched": games whose recorded verdict is older than an
age limit that depends on the verdict — the same self-selecting shape
`findGamesNeedingReleaseDateBackfill` uses for unreleased games (ADR-0060).

| Verdict on record | Re-check when `deck_compat_fetched_at` is older than |
|---|---|
| Unknown | 30 days |
| Playable, Unsupported | 90 days |
| Verified | 180 days |

- The re-check cohort is capped at **25 games per run, oldest `deck_compat_fetched_at` first**.
  Without a cap every game stamped during the initial pass comes due on the same night and produces
  one large burst against the storefront; with it the initial hump drains over a few weeks and the
  steady state is roughly ten requests a night for ~1000 Steam games. The never-fetched cohort is
  NOT capped — a newly added game is still picked up the next morning.
- A successful re-check overwrites `deck_compat` and re-stamps `deck_compat_fetched_at` (the
  existing `upsertGameDeckCompat` already does both).
- A failed re-check keeps the verdict on record and goes through games-wkyf0's failed-attempt
  backoff instead of being retried nightly.
- Intervals and cap are plain constants next to the cursor query, not settings.

## Acceptance criteria

- [ ] A game with a recorded verdict older than its verdict's age limit is returned by the job's
      cursor; one younger than the limit is not — covered for all four verdicts with an injected
      clock (Expecto, in-memory SQLite).
- [ ] With more than 25 due re-checks, one run returns exactly the 25 with the oldest
      `deck_compat_fetched_at`; the rest come due on later runs.
- [ ] Never-fetched games are returned in full regardless of the re-check cap.
- [ ] A re-check that returns a different verdict overwrites `deck_compat` and re-stamps
      `deck_compat_fetched_at`; a list/detail read then shows the new verdict.
- [ ] A re-check whose fetch fails leaves the recorded verdict and its stamp untouched and is
      subject to games-wkyf0's backoff (not retried the next night).
- [ ] The scheduled job's summary line reports first fetches and re-checks as separate numbers.
- [ ] Cache tier only: `checkProjectionDrift` stays zero for `GameProjection`, no `*Projection.fs`
      file references `MetadataCache` (ADR-0045); pacing comes solely from
      `Steam.throttleStorefrontCall` (ADR-0066).

## Notes

- Fully refined; it waits in backlog only because it depends on games-wkyf0, which edits the same
  cursor query (`MetadataCache.findGamesNeedingDeckCompatBackfill`) and job
  (`src/Server/GameDeckCompatBackfill.fs`). Promote once games-wkyf0 is in `done/`.
- The intervals are a modeling-session proposal (2026-09-18), not observed Valve behaviour — cheap
  to tune later since they are constants.
- The job keeps its "backfill" name and 06:00 slot; renaming it is not part of this task.
- No UI change: the existing badge reads `deck_compat` straight through.
- Workers never touch the live database — fixtures only.
