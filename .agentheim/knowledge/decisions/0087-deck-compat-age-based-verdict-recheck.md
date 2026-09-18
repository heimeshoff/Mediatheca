---
id: 0087
title: Deck-compat backfill re-checks already-recorded verdicts on a per-verdict age limit, capped at 25/night after backoff, exempting the never-fetched cohort
scope: games
status: accepted
date: 2026-09-18
supersedes: []
superseded_by: []
amends: [0084]
related_tasks: [games-kfpqp]
related_adrs: [0043, 0045, 0059, 0060, 0066, 0084]
related_research: []
---

# ADR 0087: Deck-compat backfill re-checks already-recorded verdicts on a per-verdict age limit, capped at 25/night after backoff, exempting the never-fetched cohort

## Context

The Deck-compat backfill's cursor (`MetadataCache.findGamesNeedingDeckCompatBackfill`) has
always been a permanent `deck_compat_fetched_at IS NULL` drain (ADR-0059) plus, since
games-wkyf0/ADR-0084, a failed-attempt backoff filtering that same never-fetched cohort. Once a
game's first fetch succeeds, though, nothing ever revisits it — Valve's own verdict can change (an
Unknown game gets properly tested and becomes Playable/Verified, a developer patch bumps Playable
to Verified, an anti-cheat change causes a downgrade) and the recorded value simply goes stale
forever. On the dev database 97 games sit at Unknown despite Valve testing more of the catalogue
since this feature shipped. Marco confirmed on 2026-09-18 (games-kfpqp's modeling session) that
changed verdicts should reach existing games, with the timer remaining the only trigger (no event,
no user-facing manual re-check button).

This is the third divergence in this backfill family's cursor shape from the simple permanent
drain — ADR-0060 recorded the first (the release-date backfill's steady-state re-poll while a game
is unreleased), ADR-0084 the second (the failed-attempt backoff) — and both name this exact class
of decision (a backfill-cursor-shape choice with real downstream request-volume/staleness
consequences) as ADR-warranting rather than left in a task file's `## Notes` alone. games-wkyf0's
own verifier failed iteration 1 for exactly this omission. Four judgment calls this task's worker
made, each with a real downstream consequence:

1. **Why the per-verdict age limits differ** (Unknown 30 days, Playable/Unsupported 90, Verified
   180) rather than one uniform interval.
2. **The 25-per-run cap, and why the never-fetched cohort is exempt from it.**
3. **Capping after the failed-attempt backoff filter, not before.**
4. **How an unrecognised or NULL stored verdict is treated for age-limit purposes.**

## Decision

### Per-verdict age limits: 30 / 90 / 180 days

`MetadataCache.deckCompatRecheckAgeLimitDays` maps a recorded verdict to how long it is trusted
before the re-check cohort re-fetches it: `Unknown` 30 days, `Playable`/`Unsupported` 90 days,
`Verified` 180 days. `Unknown` gets the shortest window because it is the verdict most likely
still in flux as Valve tests more of the catalogue — the 97-game Unknown cohort on the dev
database is exactly the population this task exists to eventually resolve. `Verified` gets the
longest window because a game Valve has already fully certified working is the least likely of the
four verdicts to regress; there being no evidence any specific Verified title has ever been
downgraded, a long interval keeps the steady-state request cost low against the least volatile
cohort. `Playable`/`Unsupported` deliberately share the middle value: both can move in either
direction (a developer patch, an anti-cheat change) and there is no observed evidence that either
one is more volatile than the other, so splitting them into two separate constants would only add
false precision.

### The re-check cohort is capped at 25 per run, oldest `deck_compat_fetched_at` first; the never-fetched cohort is not capped

`MetadataCache.deckCompatRecheckCap = 25`. Every game the never-fetched cohort ever stamped ages
into re-check eligibility at roughly the same time (whichever it was first fetched), so without a
cap the very first night any interval elapses would produce one large burst of re-check requests
against the storefront — against a vendor whose traffic shape has already been flagged once
(ADR-0066's context). Capping at 25 per run, taking the oldest `deck_compat_fetched_at` rows first,
drains that initial hump gradually over a few weeks (roughly the size of the dev database's
Steam-linked catalogue divided by the cap) and settles into a low, steady trickle once the hump has
cleared. The never-fetched cohort is deliberately exempt from this same cap: a newly added game
must still be picked up on its very next scheduled run regardless of how many re-checks happen to
be due that same night, since delaying a brand-new game's very first Deck-compat fetch behind an
unrelated backlog of re-checks would be a regression in first-fetch latency this task has no reason
to introduce.

### The cap counts only games that already passed the failed-attempt backoff filter

The ordering is fixed: age limit → failed-attempt backoff (games-wkyf0/ADR-0084's existing
`min(2^attempts, 30)`-day filter, applied identically to the re-check cohort) → sort oldest-first →
take 25. Capping in SQL first (e.g. a bare `LIMIT 25` before applying the backoff) would let up to
25 re-checks that are currently backing off from a failed attempt occupy every cap slot, starving
the run of any actually-eligible re-check — the exact failure mode games-wkyf0 introduced backoff
to avoid, reintroduced one layer up. Filtering the backoff first and capping the survivors instead
guarantees the 25 returned are always the 25 most‑overdue re-checks that are genuinely fetchable
right now.

### An unrecognised or NULL stored verdict re-checks as Unknown (30 days)

`MetadataCache.decodeDeckCompat` (a re-introduced private decode, local to this module — see
"Consequences" below) maps a NULL or any string other than the four known verdicts to `Unknown`.
This mirrors the badge's own honest-degradation default (`GameProjection.readDeckCompat`) and gives
a row whose stored value cannot be trusted at all the shortest, most eager re-check window, so a
data shape this task's authors did not anticipate (a stored value that predates a set of encoded
strings changing, for instance) is corrected as quickly as any genuine Unknown verdict rather than
silently parked behind Verified's much longer 180-day window.

### `upsertGameDeckCompat` now takes the job's injected `now` instead of stamping `DateTime.UtcNow` itself

Before this task, `upsertGameDeckCompat` called `System.DateTime.UtcNow` directly to stamp
`deck_compat_fetched_at`. A re-check's success re-stamps that same column, and the re-check
cursor's own age-limit comparison (`now >= fetchedAt.AddDays(...)`) needs to agree with whatever
clock produced the stamp, or an injected-clock test cannot observe "a freshly re-checked game is no
longer due" without also controlling the real system clock. `upsertGameDeckCompat` now takes `now`
as a parameter, threaded through from `GameDeckCompatBackfill.runBackfill`'s own injected clock —
the same seam games-wkyf0 already established for `recordDeckCompatFailedAttempt`.

## Alternatives considered

- **One uniform age limit for every verdict.** Rejected: it would either re-check a confirmed
  Verified game as aggressively as a genuinely uncertain Unknown one (wasting storefront requests
  against the least volatile cohort), or leave Unknown games stale for as long as Verified ones
  (defeating the primary motivation — the 97-game Unknown backlog).
- **No cap on the re-check cohort.** Rejected: every game the never-fetched cohort has ever stamped
  ages into eligibility together, so the very first night any age limit elapses would burst the
  entire aged cohort against the storefront in one run — exactly the traffic-shape risk ADR-0066's
  context already flags as a live concern.
- **Applying the cap to the never-fetched cohort too, for a single uniform "N candidates per
  night" ceiling.** Rejected: a brand-new game's first-ever Deck-compat fetch would then compete
  with an unrelated backlog of re-checks for cap slots, delaying a genuinely new game's first
  badge for no benefit — the never-fetched cohort has never had a volume problem the way the
  post-hump re-check cohort would.
- **Capping in SQL before applying the failed-attempt backoff filter (e.g. `... ORDER BY
  deck_compat_fetched_at LIMIT 25` as a subquery, backoff filtered afterwards).** Rejected: a run
  with 25+ backing-off re-checks older than the 25 genuinely-eligible ones would return zero
  actually-fetchable candidates that night, silently starving the cohort exactly when the backoff
  mechanism is doing its job.
- **Treating an unparseable/unrecognised stored verdict as ineligible for re-check at all (skip
  it).** Rejected: this would let an already-broken row (whatever caused the unrecognised value)
  never self-heal, the opposite of this task's whole purpose; treating it as Unknown gives it the
  fastest path back to a genuine verdict instead.

## Consequences

### Positive
- The 97-game Unknown backlog (and any future Playable/Unsupported/Verified verdict that goes
  stale) is no longer permanently frozen at whatever Valve said on first fetch — a Valve verdict
  change now reaches an existing game within, at most, its verdict's age limit plus however long
  the 25-per-run cap takes to drain the queue.
- The 25-per-run cap keeps the worst-case nightly re-check burst bounded and predictable, converging
  to a small steady-state trickle once the initial stamped-together hump clears.
- `BackfillResult.Rechecks`/`VerdictsChanged` give the first real evidence (via the scheduled job's
  own summary line) for whether the chosen 30/90/180-day intervals are well-tuned, without adding
  any new persistent storage — they are plain per-run counters.
- `upsertGameDeckCompat`'s injected clock closes a latent test-only gap: every consequence of this
  task is directly testable via `GameDeckCompatBackfill.runBackfill`'s own `now` parameter, with no
  sleep anywhere.

### Negative / accepted tradeoff
- A verdict that changes could still take up to its full age limit (up to 180 days for a Verified
  regression) before this job notices, plus however long the cap takes to reach it if the queue is
  backed up — accepted because, per ADR-0084's own precedent, the badge has no urgency requirement
  anywhere in this codebase and `runJobNow` already offers a manual on-demand run.
- The re-check cohort's own SQL query duplicates most of the never-fetched cohort's column-reading
  shape (steam_app_id join, failed-attempt columns) rather than sharing one parameterised query —
  accepted for readability: the two cohorts' WHERE clauses and post-processing (age-limit filter,
  cap, verdict) are different enough that a single parameterised query would need its own
  conditional logic anyway.
