---
id: 0043
title: Event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description
scope: global
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
amends: [0012]
related_tasks: [infrastructure-e4kwm]
related_research: [tv-series-metadata-fallback-sources]
---

# ADR 0043: Event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description

## Context

The projection drift detector (ADR-0031) reported 2437 discrepancies, all in `SeriesProjection` —
zero in every other bounded context's tables. Root cause: `SeriesRefresh.applyToProjection` writes
TMDB-fetched episode/season metadata directly into projection tables, while the event that
accompanies the refresh (`Series_refreshed`) is a no-op summary carrying only counts. A projection
rebuild replays the event log and gets none of that metadata back — 780 refreshes' and 23
Jellyfin-materialized episodes' worth of state exists **only** in the live tables and evaporates on
rebuild, silently, until someone runs the drift check or rebuilds and watches the library go blank.

The same category recurs, and not only in the direction "event too thin, projection carries the
real state":

- **Games** carries 7668 `Game_play_mode_added` events tagging play modes with Steam Store category
  strings — 43% of the entire 17,638-event log. The category tag is Steam's own description of the
  game, re-fetchable from the Steam Store API at any time; it rides inside a domain event anyway.
- **Movies** is deterministic today only because nothing yet refreshes movie metadata after import —
  the same shape of defect is latent, waiting for the first movie-refresh feature.
- The **mirror-image** defect also exists: `game_play_session` was a plain imperative table (42 rows
  of unrebuildable user history, no backing event), and `Game_play_time_set` carried a republished
  `SUM` that is provably non-monotonic under correction (three games' totals shown to *decrease*
  across observations — Grounded 2952→2282, Windrose 975→375, Starcom 979→811). Here the defect runs
  the other way: genuine user history — the user's own play sessions — was being stored as a mutable
  cache instead of an event, so it couldn't be replayed, corrected, or audited.

Fixing `SeriesRefresh` alone, without writing down *why* it was wrong, guarantees recurrence: the
next person to build movie-refresh will rebuild the identical anti-pattern, because nothing in
Administration's or Integration's ubiquitous language tells them not to. The rule this ADR states
constrains Movies, Series, and Games' event vocabulary directly, and has a positive half (play
sessions **are** domain history, not a cache) that binds Games. Integration and Administration are
both `generic` bounded contexts (see `.agentheim/context-map.md`); a generic BC cannot be the
authority that constrains three `core` BCs' event design. `infrastructure` is the context map's
declared "standing home for tech decisions that apply to the system as a whole" — this doctrine
belongs here.

This ADR is a refinement of ADR-0002 (event sourcing + CQRS is the persistence model), not a
challenge to it: ADR-0002 established that reads are projections rebuilt from an append-only log of
"every domain change." This ADR sharpens what counts as a domain change worth recording as an event,
versus what is better modeled as a projection-level cache of externally-sourced description.

## Decision

### The test

> An event records an **observation of the user's own engagement** with a work. A cache records **a
> third party's description** of the work.
>
> Operative form — **re-derivability**: if the fact can be re-fetched from its source at any time
> without loss, it is cache. If the system observed something at a moment that can never be observed
> again, it is an event.

### The second clause (identity card)

> An externally-sourced field may remain a projection column **only if** it is written exclusively by
> an event that carries it, and never by a refresh path.

This is what keeps `name`, `year`, `poster_ref`/`cover_ref`, and `genres` as legitimate projection
columns despite being externally sourced: they ride in every `*_added_to_library` snapshot event, so
replaying the log reproduces them deterministically with no out-of-band write involved. It is what
makes ADR-0038's wipe-first-import recovery yield a browsable library instead of 106 nameless rows,
and keeps `ORDER BY name` non-degenerate after a rebuild. The identity-card clause is what
`SeriesRefresh.applyToProjection` violates and a `Movie_added_to_library` snapshot does not: the
difference is not "is the field externally sourced" but "does a refresh path write it outside the
event that carries it."

### Three boundary calls the naive "external = cache" rule gets wrong

The re-derivability test is not "does the data originate outside the app" — Steam, TMDB, and RAWG
are all outside the app, yet not everything they touch is cache:

- A per-day playtime **delta is an event** even though Steam is the source: Steam exposes only
  cumulative totals, so the delta between two observations is observed once and can never be
  observed again — a later query against Steam cannot recover "what changed between yesterday and
  today," only "what the total is now."
