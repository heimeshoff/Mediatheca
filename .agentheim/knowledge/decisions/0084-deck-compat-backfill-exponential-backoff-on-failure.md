---
id: 0084
title: Deck-compat backfill failures back off exponentially (capped at 30 days), uniformly across failure kinds, cleared only by a genuine success
scope: games
status: accepted
date: 2026-09-18
supersedes: []
superseded_by: []
amends: [0059]
related_tasks: [games-wkyf0]
related_research: []
---

# ADR 0084: Deck-compat backfill failures back off exponentially (capped at 30 days), uniformly across failure kinds, cleared only by a genuine success

## Context

ADR-0059 recorded that `GameDeckCompatBackfill.fs` "reuses `GameFacetBackfill.fs`'s shape exactly
... same leave-cursor-NULL-on-failure retry semantics" — a game whose fetch fails simply stays a
candidate forever, retried on every scheduled run with no bookkeeping about the failure itself.
On the dev database this cost about three minutes of pointless store-page requests per night
against 118 of 1021 Steam-linked games that will never resolve (delisted apps, age-gate
redirects, tools/DLC that never carry a Deck verdict at all) — traffic against a vendor that has
already flagged this account's traffic shape once (ADR-0066's context). The failures were also
invisible: the `Error _ -> ()` branch incremented neither `Succeeded` nor `Errors`, so the job's
own summary under-reported them.

This is the second time this backfill family has needed a cursor shape that diverges from
`findGamesNeedingFacetBackfill`'s simple permanent `fetched_at IS NULL` drain — ADR-0060 recorded
the first divergence (the release-date backfill's steady-state re-poll predicate) and named this
exact class of decision as ADR-warranting. games-wkyf0 is the second such divergence in the same
family and, per that project's own precedent, gets its own record here rather than living only in
the task file's `## Notes` (narration is evidence a decision exists, never a substitute for the
record) or in code comments alone.

Three judgment calls this task's worker made, each with a real downstream consequence:

1. **The backoff schedule and its ceiling.** How long should a failing game wait before the next
   retry, and does that wait ever stop growing?
2. **Whether failure kinds are classified.** A `Steam.getDeckCompatibility` `Error` result (Steam
   had nothing usable — most often permanent: a delisted app, an age-gate page, a DLC/tool with no
   Deck verdict at all) and an unhandled exception (most often transient: a dropped connection, a
   momentary DB contention) are mechanically different failure shapes. Should they back off
   differently?
3. **Where the failure counter resets.** A game that failed N times and then succeeds needs its
   bookkeeping cleared somewhere — but by which writer, and is a separate reset ever needed?

## Decision

### One shared exponential backoff, `min(2^attempts, 30)` days, for every failure kind

`MetadataCache.findGamesNeedingDeckCompatBackfill` skips a game whose `deck_compat_last_attempt_at`
is more recent than `min(2^attempts, 30)` days ago, where `attempts` is
`deck_compat_failed_attempts` (2, 4, 8, 16, then a flat 30-day ceiling). Both the `Error`-result
branch and the exception branch in `GameDeckCompatBackfill.fs` call the exact same
`MetadataCache.recordDeckCompatFailedAttempt`, incrementing the same counter and stamping the same
timestamp — there is no transient/permanent classification anywhere in this path. A
never-attempted game (`deck_compat_last_attempt_at IS NULL`) is always eligible regardless of the
clock, so the never-fetched cohort this backfill has always served is unchanged in shape; the
backoff only ever *removes* an already-failed game from a given run's candidate set, temporarily.

