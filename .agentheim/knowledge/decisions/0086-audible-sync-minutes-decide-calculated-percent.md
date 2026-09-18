---
id: 0086
title: Audible sync decides on listened minutes and calculates the percent from the true position — fixes the last-listened metadata decoder's wrong top-level nesting
scope: integration
status: accepted
date: 2026-09-18
supersedes: []
superseded_by: []
amends: [0082, 0076]
related_tasks: [integration-fn3yx, books-wk67x, integration-dtdbb, integration-dvbjp]
related_research: [audible-finished-and-last-listened-timestamps-2026-09-18, audible-api-surface-and-listening-progress-2026-09-16]
---

# ADR 0086: Audible sync decides on minutes, calculates percent (amends ADR-0082 §2/Consequences, ADR-0076 §1)

## Context

Builder ruling 2026-09-18: the Audible sync should take **minutes** as the discerning factor for
whether something is written, and the **percent should be calculated instead of fetched**.

Live evidence, 2026-09-18, "For We Are Many" (ASIN `B06Y5MY16Q`, runtime 539 min), queried right
after a two-minute listen followed by a Settings-triggered sync: `GET /1.0/library/{asin}` reported
`percent_complete: 0.0` (two days earlier the same listing said 14 %) — the library listing's
percent resets to 0 the instant playback starts and is not a trustworthy position. `GET
/1.0/content/{asin}/metadata?response_groups=last_position_heard` reported the true position,
`position_ms: 3627052` (about 60 minutes), under bearer-only auth — settling the research report's
"unverified under bearer-only auth" flag.

The sync (integration-jjvg2/ADR-0076, amended for priors by ADR-0082/integration-dtdbb) stored
`Reading_progress_observed { Percent 0; Position Minutes (0, 539) }` for this incident, wiping the
visible position. Two compounding defects were found on the way:

1. `AudibleSync.percentOf` floors the source's float percent to a whole percent, and (pre-books-wk67x)
   the aggregate no-op'd on an equal percent — so a two-minute listen of a nine-hour book (0.37 %)
   could never produce a history entry even with a correct listing.
2. `Audible.decodeLastPositionHeard` read `last_position_heard` at the BODY's top level, but the
   real wire nests it under `content_metadata` — `{"content_metadata":{"last_position_heard":
   {...}}}`. The decoder therefore always decoded to "nothing to report" against the real API. This
   is why the 2026-09-18 import reported "0 priors from Audible, 84 priors dated today" — every
   prior silently fell back to today-dating. Every stub in `AudibleLibrarySyncTests.fs` shared the
   same wrong, un-nested assumption, which is how it shipped green.

books-wk67x (ADR-0085, landed the same day) fixed defect 1: the aggregate's no-op rule now compares
percent AND position, so a minutes-sensitive sync can write same-day entries without ever
overwriting a prior (append-only history). This ADR fixes defect 2 and reverses ADR-0082's "the
sync never fetches a last-listened date" now that books-wk67x makes doing so safe.

## Decision

1. **Fix the decoder.** `Audible.decodeLastPositionHeard` reads `content_metadata.last_position_heard`
   via `get.Optional.At [ "content_metadata"; "last_position_heard" ]` (the `Steam.fs` `.At` idiom).
   No fallback to the old top-level shape — it never existed on the wire. Every stub in
   `AudibleLibrarySyncTests.fs`/`AudibleTests.fs` is corrected to the real nested shape.

