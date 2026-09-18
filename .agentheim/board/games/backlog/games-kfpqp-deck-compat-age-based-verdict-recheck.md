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
related_adrs: [0043, 0045, 0059, 0060, 0066, 0084]
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
- **The cap counts only games that are actually eligible.** games-wkyf0's backoff filter runs in F#
  after the SQL read, so the 25 are taken *after* that filter (age limit → backoff → oldest first →
  take 25). Capping in SQL first would let 25 re-checks sitting in backoff occupy every slot and
  starve the cohort.
- A stored `deck_compat` string that is NULL or not one of the four encoded verdicts is treated as
  Unknown (30 days).
- The cursor tells the job which cohort each candidate came from (first fetch vs. re-check), so the
  job can count them separately. It also returns the verdict on record for re-checks, so the job
  can count how many re-checks came back with a *different* verdict — the only evidence there will
  be for tuning the intervals later.
- A successful re-check overwrites `deck_compat` and re-stamps `deck_compat_fetched_at` via the
  existing `upsertGameDeckCompat`. That writer currently stamps `DateTime.UtcNow` itself; it takes
  the job's injected `now` instead, so the stamp and the age-limit comparison share one clock (an
  injected-clock test would otherwise see a just-re-checked game as still due).
- A failed re-check keeps the verdict and its stamp on record and goes through games-wkyf0's
  `recordDeckCompatFailedAttempt` — the same `min(2^attempts, 30)`-day backoff (ADR-0084), applied
  to the re-check cohort exactly as to the never-fetched one. `upsertGameDeckCompat` already
  clears the failure columns on the next success.
- `BackfillResult` gains `Rechecks` (candidates from the re-check cohort) and `VerdictsChanged`
  (successful re-checks whose verdict differs from the one on record); `Processed`, `Succeeded`,
  `Failed`, `Errors` keep their meaning across both cohorts.
- Intervals and cap are plain constants next to the cursor query, not settings.

## Acceptance criteria

- [ ] A game with a recorded verdict older than its verdict's age limit is returned by the job's
      cursor; one younger than the limit is not — covered for all four verdicts with an injected
      clock (Expecto, in-memory SQLite).
- [ ] With more than 25 due re-checks, one run returns exactly the 25 with the oldest
      `deck_compat_fetched_at`; the rest come due on later runs.
- [ ] Due re-checks that are still inside their failed-attempt backoff do not consume cap slots:
      with 25 such games plus 5 eligible due re-checks, the run returns the 5.
- [ ] Never-fetched games are returned in full regardless of the re-check cap.
- [ ] A re-check that returns a different verdict overwrites `deck_compat` and re-stamps
      `deck_compat_fetched_at` with the injected `now`; a list/detail read then shows the new
      verdict, and the game is no longer due on a second run with the same clock.
- [ ] A re-check whose fetch fails (both the `Error` result and the exception branch) leaves the
      recorded verdict and its stamp untouched, records the failed attempt, and is absent from the
      cursor until `min(2^attempts, 30)` days have passed (not retried the next night).
- [ ] `BackfillResult` reports `Rechecks` and `VerdictsChanged`, and the scheduled job's summary
      line shows first fetches, re-checks and changed verdicts as separate numbers next to the
      existing failed/errors counts.
- [ ] An ADR (scope `games`, `related_tasks: [games-kfpqp]`, amends 0084, ADR-0060's structure)
      records the age-based re-check cohort: the per-verdict limits and why they differ, the
      25-per-run cap and why never-fetched games are exempt, cap-after-backoff, and the timer
      remaining the only trigger. The Games README's Deck-compat entry names it.
- [ ] Cache tier only: `checkProjectionDrift` stays zero for `GameProjection`, no `*Projection.fs`
      file references `MetadataCache` (ADR-0045); pacing comes solely from
      `Steam.throttleStorefrontCall` (ADR-0066).

## Notes

- Refined 2026-09-18 against what games-wkyf0 shipped (now in `done/`, ADR-0084). That task left
  `findGamesNeedingDeckCompatBackfill conn now` returning `(slug, steamAppId)` from a
  `deck_compat_fetched_at IS NULL` read with the backoff applied as an F# `List.filter`, and
  `runBackfill conn jobLock httpClient now`; its Outcome notes the re-check cohort can be added
  against `deck_compat_fetched_at IS NOT NULL` without touching the existing WHERE clause.
- Code: `src/Server/MetadataCache.fs` (`findGamesNeedingDeckCompatBackfill`,
  `upsertGameDeckCompat`, `deckCompatBackoffDays`), `src/Server/GameDeckCompatBackfill.fs`
  (`BackfillResult`, `runBackfill`), `src/Server/Composition.fs` ("Game Deck-compat backfill"
  summary line), `tests/Server.Tests/GameDeckCompatBackfillTests.fs`.
- The ADR criterion is there because games-wkyf0's verifier failed iteration 1 on exactly this:
  a backfill-cursor-shape decision narrated only in task Notes (ADR-0060 and ADR-0084 are the
  precedent). ADR-0084's References name this task by its `backlog/` path; leave that as written.
- The intervals are a modeling-session proposal (2026-09-18), not observed Valve behaviour — cheap
  to tune later since they are constants.
- The job keeps its "backfill" name and 06:00 slot; renaming it is not part of this task.
- No UI change: the existing badge reads `deck_compat` straight through.
- Workers never touch the live database — fixtures only.