This is deliberately looser than treating the two failure shapes differently. A transient
exception now costs the game up to two extra days of staleness on its very first retry (the same
`2^1 = 2` day step a permanent `Error` result gets) before backing off further only if it keeps
failing — acceptable because the badge is never time-critical (nothing in this codebase gates on
"is the Deck verdict fresh") and because a transient failure is, by definition, rare enough that
it is very unlikely to recur on the very next attempt and trigger the deeper steps of the same
curve. Exponential-with-a-cap was chosen over a flat interval (e.g. "always wait 7 days") because
it drains the common case fast — the very first retry after a genuinely transient blip is only two
days out — while still converging to a low steady-state request rate against games that will
never resolve, without ever fully giving up and abandoning a game that might someday become
fetchable (an age-gate cookie fix, a relisted app). The 30-day ceiling, not an unbounded `2^N`, is
what keeps that convergence from taking over a year to reach (`2^9 ≈ 512` days) for a game that
has failed repeatedly since this feature shipped.

### Accepted consequence: a recovered game can wait up to 30 days for its next check

If Steam relists a delisted app, or an age-gate/cookie issue affecting the account gets fixed, a
game that has failed enough times to be at the 30-day ceiling does not resolve until its next
scheduled attempt — up to 30 days later. This is accepted, not overlooked: the Deck-compat badge
has no urgency requirement anywhere in this codebase (unlike, say, a release date the Upcoming
section actively sorts by), and `runJobNow` already offers a manual, on-demand run for the rare
case where someone specifically wants a fresher check sooner. Shortening the ceiling would raise
steady-state request volume against the ~12% of the library that never resolves for the small
benefit of a faster recheck nobody has asked for.

### The counter is reset only by `upsertGameDeckCompat`'s own success path — no separate writer

A successful fetch's only writer, `MetadataCache.upsertGameDeckCompat`, resets both
`deck_compat_failed_attempts` and `deck_compat_last_attempt_at` to `NULL` in the same
`INSERT ... ON CONFLICT DO UPDATE` statement that writes the verdict and stamps
`deck_compat_fetched_at`. No separate "clear failure state" function exists. This keeps the
invariant simple to state and verify: a row's failure bookkeeping is non-`NULL` if and only if its
most recent attempt failed and no success has landed since — there is exactly one code path that
can make that true (`recordDeckCompatFailedAttempt`) and exactly one that can make it false
(`upsertGameDeckCompat`), and a success unconditionally wins.

### ADR-0059's "same retry semantics as GameFacetBackfill" is retracted for Deck-compat