2. **The sync fetches the last position for every item, reversing ADR-0082 §2.**
   `AudibleSync.runProgressSync` calls `Audible.getLastPositionHeard` for every library item
   (matched, or one the loop just created), reusing the SAME access token `Audible.withAccessToken`
   already minted for the `/1.0/library` fetch (`Api.importAudibleLibraryImpl`'s own precedent —
   no separate per-item refresh/retry orchestration), through the existing 700 ms
   `Audible.throttleMetadataCall` gate (ADR-0066's shape). For a ~84-title library this costs
   roughly one extra minute per run, nightly and from Settings alike — accepted (this task's Notes,
   uncontested). The job lock is never held across an awaited HTTP call (ADR-0028 discipline,
   unchanged). The sync still NEVER issues `Record_prior_reading_progress` — only
   "Import library" is a prior-recording writer.

3. **Minutes decide.** `AudibleSync.observationFor`'s `Position`, whenever a real `position_ms` is
   known, is `Minutes (position_ms / 60000, Some runtime)` — the same shape as before, but now
   ALWAYS computed from a genuinely fetched position rather than defaulting to a percent×runtime
   estimate. Whether anything is written is the aggregate's call under ADR-0085's rule (no change
   in percent AND position means no event), so a change of even one minute writes an entry and an
   untouched title writes nothing.

4. **Percent is calculated.** `AudibleSync.calculatePercent minutes runtimeMinutes = floor (minutes
   / runtimeMinutes × 100)`, clamped 0–100. The library listing's `percent_complete` is no longer an
   input whenever both `position_ms` and `RuntimeMinutes` are known — this is the change that fixes
   the incident, since the listing's own percent is exactly the value proven untrustworthy. A
   finished title (`is_finished` true) whose calculated percent is below 100 keeps its calculated
   percent and finishes via the explicit flag, exactly as the flag already worked.
   `AudibleSync.percentOf` (the pre-existing floor-and-treat-finished-as-100 helper) is demoted to
   a FALLBACK, reached only in the two degraded cases below and by `Api.importAudibleLibraryImpl`'s
   own already-has-an-Audible-row re-observation branch, which never re-fetches metadata at all.

5. **Degraded cases, no guessing — `AudibleSync.observationFor` (the ONE pure decision both the
   import and the sync funnel through) now branches on `lastListened`:**
   - `lastListened = None` (never fetched — the only remaining caller is Api.fs's already-tracked-
     book branch): unchanged pre-fn3yx behaviour verbatim — the listing's own percent, a
     percent×runtime position estimate.
   - `lastListened = Some { PositionMs = Some p; ... }` with `RuntimeMinutes` known: the calculated-
     percent branch (point 4).
   - `lastListened = Some { PositionMs = Some p; ... }` with `RuntimeMinutes` unknown: falls back to
     `percentOf` (the listing's own floored percent), `Position = None` — the only remaining use of
     the listing's percent when a real position genuinely cannot be placed against a total.
   - `lastListened = Some { PositionMs = None; ... }` (`status = "DoesNotExist"`, or an undecodable
     body — both collapse to `emptyLastPositionHeard` inside `Audible.getLastPositionHeard` itself):
     Audible has no position to report at all. NEVER guessed from the listing's percent — the exact
     value this ADR exists to stop trusting — no observation at all, UNLESS the item's own explicit
     `IsFinished` flag says otherwise, in which case `Percent = 100, Position = None`. This rule
     applies uniformly regardless of caller (sync or import's first-ever-prior branch): a DoesNotExist
     body means the same thing — "Audible has no position for this title" — whichever edge asked.
   - A failed metadata HTTP call (network, 401 after retry, 5xx) inside `runProgressSync` skips that
     item for the run and lists it in the result's `Errors`, never falling back to the listing's
     percent for it. `Api.importAudibleLibraryImpl`'s OWN degrade-on-HTTP-failure handling is
     unchanged by this ADR (see Alternatives) — it still collapses a failed round trip to "not
     fetched" and records a today-dated prior from the listing's percent, since the one-time
     bootstrap import has no next run to retry a lost first-ever data point on.

6. **`ObservedOn` follows Audible's own day.** `observationFor` already preferred
   `lastListened.LastUpdatedOn` over `today`; since the sync now supplies a real fetched value for
   every item, a nightly run that fires after midnight now correctly dates yesterday evening's
   listening to yesterday, for the sync as well as the import. The UTC-vs-local timezone question
   this raises (see the task's own Notes) is explicitly left open, per ADR-0082 §4's existing
   "undocumented zone" stance — out of scope here.

7. **Import shares the path.** `Api.importAudibleLibraryImpl` keeps issuing
   `Record_prior_reading_progress` unchanged in shape; it gains the calculated-percent rule and the
   DoesNotExist-skip rule through the same shared `observationFor`, so import and sync remain one
   pure decision (ADR-0076's "only Audible-sourced writer" of `Reading_progress_observed`).

## Alternatives considered

- **Scope the DoesNotExist-skip rule to the sync only, keeping Import's first-ever-prior branch
  always recording something from the listing's percent** — rejected: `observationFor` is
  documented everywhere as ONE shared pure decision; splitting its behaviour by caller identity
  would reintroduce exactly the kind of hidden two-tier logic this task exists to remove. A
  DoesNotExist body means the same thing regardless of which edge is asking.
- **Also apply the "skip and report, never fall back" rule to Import's HTTP-failure handling** —
  considered, rejected: Import's existing degrade-to-today-dated-fallback (integration-dtdbb) is a
  DIFFERENT layer decision than the DoesNotExist rule above — `Api.fs`'s own `fetchLastPositionHeard`
  collapses a failed round trip to "not fetched" (`lastListened = None`), not to
  `Some emptyLastPositionHeard`. Left untouched: the one-time bootstrap import has no next run to
  retry a transient metadata outage on, so recording SOMETHING (today-dated, from the listing) beats
  silently losing that book's only chance at a first-ever data point. The sync, by contrast, runs
  again tomorrow, so skip-and-report is the safer default there.
- **Use raw float minutes (`position_ms / 60000.0`) for the percent formula instead of the
  already-floored integer minutes the Position field carries** — rejected: the task's own "What"
  section formula reads `floor (minutes / runtimeMinutes × 100)`, where "minutes" is the Position's
  own value; using the same already-truncated integer for both fields keeps the observation
  internally consistent (a client reading Percent and Position back always sees them agree).
- **Thread an explicit boolean/parameter through `observationFor` distinguishing "sync call" from
  "import call"** — rejected for the same reason as the first alternative: the whole point of a
  shared pure decision is that it reasons from data (`lastListened`, the item), not from caller
  identity.

## Consequences

- The exact incident (a two-minute listen recording `Percent 0`, wiping a real ~60-minute position)
  cannot recur: the library listing's percent is never trusted once a real position is known.
- The nightly sync now costs one extra metadata call per library item (~1 extra minute for an
  ~84-title library) — the same per-item cost ADR-0082/integration-dtdbb already accepted for
  Import, now paid nightly too. Accepted per this task's Notes.
- A title Audible reports as truly never-started (`DoesNotExist`, not finished) no longer gets a
  fabricated 0 %-or-listing-percent entry from either the sync or a fresh import — a strict
  correctness improvement, though it means a book can now go through creation with zero
  `book_progress` rows until Audible actually reports a position for it.
- `AudibleLibrarySyncTests.fs` needed a substantial rewrite: every metadata stub corrected to the
  real nested shape, the counting-stub test that pinned "the sync never calls the metadata
  endpoint" replaced by one pinning "exactly one call per item", and several fixture percents
  recomputed from real positions instead of the listing's own percent_complete field. 10 net new
  tests; 991 Expecto tests green.
- The UTC-vs-local timezone question in `last_position_heard.last_updated` (flagged as a hard data
  point by this task's own Notes) stays explicitly open, per ADR-0082 §4 — a future task's problem.

## References

- `.agentheim/knowledge/decisions/0082-prior-reading-progress-and-manual-finish-local-date.md`,
  `0076-books-progress-observation-events-and-status-lifecycle.md` — amended by this ADR.
- `.agentheim/knowledge/decisions/0085-books-reading-progress-history-append-only.md` — the
  no-op-rule fix (books-wk67x) this task depends on to write same-day entries safely.
- `.agentheim/knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md` — the
  adapter-owned-throttle shape `Audible.throttleMetadataCall` already followed and this ADR reuses.
- `.agentheim/knowledge/decisions/0074-audible-credentials-imported-from-user-run-auth-file-never-registered.md` —
  the access-token minting/caching this ADR's per-item reuse builds on.
- `.agentheim/knowledge/research/audible-finished-and-last-listened-timestamps-2026-09-18.md` — the
  live wire evidence (nested shape, bearer-only auth working) this ADR is built on.
- `src/Server/Audible.fs` (`decodeLastPositionHeard`, `getLastPositionHeard`,
  `throttleMetadataCall`), `src/Server/AudibleSync.fs` (`percentOf`, `calculatePercent`,
  `observationFor`, `runProgressSync`), `src/Server/Api.fs` (`importAudibleLibraryImpl`),
  `tests/Server.Tests/AudibleLibrarySyncTests.fs`, `tests/Server.Tests/AudibleTests.fs` — the code
  sites this ADR reasons about.