- `MetadataCache.fs` re-introduces a private `decodeDeckCompat`, duplicating (in spirit, not code)
  `GameProjection.fs`'s own private decode of the same column — an accepted, intentional
  duplication per ADR-0045's precedent (`decodeVrSupport`), not a violation: the by-construction
  invariant bars a `*Projection.fs` file from referencing `MetadataCache`, not the reverse, and
  `MetadataCache` decoding its own column for its own cursor's purposes introduces no new coupling.

## References

- `.agentheim/knowledge/decisions/0084-deck-compat-backfill-exponential-backoff-on-failure.md` —
  amended by this ADR: the failed-attempt backoff filter is applied to the re-check cohort exactly
  as it already was to the never-fetched one, and the cap-after-backoff ordering above builds
  directly on that filter without changing it.
- `.agentheim/knowledge/decisions/0060-release-date-cache-partial-precision-sort-and-self-draining-backfill.md`
  — the structural precedent this ADR follows for recording a backfill-cursor-shape divergence, and
  for `findGamesNeedingReleaseDateBackfill`'s own self-selecting-cursor shape this task's cohort
  mirrors.
- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`,
  `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the doctrine
  and cache tier this re-check cohort stays within (cache-tier only, no event, no `Projected`
  table); unaffected by this ADR.
- `.agentheim/knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md` — the pacing
  this task adds no sleep on top of; unaffected by this ADR.
- `src/Server/MetadataCache.fs` (`deckCompatRecheckAgeLimitDays`, `deckCompatRecheckCap`,
  `decodeDeckCompat`, `findGamesNeedingDeckCompatBackfill`, `upsertGameDeckCompat`),
  `src/Server/GameDeckCompatBackfill.fs` (`BackfillResult.Rechecks`/`VerdictsChanged`,
  `runBackfill`'s cohort counting), `src/Server/Composition.fs` ("Game Deck-compat backfill" job
  summary line) — the code this ADR describes.
