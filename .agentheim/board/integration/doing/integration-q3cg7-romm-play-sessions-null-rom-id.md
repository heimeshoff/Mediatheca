---
id: integration-q3cg7
title: RomM sync fails outright when any play session has a null rom_id — skip orphaned sessions instead of rejecting the whole page
status: doing
type: bug
context: integration
created: 2026-09-27
completed:
depends_on: []
blocks: []
tags: [romm, sync, decoder]
related_adrs: [0088]
related_research: []
prior_art: [integration-jkbm1]
---

## Why
The builder hit this on a manual "Sync now":

> RomM sync failed: Failed to parse RomM play sessions: Error at: `$.[15].rom_id` Expecting an int but instead got: null

RomM's `GET /api/play-sessions` can return sessions whose `rom_id` is `null` — typically a session whose rom was since deleted/rescanned away in RomM (the FK is nulled, the session row survives). `RomM.decodePlaySession` (`src/Server/RomM.fs:144`) decodes `rom_id` with `get.Required.Field "rom_id" Decode.int`, and `decodePlaySessions` is a plain `Decode.list`, so **one** orphaned session fails the entire page → the whole sync aborts and no other session imports. Every subsequent run hits the same record, so RomM sync is permanently wedged until the orphan disappears upstream.

## What
An orphaned play session (no rom) cannot be attributed to any Game, so it is simply not importable — it must be skipped, not fatal.

- Decode `rom_id` as optional (absent or `null`) at the wire boundary, and drop sessions without a rom before they reach `RomMSync` — `RomMPlaySession.RomId` stays a plain `int` (parse, don't validate: downstream grouping by rom never sees an orphan).
- The rest of the page — and the paging loop (a page's length before filtering still decides "short page = last page") — is unaffected by skipped sessions.
- A genuinely malformed payload (non-list, missing `id`/`start_time`, wrong types other than a null/absent `rom_id`) still surfaces as the existing `Failed to parse RomM play sessions` error — this is not a blanket "ignore bad records".

## Acceptance criteria
- [ ] A fixture page of play sessions where one entry has `"rom_id": null` decodes successfully; the result contains every other session and omits the orphan.
- [ ] A fixture entry with the `rom_id` key absent is treated the same as `null` (omitted, no error).
- [ ] Paging still terminates correctly: a full-length page (page size) that contains an orphan still requests the next page — the short-page check uses the raw item count, not the filtered count.
- [ ] A `RomMSync` run against a fake RomM whose session list contains an orphan imports the other closed sessions onto their Games and reports success (no `romm_last_error` set).
- [ ] A session with a non-integer, non-null `rom_id` (e.g. a string) still fails decoding with the `Failed to parse RomM play sessions` message.
- [ ] Existing RomM tests (`RomMTests`, `RomMSyncTests`, `RomMApiTests`, `RomMPlayButtonTests`) stay green.

## Notes
- Code: `src/Server/RomM.fs` — `RomMPlaySession` type (~l.54), `decodePlaySession`/`decodePlaySessions` (~l.141-151), the paged fetch around l.254-266; consumer `src/Server/RomMSync.fs:277-278` (`List.filter EndTime.IsSome |> List.groupBy RomId`).
- Session-id cursor (games-rmxg2, ADR-0088) is per-Game, so a skipped orphan never needs to consume an id — there is no Game to record it on.
- Tests: `tests/Server.Tests/RomMTests.fs` (decoder fixtures), `RomMSyncTests.fs` (fake-RomM sync runs).
- Optional nicety, not required: count skipped orphans in the run summary/log line so they are visible rather than silently dropped.
