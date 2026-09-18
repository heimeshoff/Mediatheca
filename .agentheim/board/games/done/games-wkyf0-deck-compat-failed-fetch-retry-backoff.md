---
id: games-wkyf0
title: Deck-compat job — stop re-fetching permanently failing games every night by recording failed attempts and retrying them on a growing delay
status: done
type: bug
context: games
created: 2026-09-18
completed:
depends_on: []
blocks: [games-kfpqp]
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0059, 0066, 0084]
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

## Verifier note (iteration 1)

**VERDICT: FAIL** — checks 1–5 pass (build + 972/972 Expecto green, every acceptance criterion maps to a passing test, scope clean); check 6 (ADRs for decisions) does not.

**REASONS:**
- Check 6 (ADRs for decisions): the diff embeds a cursor-eligibility decision a future maintainer will ask "why?" about, and `ADRS_WRITTEN: none`. `MetadataCache.findGamesNeedingDeckCompatBackfill` (the new `deckCompatBackoffDays` helper and the `List.filter` on `deck_compat_last_attempt_at`) deliberately diverges the Deck-compat backfill from the permanent `fetched_at IS NULL` drain shape that games-a7dqx/games-b8xnw established and that ADR-0059 explicitly states this job reuses verbatim ("same leave-cursor-NULL-on-failure retry semantics" as `GameFacetBackfill.fs`). After this change that sentence is no longer true in effect for deck-compat while it remains true for facets, and nothing durable records why.
- The project's own on-point precedent is ADR-0060, whose Context names this exact class of decision as ADR-warranting (item 2, "The backfill's steady-state candidate query"). games-wkyf0 is the second such divergence in the same backfill family and got no equivalent record.
- Three distinct judgment calls with real downstream consequences are undocumented outside ephemeral/narrative surfaces: (a) the `min(2^N, 30)` schedule and specifically the flat 30-day ceiling — a game that becomes fetchable again (age-gate cookie fixed, relisted) now waits up to 30 days for its verdict; (b) treating a transient exception and a permanent `Error` result identically rather than classifying them; (c) resetting the counter on success via `upsertGameDeckCompat` rather than a separate writer. The task's `## Notes` narrate (b) — task-file narration is evidence the decision exists, never a substitute for the record.
- The `README_DELTA` bullet is good and should stay, but it does not close this: the Games README already carries an equally detailed prose entry for the release-date backfill's re-poll semantics and ADR-0060 was still written.

**SUGGESTED_FIX:** Write one ADR (scope `games`, `related_tasks: [games-wkyf0]`) recording the Deck-compat retry-backoff decision — context being ADR-0059's "same retry semantics as GameFacetBackfill" no longer holding for this job, at least two options weighed for the failure policy (single shared backoff vs. transient/permanent classification; exponential-with-30-day-cap vs. a flat interval or a give-up-after-N terminal state), and the accepted consequence that a recovered game waits up to 30 days. Follow ADR-0060's structure, and reference the new ADR from the `README_DELTA` bullet. Change nothing else — the code, tests, and README delta are otherwise sound.

**ITERATION_HINT:** likely-fixable

## Outcome

The nightly Deck-compat backfill no longer re-fetches a permanently-failing game every single
night forever. Two new `game_metadata_cache` columns, `deck_compat_failed_attempts` and
`deck_compat_last_attempt_at` (idempotent `ALTER TABLE ... ADD COLUMN`, same guard style as
`deck_compat_fetched_at`), are cache-tier bookkeeping only — no event, no `Projected` table
(ADR-0043/ADR-0045); `deck_compat`/`deck_compat_fetched_at` stay untouched by a failure, so the
badge keeps rendering Unknown exactly as before.