- `Game_steam_app_id_set` **stays an event** even though Steam assigns the value: the *link* between
  a Mediatheca game and a Steam appId is our own decision, not Steam's fact —
  `PlaytimeTracker.findByName` (`src/Server/PlaytimeTracker.fs:636`) links by fuzzy name match, so a
  wrong link is a correctable mistake that must be auditable, the same reason any user decision is an
  event.
- `Game_family_owner_added` **stays an event**: it is dual-sourced (`Api.fs:442-470` Steam family
  import, `Api.fs:3110-3122` explicit UI action) and the value it stores is a **Friends-BC slug**
  reached through a user-maintained `steamIdToFriendSlug` map that Steam has no knowledge of. Steam
  alone cannot reconstruct this fact by being re-queried; it depends on state Steam never had.

### Classification table

For every event type and field named in the evidence above (and in the workstream's supporting
research):

| Item | Classification | Why |
|---|---|---|
| `name`, `year`, `poster_ref`/`cover_ref`, `genres` on Movie/Series/Game | Cache — projection column, event-carried | Rides in the `*_added_to_library` snapshot event; replay reproduces it deterministically. Passes the identity-card clause. |
| TMDB series/episode metadata (title, overview, air date) written by `SeriesRefresh.applyToProjection` | Cache — projection column | Re-fetchable from TMDB at any time; the defect was writing it outside any event, not the fact of it being cache. |
| Jellyfin-materialized season/episode rows (`source='jellyfin'`) | Cache — projection column, provenance-tagged | Re-derivable from the Jellyfin server at any time (ADR-0012); TMDB's later `INSERT OR REPLACE` resets provenance without a second code path. |
| `rawg_rating`, `hltb_hours`, `tmdb_rating` on movies/series/episodes | Cache — projection column | Third-party ratings/estimates, re-fetchable from RAWG/HowLongToBeat/TMDB at any time. |
| Artwork refs (posters/covers/stills) sourced from TMDB/RAWG/Jellyfin | Cache — projection column | Re-fetchable image references; ADR-0025/ADR-0039 already treat the underlying files as a reclaimable cache. |
| `Series_refreshed` | Event — narrowed to real airing-status transitions | The transition itself (e.g. Returning → Ended) is an observation of a moment that cannot be re-derived once superseded by the next transition; the episode/season *metadata* that used to ride alongside it is cache and moves off the event (builder decision, this workstream: 566/780 historical events carry null statuses; the real 214 transitions plus `Series_added_to_library.status` reproduce live status for 103/105 series). |
| `Game_play_mode_added`'s Steam Store category tag payload | Cache-shaped data currently riding on an event — narrow the event, keep the tag as a projection column refreshed from Steam | The tag is Steam's own description of the game, re-fetchable at any time; carrying it on 7668 events (43% of the log) is exactly the anti-pattern this doctrine names. The play-mode designation itself, if it reflects a user decision, stays an event. |
| `game_play_session` rows (user's own play history) | Event — genuine domain history, was wrongly imperative | The user's play sessions are an observation of their own engagement, observed once, never re-derivable from Steam (which only ever reports a cumulative total). This is the mirror-image defect: domain history stored as a mutable cache instead of an event. |
| `Game_play_time_set`'s republished cumulative `SUM` | Retired — non-monotonic under correction, not re-derivable as a fact | Provably decreases across observations (Grounded 2952→2282, Windrose 975→375, Starcom 979→811); a stored running total is neither a faithful cache (Steam's own total, unmodified) nor a faithful event (a single observed delta) — it conflates both. Superseded by folding two facts from the log: what the user asserts as total, and what Steam has ever reported (see per-day delta and `Prior_play_time_recorded`, below). |
| Per-day playtime delta observed from Steam | Event | Steam exposes only a cumulative total; the delta between two polls is observed once and never again — passes the re-derivability test directly. |
| `Game_steam_app_id_set` | Event | The link is our decision (fuzzy name match), not Steam's fact; must be auditable and correctable. |
| `Game_family_owner_added` | Event | Dual-sourced, and the stored value (a Friends-BC slug) depends on user-maintained state Steam never had. |
| `Prior_play_time_recorded` (pre-tracking playtime, dateless) | Event | Records the user's own assertion of playtime accumulated before tracking began; not derivable from any external source at all. |

## Consequences

### Positive
- Gives the drift detector (ADR-0031) a name for what "zero drift" means structurally, not just
  operationally: drift reaches zero by *removing* out-of-band-written columns from events'
  responsibility (or narrowing the writing event to legitimately carry them), never by adding an
  ignore-list to `diffTable`.
