---
id: games-kfpqp
title: Deck-compat job — re-check already recorded verdicts once they pass an age limit (Unknown 30 days, Playable/Unsupported 90, Verified 180), capped per night, so Valve's verdict changes reach existing games
status: done
type: feature
context: games
created: 2026-09-18
completed:
depends_on: [games-wkyf0]
blocks: []
tags: [games, metadata, cache, steam, steam-deck]
related_adrs: [0043, 0045, 0059, 0060, 0066, 0084, 0087]
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

## Outcome

The Deck-compat backfill's cursor (`MetadataCache.findGamesNeedingDeckCompatBackfill`) now walks
two cohorts instead of one: the existing never-fetched cohort (`deck_compat_fetched_at IS NULL`,
unchanged in shape and still uncapped) and a new re-check cohort of games whose recorded verdict is
older than a per-verdict age limit (Unknown 30 days, Playable/Unsupported 90, Verified 180 —
ADR-0087). Neither `checkProjectionDrift` nor `*Projection.fs`'s freedom from `MetadataCache`
references was touched — this stays cache-tier only (ADR-0043/ADR-0045), and pacing still comes
solely from `Steam.throttleStorefrontCall` (ADR-0066, verified via `grep -rn "MetadataCache"
src/Server/*Projection.fs` returning only pre-existing comments).

- `src/Server/MetadataCache.fs` — `decodeDeckCompat` (re-introduced, private, local to this
  module — a NULL/unrecognised stored value decodes to `Unknown`), `deckCompatRecheckAgeLimitDays`
  (the 30/90/180-day table), `deckCompatRecheckCap` (= 25). `findGamesNeedingDeckCompatBackfill`'s
  return type changed to `(string * int * DeckCompatibility option) list` — `None` for a
  never-fetched candidate, `Some verdict` (the verdict on record) for a re-check candidate. The
  re-check cohort's own SQL query (`deck_compat_fetched_at IS NOT NULL`) is filtered in F# by age
  limit, then by games-wkyf0's existing failed-attempt backoff (`notInBackoff`, factored out and
  shared by both cohorts), then sorted oldest-`deck_compat_fetched_at`-first and truncated to 25 —
  in that exact order, so a backing-off re-check never occupies a cap slot that an eligible one
  could use. `upsertGameDeckCompat` gained a `now: DateTime` parameter (its caller's injected
  clock) instead of stamping `DateTime.UtcNow` itself, so a re-check's re-stamp and the cursor's own
  age-limit comparison share one clock.
- `src/Server/GameDeckCompatBackfill.fs` — `BackfillResult` gained `Rechecks` (candidates drawn
  from the re-check cohort, a subset of `Processed`) and `VerdictsChanged` (successful re-checks
  whose fresh verdict differs from the one on record). `runBackfill` counts a candidate as a
  recheck the moment it's pulled from the cursor (regardless of outcome), and compares the fetched
  verdict against the recorded one only on a re-check's success. Both the `Error`-result branch and
  the exception branch call `recordDeckCompatFailedAttempt` exactly as before — a re-check's
  recorded verdict and stamp are left untouched by either failure kind, same as a never-fetched
  candidate.
- `src/Server/Composition.fs` — the "Game Deck-compat backfill" job's summary line now reports
  `%d/%d games fetched (%d first fetches, %d re-checks, %d verdicts changed), %d failed, %d errors`.
- `tests/Server.Tests/GameDeckCompatBackfillTests.fs` — the four existing candidate-tuple
  assertions were updated for the new 3-element shape (`None` for a never-fetched candidate). Ten
  new tests: the age-limit due/not-due boundary for all four verdicts, a NULL/unrecognised stored
  verdict re-checking as Unknown, the 25-per-run cap returning the oldest first, a backoff-blocked
  batch of 25 never consuming a cap slot ahead of 5 genuinely eligible re-checks, the never-fetched
  cohort staying uncapped alongside 30 due re-checks, a changed-verdict re-check overwriting
  `deck_compat`/re-stamping `deck_compat_fetched_at` (verified via a `GameProjection.getBySlug`
  read and via the cursor no longer returning the game), an unchanged-verdict re-check not counting
  as a change, a re-check's `Error`-result failure leaving the recorded verdict/stamp untouched and
  respecting the backoff, a re-check's exception-branch failure doing the same (simulated via a
  `BEFORE UPDATE` SQLite trigger that aborts only `upsertGameDeckCompat`'s own write, since
  `Steam.getDeckCompatibility` converts every HTTP-layer exception into a `Result.Error` itself and
  so cannot be used to reach `runBackfill`'s outer exception branch), and a mixed run splitting
  `Processed` into first-fetch/re-check counts correctly. `createConnection` gained
  `PlaySessionProjection.handler.Init` (needed by `GameProjection.getBySlug`'s join against
  `game_play_session`, exercised for the first time in this file). A new `seedManyGames` helper
  seeds several games' `game_detail` rows before calling `MetadataCache.seedFromProjections` exactly
  once — that function is gated by a once-per-database settings marker, so seeding many games via
  the single-game helper in a loop would silently only ever seed the first one (caught and fixed
  during this task's own test run).
- `tests/Server.Tests/GameDeckCompatProjectionTests.fs` — its three `upsertGameDeckCompat` call
  sites updated to pass `DateTime.UtcNow` for the new `now` parameter.

ADR-0087 (new, scope `games`, `related_tasks: [games-kfpqp]`, `amends: [0084]`) records the four
judgment calls: the per-verdict age limits and why they differ, the 25-per-run cap and why the
never-fetched cohort is exempt, cap-after-backoff ordering, and the NULL/unrecognised-verdict
treatment. The Games README's Deck-compat entry (a new "Deck-compat age-based re-check" bullet,
appended to `## Ubiquitous language` alongside the existing "Deck compatibility" and "Deck-compat
retry backoff" bullets) names ADR-0087 explicitly. Doc comments in
`src/Server/MetadataCache.fs` (lines documenting `deckCompatRecheckAgeLimitDays`,
`deckCompatRecheckCap`, and `findGamesNeedingDeckCompatBackfill`) and
`src/Server/GameDeckCompatBackfill.fs` (`BackfillResult.Rechecks`/`VerdictsChanged` and
`Composition.fs`'s summary-line comment) cite "ADR-0087" by that provisional number — if
`finalizeAdrNumbering` renumbers it at integration, those citations need patching to match.

Full suite: 1001/1001 Expecto tests passing (10 added, all others unaffected). `npm run build`
(Fable client build) green — no client files were touched by this task, so this just confirms
nothing else regressed. `npm run test:client` was not run since no client/shared files changed.