- `src/Server/MetadataCache.fs` — `recordDeckCompatFailedAttempt` (new) increments the attempt
  counter and stamps the attempt time on both the `Error`-result and exception branches.
  `upsertGameDeckCompat` (the only success writer) now also resets both failure columns to
  `NULL` on a genuine success. `findGamesNeedingDeckCompatBackfill` gained a `now: DateTime`
  parameter and a private `deckCompatBackoffDays` helper (`min(2.0 ** attempts, 30.0)`,
  2/4/8/16/30 days): a never-attempted game (`deck_compat_last_attempt_at IS NULL`) is always
  eligible; a previously-failed game is filtered out in F# until its backoff window has elapsed.
- `src/Server/GameDeckCompatBackfill.fs` — `runBackfill` takes the same injected `now` and
  threads it through to the cursor query and to `recordDeckCompatFailedAttempt`.
  `BackfillResult` gained a `Failed: int` field (the `Error`-result branch, previously a silent
  `Error _ -> ()` no-op that under-reported every failure); `Errors` still counts the exception
  branch, now also calling `recordDeckCompatFailedAttempt`. No sleep was added anywhere — pacing
  still comes solely from `Steam.getDeckCompatibility`'s own `throttleStorefrontCall`
  (ADR-0066), unchanged by this task.
- `src/Server/Composition.fs` — the "Game Deck-compat backfill" job spec now passes
  `DateTime.UtcNow` and its summary line reports `%d/%d games fetched, %d failed, %d errors`.
- `tests/Server.Tests/GameDeckCompatBackfillTests.fs` — the existing failure-path test was
  rewritten to pin the new bookkeeping (attempt count/stamp, `Failed`/`Errors` split) instead of
  its old "always retried next run" claim, which the backoff now supersedes. Four new tests:
  backoff absence/return at the 2-day boundary for one attempt, a never-attempted game staying
  eligible regardless of the clock, the 30-day cap (seeded directly at 6 attempts rather than
  running the job six times), and a success-after-failure resetting both bookkeeping columns
  while writing the verdict.

Both new columns live only on `game_metadata_cache`; `GameDeckCompatProjectionTests.fs`'s
`checkProjectionDrift` test is unaffected (still zero discrepancies), and
`grep -rn "MetadataCache" src/Server/*Projection.fs` still returns zero code matches (only
pre-existing comments), matching ADR-0045's by-construction invariant — this task never touched
`GameProjection.fs`.

Re-checking games that already have a recorded verdict (`games-kfpqp`) is explicitly out of
scope and unaffected: `findGamesNeedingDeckCompatBackfill`'s never-fetched cohort
(`deck_compat_fetched_at IS NULL`) is unchanged in shape, just now filtered by the added backoff,
leaving `games-kfpqp` free to add a second cohort against `deck_compat_fetched_at IS NOT NULL`
without touching this query's existing WHERE clause.

**Iteration 2 (verifier note addressed):** verifier iteration 1 passed checks 1-5 (build, all
972/972 tests, every acceptance criterion mapped to a passing test, scope discipline) but FAILed
check 6 — the backoff/classification/reset judgment calls above were narrated only in the task's
own `## Notes` and the README delta, with no durable ADR, diverging from ADR-0059's "same retry
semantics as GameFacetBackfill" claim the way ADR-0060 recorded the release-date backfill's own
prior divergence from the same family. ADR-0084 (new, scope `games`, `related_tasks:
[games-wkyf0]`, `amends: [0059]`) now records: the exponential-with-30-day-cap schedule and why it
was chosen over a flat interval or a terminal give-up state, the deliberate choice to back off
every failure kind identically rather than classify transient vs. permanent, the accepted
consequence that a recovered game can wait up to 30 days, and the reset-only-via-
`upsertGameDeckCompat` choice. The README delta bullet now names ADR-0084 explicitly. No code or
test changed in this iteration — the verifier's own note confirmed those were sound.

Full suite: 972/972 Expecto tests passing (4 added). `npm run build` (Fable client build) green.
Client/Shared files untouched, so `npm run test:client` was not required and not run.
