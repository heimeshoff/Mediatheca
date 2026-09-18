---
id: integration-fn3yx
title: Audible sync decides on listened minutes and calculates the percent — position comes from `last_position_heard`, not the library listing's unreliable `percent_complete`; fixes the decoder that reads that endpoint at the wrong nesting level (reverses ADR-0082's "the sync never fetches last-listened").
status: doing
type: feature
context: integration
created: 2026-09-18
completed:
depends_on: [books-wk67x]
blocks: []
tags: [audible, integration, reading-progress, sync, minutes, last-position-heard]
related_adrs: [0082, 0076, 0074, 0066]
related_research: [audible-finished-and-last-listened-timestamps-2026-09-18, audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [integration-dtdbb, integration-dvbjp, integration-jjvg2]
---

## Why
Builder ruling 2026-09-18: the Audible sync should take **minutes** as the discerning factor for
whether something is written, and the **percent should be calculated instead of fetched**.

Live evidence, 2026-09-18, "For We Are Many" (ASIN `B06Y5MY16Q`, runtime 539 min), queried right
after a two-minute listen followed by a Settings-triggered sync:

- `GET /1.0/library/{asin}` → `percent_complete: 0.0`, `listening_status.percent_complete: 0.0`,
  `time_remaining_seconds: 32340` (the full runtime), `finished_at_timestamp:
  "2026-09-18T08:23:27.153Z"`. Two days earlier the same listing said 14 %. The library listing's
  percent resets when playback starts — it is not a trustworthy position.
- `GET /1.0/content/{asin}/metadata?response_groups=last_position_heard` → HTTP 200 under
  bearer-only auth, body `{"content_metadata":{"last_position_heard":{"last_updated":"2026-09-18
  08:25:17.014","position_ms":3627052,"status":"Exists"}},"response_groups":[...]}` — the true
  position, about 60 min. This also settles the research report's "unverified under bearer-only
  auth" flag: it works.

Consequences seen: the sync stored `Reading_progress_observed { percent 0; Minutes (0, 539) }`,
wiping the visible position. And even with a correct listing, `AudibleSync.percentOf` floors to a
whole percent and `Books.decideObserveProgress` no-ops on an equal percent, so a two-minute listen
of a nine-hour book (0.37 %) could never produce a history entry.

Second defect found on the way: `Audible.decodeLastPositionHeard` reads `last_position_heard` at
the top level of the body, but the wire nests it under `content_metadata`. It therefore always
decodes to "nothing to report". This is why the 2026-09-18 import reported `0 priors from Audible,
84 priors dated today`. Every test stub in `AudibleLibrarySyncTests.fs` (lines 149, 656, 827, 958)
uses the wrong, un-nested shape, which is how it shipped green.

## What
1. **Fix the decoder.** `decodeLastPositionHeard` reads `content_metadata.last_position_heard`.
   Correct every test stub to the real nested wire shape quoted above. No fallback to the
   top-level shape — it never existed on the wire.
2. **The sync fetches the last position.** `AudibleSync.runProgressSync` calls
   `Audible.getLastPositionHeard` for every library item, through the existing 700 ms metadata
   throttle (ADR-0066 shape), reusing the token `withAccessToken` already minted. For a library of
   about 84 titles that is roughly one minute per run, nightly and from Settings alike — accepted.
   The job lock is still never held across an awaited HTTP call (ADR-0028 discipline already in
   this module).
3. **Minutes decide.** The observation's `Position` is `Minutes (positionMs / 60000, Some
   runtime)`. Whether anything is written is the aggregate's call under `books-wk67x`'s rule — no
   change in percent AND position means no event — so a change of even one minute writes an entry
   and an untouched title writes nothing.
4. **Percent is calculated.** `percent = floor (minutes / runtimeMinutes × 100)`, clamped to
   0–100. The library listing's `percent_complete` is no longer an input whenever both
   `position_ms` and `RuntimeMinutes` are known. `is_finished` from the library listing stays the
   source of the `Finished` flag; a finished title whose calculated percent is below 100 keeps its
   calculated percent and finishes via the flag, exactly as the flag works today.
5. **Degraded cases, no guessing.**
   - `status = "DoesNotExist"` (never started): no observation, unless `is_finished` is true, in
     which case observe `Percent = 100`, `Position = None`, `Finished = true`.
   - The metadata call fails for an item (network, 401 after the retry, 5xx): skip that item for
     this run and add it to the result's `Errors` — never fall back to the listing's percent,
     which is the value this task exists to stop trusting.
   - `RuntimeMinutes` unknown: fall back to today's behaviour for that item (floored
     `percent_complete`, `Position = None`) — the only remaining use of the listing's percent.
6. **`ObservedOn` follows Audible's own day.** `observationFor` already prefers
   `lastListened.LastUpdatedOn` over `today`; passing the fetched value makes a nightly run that
   fires after midnight date yesterday evening's listening to yesterday. Keep the date-part
   truncation as is (see Notes on the timezone).
7. **Import shares the path.** `Api.importAudibleLibraryImpl` keeps issuing
   `Record_prior_reading_progress`, and gains the same calculated-percent rule through
   `observationFor`, so import and sync stay one pure decision (ADR-0076's "only Audible-sourced
   writer").
8. **ADR.** Write an ADR amending ADR-0082 §2 and its Consequences ("the sync never fetches a
   last-listened date", "stays a single `/1.0/library` call") and ADR-0076 §1 ("percent computed
   from the source's own percent") for Audible. Replace the counting-stub test that pins "the
   nightly sync never calls the metadata endpoint" with one that pins one call per library item.
   Update the Integration README's Audible sync description and the module doc comments in
   `AudibleSync.fs` and `Audible.fs` that state the old rule.

## Acceptance criteria
- [ ] Decoder test: the real wire body `{"content_metadata":{"last_position_heard":{"last_updated":"2026-09-18 08:25:17.014","position_ms":3627052,"status":"Exists"}}}` decodes to `LastUpdatedOn = Some "2026-09-18"`, `PositionMs = Some 3627052L`; a nested `"DoesNotExist"` decodes to both `None`.
- [ ] Every `last_position_heard` stub in `AudibleLibrarySyncTests.fs` uses the nested shape, and the import test expecting `PriorsFromAudible = 1` passes against it.
- [ ] Sync test reproducing the incident: the library listing reports `percent_complete 0.0` while the metadata endpoint reports `position_ms 3627052` for a 539-minute title — the observation written is `Percent = 11`, `Position = Minutes (60, Some 539)`.
- [ ] Sync test: the position moves from 60 to 62 minutes with the calculated percent unchanged at 11 — a second `Reading_progress_observed` is written.
- [ ] Sync test: position and percent unchanged since the last run — zero events appended, `Observed = 0`.
- [ ] Sync test: the metadata call fails for one item — that item is skipped and listed in `Errors`, the other items are observed, and the listing's percent is not used for the failed item.
- [ ] Sync test: `DoesNotExist` with `is_finished = false` writes nothing; with `is_finished = true` writes a finishing observation.
- [ ] Sync test: a title with no `RuntimeMinutes` falls back to the floored listing percent.
- [ ] Counting-stub test pins exactly one metadata call per library item per run, each through `throttleMetadataCall`.
- [ ] The observation's `ObservedOn` is the date part of `last_updated` when present, else the run's local date.
- [ ] ADR written amending ADR-0082 and ADR-0076 §1; Integration README and the stale doc comments updated.
- [ ] `npm run build` and `npm test` pass.

## Notes
- Code sites: `src/Server/Audible.fs` (`decodeLastPositionHeard` near line 569, `getLastPositionHeard`, `throttleMetadataCall`), `src/Server/AudibleSync.fs` (`percentOf`, `observationFor`, `runProgressSync`), `src/Server/Api.fs` (`importAudibleLibraryImpl` near line 2079–2140), `tests/Server.Tests/AudibleLibrarySyncTests.fs`.
- Depends on `books-wk67x`: the minutes-sensitive no-op rule and append-only history live in the aggregate. Without it, a minutes-based sync would overwrite same-day entries, priors included.
- Timezone evidence for whoever revisits the truncation: the listen ended 10:25 local (CEST) and `last_updated` read `08:25:17`, so the field is UTC. Truncating the date part therefore misdates listening between 00:00 and 02:00 local by one day. Left out of scope here on purpose; ADR-0082 §4 called the zone undocumented, and this is the first hard data point.
- Capture defaults chosen without the builder in the loop, open to a veto before `work` runs: fetch for every library item rather than only unfinished ones (point 2); skip-and-report on a failed metadata call (point 5); floored percent, keeping ADR-0076's "99.6 never rounds to 100" guard (point 4).
- The dev database is disposable — no repair of the 2026-09-18 0 % observation is wanted.