- Constrains all future refresh-shaped features (movie refresh, RAWG refresh, HLTB refresh) to the
  same shape from day one, instead of each BC re-deriving the lesson independently.
- Gives Games' play-history correction (this workstream's sibling tasks) a doctrinal basis: play
  sessions move from an imperative table to genuine domain events, and the non-monotonic `SUM` is
  retired in favor of two folds derived from the log.

### Negative / accepted tradeoff
- Two more concepts to hold in mind when designing any new event (re-derivability, identity-card).
  Mitigated by this ADR being global and short enough to reference directly rather than re-derive.
- Does not by itself fix any of the named defects — `SeriesRefresh`, `Game_play_mode_added`, and
  `game_play_session`/`Game_play_time_set` each need their own follow-up implementation task in the
  owning BC; this ADR is the doctrine those tasks are built against.

## Relationship to ADR-0012

This ADR **amends ADR-0012 in place** (ADR-0012 stays `accepted`, not superseded). Every substantive
decision in ADR-0012 survives unchanged: TMDB authoritative, provenance as a projection column,
`INSERT OR IGNORE` vs. `INSERT OR REPLACE`-resets-`source`, the mandatory synthetic season container,
the pure injected-effect core, and client provider-blindness. Two passages are retracted because they
stated the *old* justification this ADR replaces:

1. The Decision section's justification clause — *"justified because metadata is already a
   rebuildable read-model cache, not aggregate state"* — retracted because the doctrine now states
   the actual test (re-derivability + identity-card), not an appeal to "it's just a cache."
2. The Consequences section's claim that *"a full projection rebuild drops them and the next sync
   re-creates them"* — retracted because this was not a benign consequence but a precise description
   of the defect this doctrine exists to prevent: a projection rebuild silently losing metadata that
   should either be re-fetchable on demand (true cache, fine to lose) or carried by an event (never
   lost). ADR-0012's materialized rows *are* true cache under the re-derivability test — the passage
   was retracted for stating the old, unexamined reasoning, not because the underlying decision was
   wrong.

ADR-0039 (Jellyfin still storage path) and ADR-0040 (still backfill lives in `materializeMissingEpisodes`)
coexist unchanged: both are tier-agnostic (image-file naming, backfill predicate) and this doctrine
does not require touching either.

## Alternatives considered

- **Fix `SeriesRefresh` without writing an ADR** — rejected: the drift detector's own report shows
  the pattern is already latent in Movies and pervasive in Games; fixing one instance without a
  named rule guarantees the next BC re-derives the anti-pattern from scratch.
- **State the rule as "external data is always cache"** — rejected: the three boundary calls
  (playtime delta, `Game_steam_app_id_set`, `Game_family_owner_added`) show this is wrong. The
  correct axis is re-derivability, not origin.
- **Mark ADR-0012 as superseded and write a replacement ADR** — rejected: every substantive
  decision in ADR-0012 survives; superseding it would misrepresent the correction as a reversal
  rather than the retraction of two justification passages.

## References

- `.agentheim/knowledge/decisions/0002-event-sourcing-cqrs.md` — the persistence baseline this ADR
  refines.
- `.agentheim/knowledge/decisions/0012-jellyfin-materializes-missing-seasons-as-projection-supplement.md` —
  amended by this ADR; see "Relationship to ADR-0012" above.
- `.agentheim/knowledge/decisions/0031-projection-drift-detector-throwaway-shadow-connection.md` —
  the drift detector whose 2437-discrepancy report is this ADR's founding evidence.
- `.agentheim/knowledge/decisions/0039-jellyfin-still-distinct-storage-path-accepted-orphan.md`,
  `.agentheim/knowledge/decisions/0040-jellyfin-still-backfill-lives-in-materialize-no-refetch-guard.md` —
  coexist unchanged.
- `src/Server/SeriesRefresh.fs` (`applyToProjection`), `src/Server/PlaytimeTracker.fs:636`
  (`findByName`), `src/Server/Api.fs:442-470` and `:3110-3122` (`Game_family_owner_added` dual
  sourcing) — the code sites the boundary calls reason about.
- `.agentheim/contexts/administration/done/administration-btvqa-projection-drift-integrity-checks.md`,
  `.agentheim/contexts/integration/done/integration-m4k7p-materialize-missing-season-from-jellyfin.md` —
  prior art this doctrine synthesizes.
