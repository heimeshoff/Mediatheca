---
id: integration-q7wv3
title: Episodes materialized before integration-007 never get a still — the backfill gap
status: todo
type: bug
context: integration
created: 2026-08-01
completed:
depends_on: []
blocks: []
tags: [jellyfin, stills, materialize, backfill, projection]
related_adrs: [0011, 0012, 0025, 0039]
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

### Resolved implementation shape

Both shape questions the capture deliberately left open were settled during this refinement against
the real code. See `## Notes` for the rejected alternatives and why.

**1 — The backfill lives inside `materializeMissingEpisodes`, as a widened skip predicate.**
Not a separate sweep. Three code-grounded reasons:

- The Jellyfin item id the fetch needs (`ep.Id`) is **already in hand** in `seriesBatch`. A separate
  sweep driven by a `WHERE source='jellyfin' AND still_ref IS NULL` query would have to re-resolve it
  through `JellyfinStore.getEpisodeJellyfinId` — a read of `jellyfin_episode`, a table that
  `clearAll` wipes and that Phase 1 only repopulates for series matched by TMDB id. That is a new
  dependency on conditionally-populated state, in exchange for data the batch already carries.
- A separate sweep needs its own fault isolation, its own result type, and its own `Api.fs` wiring —
  duplicating machinery `materializeMissingEpisodes` already has and already tests.
- The "leave `materializeMissingEpisodes` untouched" argument held for `integration-007`, where the
  change was purely additive (`numstat` 33/0). It does not hold here: **the bug *is* that function's
  skip predicate.** There is no way to fix it without touching it.

Concrete shape:

- Keep `getExistingEpisodeKeys` unchanged for the row-exists decision.
- Add `getJellyfinEpisodesMissingStill : slug -> Set<int * int>` to `SeriesProjection` —
  `SELECT season_number, episode_number FROM series_episodes WHERE series_slug = @slug AND source = 'jellyfin' AND still_ref IS NULL`.
- In the per-episode loop, the existing `if existingKeys |> Set.contains key then () else <materialize>`
  becomes `if existingKeys |> Set.contains key then <backfill-if-candidate> else <materialize>`. The
  **`else` branch stays byte-for-byte identical** — a genuinely new episode still gets its still in
  one pass at materialization time, not deferred to a later sweep.
- Add a `backfillStill : slug -> season -> episode -> stillRef -> Result<unit, string>` writer
  parameter, wired in `Api.fs` to a new `SeriesProjection.backfillEpisodeStill` — the missing UPDATE
  path:

  ```sql
  UPDATE series_episodes SET still_ref = @still_ref
  WHERE series_slug = @slug AND season_number = @season AND episode_number = @episode
    AND source = 'jellyfin' AND still_ref IS NULL
  ```

  The `source = 'jellyfin' AND still_ref IS NULL` predicates are repeated **in the WHERE clause**, not
  merely relied on at candidate-selection time. That makes acceptance criteria 2 and 4 enforced by the
  statement itself, so a TMDB refresh landing between the candidate SELECT and the UPDATE cannot be
  clobbered.
- Add `StillsBackfilled: int` to `SeriesMaterializeResult` so the backfill is observable in the sync
  result rather than silently invisible.

**2 — No refetch guard. Repetition is accepted, deliberately.**
An episode Jellyfin genuinely has no primary image for is re-attempted on every sync, forever. This is
a recorded tradeoff, not an oversight:

- The candidate set is only Jellyfin-materialized rows, which exist only where TMDB lags a season — 7
  rows today across the whole library.
- The set **drains on its own**: `SeriesRefresh`'s `INSERT OR REPLACE` (`SeriesRefresh.fs:254`) omits
  the `source` column, so a REPLACE resets it to the `DEFAULT 'tmdb'` (`SeriesProjection.fs:72`). A row
  leaves the candidate set when TMDB publishes the season — **even if TMDB itself has no still**.
- The per-attempt cost is one LAN GET that 404s, already fault-isolated and already degrading to
  `None`. The sync is client-initiated behind a 5-minute cooldown.
- Each candidate is attempted at most once per sync run (the loop visits each episode once) — the
  repetition is across runs, not within one.

If this ever bites — a large image-less library making the sequential `Async.RunSynchronously` fetches
a visible drag on SPA load — the escalation is the side table in `## Notes`, with its own ADR. Not now.

## Acceptance criteria

- [ ] An episode row with `source='jellyfin'` and `still_ref IS NULL` gets its still fetched and
      `still_ref` written on the next Jellyfin sync, via a dedicated `UPDATE` path — `INSERT OR IGNORE`
      cannot fill a column on an existing row.
- [ ] The `UPDATE` statement itself carries `AND source = 'jellyfin' AND still_ref IS NULL` in its
      `WHERE` clause, so the row is re-checked at write time and not only at candidate selection.
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
      later backfill sweep. The `else` branch of the skip predicate is unchanged.
- [ ] `SeriesMaterializeResult` reports how many stills were backfilled, distinctly from
      `EpisodesMaterialized`.
- [ ] *Interview with the Vampire* season 3 shows real thumbnails instead of the placeholder TV
      icon, against the live Jellyfin server. [human-eye]

## Notes

**Rejected: sentinel `still_ref` as a tried-and-failed marker.** Ruled out on hard evidence, not
taste. `("series_episodes", "still_ref")` is entry 8 of ADR 0025's fifteen-pair `imageRefColumns`
registry (`src/Server/Administration.fs:715`), and `getReferencedImageRefs` collects **every**
non-null, non-empty value in those columns as the "live" side of the orphan diff. A sentinel string
would be read by the orphan scanner as a live reference to a file that does not exist on disk —
polluting a registry whose own comment calls itself LOAD-BEARING and warns that a stale entry "risks
a purge deleting a still-referenced image, not merely mis-reporting a count". The one column that
looked like free storage for this marker is the one column that must not hold a non-path value.

**Rejected for now: a side table for permanently image-less episodes.** Correct, and the escalation
path if the accepted repetition ever becomes visible. Declined now as disproportionate — new
projection state plus its own ADR, in a BC classified generic ("boring plumbing where boring choices
are correct"), to save a handful of 404s against a LAN server.

**Rejected: pre-filtering candidates on `PrimaryImageTag`.** Tempting, because
`JellyfinBaseItem.PrimaryImageTag` is **already parsed** (`Jellyfin.fs:50,97`, decoded from
`ImageTags.Primary`) and sitting unused in `seriesBatch` — a zero-state guard for free. Declined for
the same reason `integration-007` declined it as a pre-check: whether `ImageTags` is populated on the
`/Shows/{id}/Episodes` response is **unverified**, and the failure mode is bad in exactly the wrong
direction. If the field is not populated, every candidate reads `None`, the backfill skips everything,
and the bug survives — silently, with no error and no 404 to notice. An unconditional attempt fails
loudly-enough (a wasted GET) rather than quietly. Worth revisiting only if someone confirms the field's
population against a live server first.

**Governing decisions:** ADR 0012 (Jellyfin materializes missing seasons as a projection-only
supplement, TMDB stays authoritative), ADR 0039 (distinct `-jellyfin.jpg` storage path, accepted
orphan), ADR 0025 (image-ref registry / orphan scan — the reason the sentinel is ruled out), ADR 0011
(the re-auth policy `getPrimaryImageWithReauth` reuses unchanged). None is challenged by this task —
it completes what 0012's deferral, closed by `integration-007`, left half-reachable.

**Prior art:** `integration-007` (the fetch this task makes reachable for existing rows),
`integration-m4k7p` (the materialization arc itself).
