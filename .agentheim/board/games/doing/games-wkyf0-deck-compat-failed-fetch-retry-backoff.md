---
id: games-wkyf0
title: Deck-compat job — stop re-fetching permanently failing games every night by recording failed attempts and retrying them on a growing delay
status: doing
type: bug
context: games
created: 2026-09-18
completed:
depends_on: []
blocks: [games-kfpqp]
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0059, 0066]
related_research: []
prior_art: [games-b8xnw, games-ev65k]
---

## Why

The scheduled "Game Deck-compat backfill" (06:00) is the only writer of
`game_metadata_cache.deck_compat` — no creation path fetches the verdict, so the job is in effect
the ongoing import path and stays scheduled (modeling session 2026-09-18). Its one real cost is a
bug: when `Steam.getDeckCompatibility` returns `Error` (store page without the
`data-hardwarecompatibility` attribute — delisted apps, age-gate redirects, tools/DLC), the job
leaves `deck_compat_fetched_at` NULL and records nothing, so the same game is fetched again every
single night, forever. On the dev database 118 of 1021 Steam-linked games sit in that state — about
three minutes of pointless store-page requests per night at the 1.5 s storefront throttle, against
a vendor that has already flagged this account's traffic shape once. Those failures are also
invisible: the `Error _ -> ()` branch increments neither `Succeeded` nor `Errors`, so the job
summary under-reports them.

## What

- Record failed attempts in the cache tier: two new `game_metadata_cache` columns,
  `deck_compat_failed_attempts INTEGER` (NULL/0 = none) and `deck_compat_last_attempt_at TEXT`,
  added with the same `ALTER TABLE ... ADD COLUMN` guard style as `deck_compat_fetched_at`.
  Cache tier only — no event, no `Projected` table (ADR-0043/ADR-0045).
- On a failed fetch (both the `Error` result and the exception branch) increment the attempt count
  and stamp the attempt time. `deck_compat`/`deck_compat_fetched_at` stay untouched, so the badge
  keeps rendering Unknown exactly as today.
- `MetadataCache.findGamesNeedingDeckCompatBackfill` keeps its never-fetched cohort but skips a game
  whose last failed attempt is more recent than its backoff delay: `min(2^attempts, 30)` days
  (2, 4, 8, 16, then 30 days flat). A never-attempted game is always eligible.
- A successful fetch clears both failure columns via `upsertGameDeckCompat`.
- The job result/summary reports failed fetches as their own number instead of dropping them.
- The job takes the current time as a parameter (or an equivalent seam) so the backoff is testable
  without sleeping.

Out of scope: re-checking games that already have a verdict (games-kfpqp), fetching at game
creation, and wiring the job into the Steam Family import — all weighed and declined or deferred in
the 2026-09-18 session (Deck compat is never time critical; the timer stays the trigger and
`runJobNow` already offers a manual run).

## Acceptance criteria

- [ ] A game whose fetch fails gets `deck_compat_failed_attempts` incremented and
      `deck_compat_last_attempt_at` stamped; `deck_compat` and `deck_compat_fetched_at` are unchanged.
- [ ] A game with N failed attempts is absent from `findGamesNeedingDeckCompatBackfill` until
      `min(2^N, 30)` days have passed since its last attempt, and present again afterwards
      (Expecto, in-memory SQLite, injected clock).
- [ ] A never-attempted, never-fetched Steam-linked game is still returned on every run.
- [ ] A successful fetch after earlier failures writes the verdict and resets both failure columns.
- [ ] `BackfillResult` and the scheduled job's summary line report failed fetches separately from
      successes and exceptions; the existing stub-HTTP tests in `GameDeckCompatBackfillTests.fs`
      are updated to pin this.
- [ ] Both new columns live only on `game_metadata_cache`; `checkProjectionDrift` stays zero for
      `GameProjection` and no `*Projection.fs` file references `MetadataCache` (ADR-0045).
- [ ] Pacing still comes solely from `Steam.throttleStorefrontCall` (ADR-0066) — the job adds no
      sleep of its own.

## Notes

- Code: `src/Server/GameDeckCompatBackfill.fs`, `src/Server/MetadataCache.fs`
  (`initialize`, `upsertGameDeckCompat`, `findGamesNeedingDeckCompatBackfill`),
  `src/Server/Composition.fs` ("Game Deck-compat backfill" job spec),
  `tests/Server.Tests/GameDeckCompatBackfillTests.fs`.
- All failure kinds back off the same way on purpose: a transient network error costs at most two
  extra days, which does not matter for a never-time-critical badge, and one rule is easier to pin
  than a transient/permanent classifier.
- games-kfpqp (age-based re-check of recorded verdicts) edits the same cursor query and job and
  waits on this task.
- Workers never touch the live database — fixtures only.