This ADR **amends ADR-0059 in place** (ADR-0059 stays `accepted`). Every other decision in
ADR-0059 survives unchanged: the dead `ajaxgetdeckappcompatibilityreport` endpoint, the
`data-hardwarecompatibility` scrape, the age-gate cookie handling, the independent
`deck_compat_fetched_at` cursor column, the inherited-unmeasured 300ms-turned-1500ms throttle
(now `Steam.throttleStorefrontCall`, ADR-0066). Only the sentence "same
leave-cursor-NULL-on-failure retry semantics as `GameFacetBackfill`" is retracted: it accurately
described the code at the time ADR-0059 was written, but games-wkyf0 makes it false for
Deck-compat while `GameFacetBackfill.fs`'s own play-facets backfill keeps the original permanent
"never fetched" shape unchanged (that backfill has no equivalent failure-cost problem to solve,
since a facet fetch failing this way was not separately observed as a recurring cost the way
Deck-compat's 118/1021 permanently-failing cohort was).

## Alternatives considered

- **Classify failures as transient vs. permanent and back off differently** (e.g. retry a
  transient exception on the very next run, only back off an `Error` result). Rejected: the two
  branches are already easy to tell apart in the code, but doing so correctly from the *outside*
  (deciding, from an `Error` string alone, whether Steam's "nothing usable" reflects a permanent
  delisting or a transient blip on their end) is guesswork this task has no evidence to base a
  classifier on. One shared rule is easier to reason about and to pin in a test than a
  classifier whose boundary cases (a delisted app that gets relisted; a persistent network issue
  that isn't really "transient") would need their own judgment calls anyway.
- **A flat retry interval** (e.g. "always wait 7 days after any failure") instead of exponential
  growth. Rejected: a flat interval either retries the permanently-failing 118/1021 cohort forever
  at a fixed, non-trivial rate (defeating this task's whole purpose if the interval is short), or
  makes every single-blip transient failure wait just as long as a game that has failed dozens of
  times (if the interval is long) — exponential growth gets both right: cheap for a first failure,
  expensive only after repeated failures.
- **A terminal "give up after N attempts" state**, permanently excluding a game from the cursor.
  Rejected: Deck-compat status can genuinely change out from under a game that once failed (Valve
  relists an app, fixes an age-gate quirk, a tools/DLC entry later gains a real verdict) — a
  permanent give-up would need its own manual re-enable path to ever recover, adding UI/API surface
  this task's own scope explicitly excludes (re-checking already-fetched verdicts is games-kfpqp's
  separate, deferred task; this task is about never-fetched-but-failing games, a different cohort).
  The 30-day cap already bounds the cost of never giving up.
- **A separate "clear failure state" writer**, called independently of `upsertGameDeckCompat`.
  Rejected: it would introduce a second place that could disagree with the success writer about
  when failure bookkeeping should be cleared, for no benefit — every success already has exactly
  one writer to extend.

## Consequences

### Positive
- The permanently-failing ~12% of Steam-linked games (118/1021 on the dev database) drop out of
  nightly traffic almost entirely once they reach the 30-day ceiling, closing the "three minutes
  of pointless requests every night, forever" cost this task exists to fix.
- The job's summary now honestly reports failed fetches (`Failed`) separately from exceptions
  (`Errors`) and successes, instead of silently swallowing the `Error` branch.
- The backoff is fully clock-injected (`GameDeckCompatBackfill.runBackfill`'s `now` parameter), so
  every consequence above is directly testable without sleeping in a test.

### Negative / accepted tradeoff
- A game that becomes fetchable again after a run of failures can wait up to 30 days before its
  next attempt — accepted above, mitigated by `runJobNow`'s existing manual-run escape hatch.
- Treating a transient exception identically to a permanent `Error` result means a genuinely
  transient blip costs a game the same first 2-day wait a permanent failure would — accepted as
  cheap given the badge's non-urgency, and avoids inventing an unevidenced classifier.
- `GameDeckCompatBackfill.fs` and `GameFacetBackfill.fs` (which this backfill "reuses the shape of"
  per ADR-0059/games-a7dqx) now genuinely diverge in retry semantics, one more small deviation a
  future reader of both files needs this ADR (and ADR-0060's precedent) to understand rather than
  assuming the two jobs still behave identically end-to-end.

## References

- `.agentheim/knowledge/decisions/0059-steam-deck-compat-endpoint-retired-html-scrape-replacement.md`
  — amended in place by this ADR; every other decision there survives unchanged.
- `.agentheim/knowledge/decisions/0060-release-date-cache-partial-precision-sort-and-self-draining-backfill.md`
  — the direct precedent for recording this class of backfill-cursor-shape decision, and the
  project's own name for why a task-file `## Notes` narration is not a substitute.
- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`,
  `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the doctrine
  and cache tier this failure bookkeeping stays within (cache-tier only, no event, no `Projected`
  table); unaffected by this ADR.
- `.agentheim/knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md` — the pacing
  this task adds no sleep on top of; unaffected by this ADR.
- `src/Server/MetadataCache.fs` (`recordDeckCompatFailedAttempt`, `deckCompatBackoffDays`,
  `findGamesNeedingDeckCompatBackfill`, `upsertGameDeckCompat`),
  `src/Server/GameDeckCompatBackfill.fs` (`runBackfill`'s `now`-parameterized failure branches),
  `src/Server/Composition.fs` ("Game Deck-compat backfill" job spec) — the code this ADR describes.
- `.agentheim/board/games/backlog/games-kfpqp-*.md` — the deferred, separate re-check-recorded-verdicts
  task this ADR's cohort (never-fetched-but-failing) is explicitly not that task's cohort
  (already-fetched-with-a-verdict).
