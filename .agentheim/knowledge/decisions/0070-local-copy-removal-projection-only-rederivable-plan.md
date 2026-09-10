---
id: 0070
title: Local copy removal is projection-only cache invalidation run as a re-derivable plan, not a domain event and not a persisted saga — delete order makes the live world the resume state
scope: integration
status: accepted
date: 2026-09-10
supersedes: []
superseded_by: []
amends: []
related_tasks: [integration-mqsd3, integration-r4vzm, integration-qb7tk]
related_research: []
---

# ADR 0070: Local copy removal is projection-only cache invalidation run as a re-derivable plan

## Context

"Remove local copy" (integration-mqsd3, split into integration-qb7tk / integration-r4vzm /
integration-mqsd3) is Integration's first *destructive* cross-system action: delete a
movie's or series' torrents with their files from qBittorrent, delete the item from
Jellyfin, then forget the Jellyfin id in Mediatheca. Every earlier Jellyfin feature was
read-and-import. Refinement raised two questions.

**Is "local copy removed" a domain event?** The Jellyfin id already lives outside the event
log — in `jellyfin_movie` / `jellyfin_series` / `jellyfin_episode` (`JellyfinStore.fs`) —
and the client-initiated sync runs `clearAll` and repopulates on every pass: the id is
treated as disposable cache today. ADR-0043's operative test is re-derivability, and
whether an item is currently present in Jellyfin can be re-queried at any time without
loss. The *act* ("the user removed it on this date") is not re-derivable, but ADR-0043's
positive clause is narrow — an observation of the user's engagement *with a work*, not with
storage — and an event would push storage vocabulary from a `generic` BC into a `core`
aggregate. The identity-card clause does not bite either: no Movies or Series event carries
a Jellyfin id.

**How does a multi-system delete survive a partial failure?** The naive answer is saga
state: persist which steps ran, resume from there. That is machinery this single-user app
does not need if the steps are ordered so that the plan can always be rebuilt from the live
systems.

## Decision

1. **Projection-only.** Removal deletes the `jellyfin_*` rows directly (new per-item
   `clearMovieJellyfinId` / `clearSeriesJellyfinId` / `clearEpisodeJellyfinId`, never
   `clearAll`) and emits **no domain event**. It is the mirror image of the sync's import —
   both are cache maintenance of a third party's state. The Journal and Intelligence never
   see it.

2. **The one event-worthy step comes first.** The user's play state in Jellyfin up to the
   moment of deletion is an observation of engagement with a work that Jellyfin discards
   with the item — event-worthy under ADR-0043. It is preserved by running the existing
   event-producing paths (`Movies.Record_watch_session`, `Series.Mark_episode_watched`)
   as the mandatory first step, never by inventing a "local copy removed" event to carry it.

3. **Plan-then-execute, two calls, no persisted intermediate state.** `planLocalCopyRemoval`
   resolves the Jellyfin item's `Path`, maps the mount prefix, and matches live torrents
   into a `RemovalPlan` the dialog renders. `removeLocalCopy` takes back the acknowledged
   hashes as data, **re-matches against a fresh torrent list** (the acknowledged set must
   equal the live match set — closes the time-of-check/time-of-use window), and executes
   in fixed order: preserve watch history → delete torrents with files → delete the Jellyfin
   item → verify both gone → clear the `jellyfin_*` rows.

4. **The order is chosen for recoverability.** The Jellyfin item is the *sole carrier* of
   the path that finds the torrents, so it dies last. Every mutating step is idempotent
   (qBittorrent answers 200 for an unknown hash; a Jellyfin 404 on DELETE maps to `Ok ()`;
   the store clears are `DELETE … WHERE`). After any partial failure the plan is
   re-derivable from the live world, so a retry is simply a fresh plan — a
   torrents-deleted-but-Jellyfin-failed retry lands in the already-supported "files only"
   branch. No saga table, no resume token, no compensation logic.

5. **Links clear only after verify.** A failure before the verify gate leaves the ids in
   place, so the detail page keeps telling the truth ("still on the server") and the next
   sync reconciles whatever did change. A removal is refused while a sync is in progress.

6. **Everything matched is acknowledged.** The dialog's per-torrent tick is an
   acknowledgement, not a selection: the Jellyfin delete removes the files under every
   matched torrent regardless, so a partial set is rejected. A pack torrent (content path a
   strict ancestor of the target) is never silently ticked nor silently excluded — it is
   shown with its extra-file count and starts un-ticked.

7. **qBittorrent's session gets no ADR-0011 treatment.** ADR-0011's persisted
   re-auth-and-retry exists for a credential that goes stale over weeks; a qBittorrent
   `SID` is seconds old and one cheap POST away. `withSession` logs in once per operation
   and forgets the cookie.

## Consequences

- Second data point for "does X need an event" in this BC: ADR-0043 applied to
  *operational-state presence* (is the item there), not only metadata description.
- Nothing about the removal is replayable from the event log — by design. A projection
  rebuild followed by a sync yields the correct state.
- `JellyfinSync` exposes its in-progress flag; `LocalCopyRemoval.fs` is pure over injected
  effects, so the whole failure matrix is unit-tested with plain lambdas.
- A mount-root mismatch (`PathOutsideMountMap`) is a loud plan-time refusal, never a
  fallback to a Jellyfin-only delete that would orphan a seeding torrent.
- If a future feature wants "when did I drop this from the server" as a journal fact, that
  is a new decision, not an amendment — it would need a reason the fact is an engagement
  observation rather than a cache change.
