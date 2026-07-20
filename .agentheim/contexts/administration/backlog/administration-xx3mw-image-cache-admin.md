---
id: administration-xx3mw
title: Image cache admin — orphan detection, size overview, purge
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, images, storage]
related_adrs: []
related_research: []
prior_art: []
---

## Why
The `images/` cache (posters, backdrops, stills, avatars, cast photos) only ever grows — removed media leaves its images behind, and nothing reports what the cache holds or what is orphaned.

## What
- On the Health tab (or its own section): image cache stats — total size, file count, breakdown by subfolder (posters/backdrops/cast/…).
- **Orphan detection:** collect all image refs referenced by live projections (movie/series/game/friend/catalog rows + cast store), diff against files on disk, list unreferenced files with sizes.
- **Purge:** delete selected (or all) orphans, with a confirmation dialog showing count + total size; deletion is file-system only, never touches the event store.

## Acceptance criteria
- [ ] Stats reflect the actual `images/` directory.
- [ ] An image belonging to a removed movie shows up as an orphan; images referenced by any projection never do.
- [ ] Purge deletes exactly the previewed files and reports the freed size.

## Notes
Refine the ref-collection source: projections (current view) vs. event log (all-time refs). Projection-based is correct for "currently referenced", but a removed-then-re-added movie's images must not be falsely purged mid-rebuild — run detection only when projections are not dirty/rebuilding.
