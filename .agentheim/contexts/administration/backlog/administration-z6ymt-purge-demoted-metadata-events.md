---
id: administration-z6ymt
title: Purge demoted metadata events from the event log via the existing ADR-0038 wipe-first import — deferred at the builder's direction because the deployed live version cannot take the migration yet
status: backlog
type: chore
context: administration
created: 2026-08-01
completed:
depends_on: [games-a7dqx, series-r2xhv]
blocks: []
tags: [event-log, migration, cleanup, ndjson]
related_adrs: [0029, 0034, 0038]
related_research: []
prior_art: [administration-n8kqw, administration-vrc56, administration-wwc36]
---

## Why

Once the metadata cache is in place and emission has stopped, roughly 10,000 events in the log are
inert historical junk: 7668 `Game_play_mode_added`, ~566 no-change `Series_refreshed`, plus the
`rawg` / `hltb` / description / website setters and ~1000 duplicate external-identity events.

**BACKLOG ONLY — do not promote.** Deferred at the builder's explicit direction: the deployed live
version cannot take this migration right now.

## What

**This needs no new tooling.** ADR-0038's wipe-first import is exactly the mechanism: export NDJSON →
filter offline → wipe-import in one transaction, with the `VACUUM INTO` backup taken first and
checkpoints reset.

Sequence after every cache-backed BC has cut over.

**The property that makes deferral safe, and that belongs in the task body when this is refined:** the
purge deletes rows whose codecs still exist and which already contribute nothing to any state — every
demoted event type has been reduced to an explicit `evolve` no-op with its projection arm removed. So
**deleting them changes nothing about replay.** The log shrinks; determinism is unaffected either way.

## Acceptance criteria

- [ ] To be written during refinement.

## Notes

**Fold in during refinement:** `GameAddedData` (`src/Server/Games.fs:10-21`) also carries
`Description` / `ShortDescription` / `WebsiteUrl` / `Genres` / `RawgRating` and artwork refs — so the
aggregate has *two* paths to the same external fields. The same demotion argument applies, but it
touches 900+ existing `Game_added_to_library` payloads, which is exactly this task's blast radius
rather than `games-a7dqx`'s.

Expected reduction: from 17,638 events to roughly 7,500.
