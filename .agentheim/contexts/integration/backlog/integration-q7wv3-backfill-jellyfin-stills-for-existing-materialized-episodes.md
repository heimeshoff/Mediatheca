---
id: integration-q7wv3
title: Episodes materialized before integration-007 never get a still — the backfill gap
status: backlog
type: bug
context: integration
created: 2026-08-01
completed:
depends_on: []
blocks: []
tags: [jellyfin, stills, materialize, backfill, projection]
related_adrs: [0012, 0039]
related_research: []
prior_art: [integration-007, integration-m4k7p]
---

## Why

`integration-007` wired the Jellyfin still fetch into materialization, but it only fires at the
moment an episode row is **created**. Every episode materialized by an *earlier* sync — while the
fetch was still the stubbed `fun _slug _season _ep _jellyfinId -> None` deferral ADR 0012 recorded —
is now permanently stuck with `still_ref = NULL`. The new code is unreachable for them.

This is also the answer to `integration-007`'s still-pending `[human-eye]` criterion 6. The protocol
entry for that task named the exact check: *"a materialized episode (e.g. Interview with the Vampire
S3) showing a real thumbnail instead of the placeholder TV icon."* The builder performed it — and it
shows the placeholder. `integration-007`'s code is correct; its scope simply never reached rows that
already existed.

**Root cause, established against the running code and the live DB:**

1. `JellyfinImport.materializeMissingEpisodes` (`src/Server/JellyfinImport.fs:184`) skips any
   `(season, episode)` present in `getExistingEpisodeKeys`, and that query
   (`src/Server/SeriesProjection.fs:1129`) returns every row regardless of `still_ref`. So the whole
   branch — including the `fetchStill` call — is skipped for an already-present row.
2. Even with the skip removed, `SeriesProjection.materializeEpisode`
   (`src/Server/SeriesProjection.fs:1165`) is `INSERT OR IGNORE`. It cannot fill a column on an
   existing row; the insert is simply ignored. **A backfill needs an `UPDATE` path that does not
   exist today.**

**Confirmed live** (`~/app/mediatheca/mediatheca.db`, series `interview-with-the-vampire-2022`):
season 3 holds seven `source='jellyfin'` episode rows, all with `still_ref = NULL`; seasons 1-2 are
`source='tmdb'` with stills. Zero `*-jellyfin.jpg` files exist anywhere under `images/stills/`, so
the fetch has never once run in production.

Every series synced before `366defb` is affected, not just this one.

## What

Backfill `still_ref` for episode rows that materialization already created but left without a still,
reusing `Jellyfin.getPrimaryImageWithReauth` + `JellyfinImport.fetchEpisodeStill` and the ADR 0039
`stills/{slug}-sXXeYY-jellyfin.jpg` storage path unchanged.

**Scope is Jellyfin-sourced rows only** — `source = 'jellyfin' AND still_ref IS NULL`. A TMDB-sourced
episode with a NULL still stays TMDB's problem. This keeps the Jellyfin adapter inside ADR 0012's
"fill a gap the primary source has not yet covered" boundary rather than turning it into a general
fallback image source for TMDB's own gaps.

Still best-effort throughout: a failed fetch leaves `still_ref = NULL` and never becomes a sync error
(the existing `SeriesMaterializeResult.Errors` / `SyncFailed` contract is unchanged by this task).

## Acceptance criteria

- [ ] An episode row with `source='jellyfin'` and `still_ref IS NULL` gets its still fetched and
      `still_ref` written on the next Jellyfin sync — a pure `UPDATE` path, since `INSERT OR IGNORE`
      cannot fill an existing row.
- [ ] A TMDB-sourced episode with `still_ref IS NULL` is left untouched — the backfill never sources
      a Jellyfin image for a TMDB row.
- [ ] The backfilled file lands at ADR 0039's `stills/{slug}-sXXeYY-jellyfin.jpg` path, so a later
      TMDB refresh still downloads its own canonical still and repoints `still_ref` (the
      `SeriesRefresh` `ImageStore.imageExists` short-circuit is never fed a Jellyfin file at TMDB's
      path).
- [ ] A row that already has a non-NULL `still_ref` is never re-fetched or overwritten.
- [ ] A fetch failure degrades to leaving `still_ref` NULL and does not add to
      `SeriesMaterializeResult.Errors` or flip the sync to `SyncFailed`.
- [ ] The newly-added episodes path from `integration-007` still behaves exactly as before — a
      genuinely new episode gets its still at materialization time, in one pass, not deferred to a
      later backfill sweep.
- [ ] *Interview with the Vampire* season 3 shows real thumbnails instead of the placeholder TV
      icon, against the live Jellyfin server. [human-eye]

## Notes

**Open question for refinement — the refetch guard.** An episode Jellyfin genuinely has no primary
image for will 404 on every client-initiated sync, forever, once it is in the backfill candidate set.
Deliberately left unresolved at capture; refinement should settle the shape against the real code.
Candidates raised so far:

- No guard — attempt every sync. Simplest, cheap per request, but unbounded repetition.
- Persist a "tried and failed" marker (sentinel `still_ref`, or a side table) so a permanently
  image-less episode is retried at most once. More correct; adds projection state and needs its own
  shape decision — and possibly an ADR, since it puts new state on the projection.

Note the blast radius is bounded in practice: the candidate set is only Jellyfin-materialized rows,
which exist only where TMDB lags a season.

**Second open question — where the backfill lives.** Two shapes, both plausible, not yet chosen:

- Widen `materializeMissingEpisodes`' skip predicate from "row exists" to "row exists *and* has a
  still", and give the writer an UPDATE path. Keeps one pass over the Jellyfin episode list.
- A separate backfill sweep alongside materialization, driven by a
  `WHERE source='jellyfin' AND still_ref IS NULL` query. Keeps `materializeMissingEpisodes` — which
  `integration-007` deliberately left untouched — untouched again.

`integration-007`'s refinement pass caught a near-miss of exactly this kind (the storage-path
collision with `SeriesRefresh`'s `imageExists` short-circuit) by resolving the shape against the real
code before work began. Worth the same treatment here.

**Governing decisions:** ADR 0012 (Jellyfin materializes missing seasons as a projection-only
supplement, TMDB stays authoritative) and ADR 0039 (distinct `-jellyfin.jpg` storage path, accepted
orphan). Neither is challenged by this task — it completes what 0012's deferral, closed by
`integration-007`, left half-reachable.

**Prior art:** `integration-007` (the fetch this task makes reachable for existing rows),
`integration-m4k7p` (the materialization arc itself).
