# Series

## Purpose
Owns the **Series aggregate** — TV shows with seasons, episodes, rewatch sessions, and episode-level watch state. Source of truth for "what's the next episode", "who watched this with me", "have I finished this run".

## Classification
**core** — One of the three media-type BCs.

## Actors
Single user.

## Ubiquitous language

- **Series** — a TV show in the library. Has seasons and episodes; carries a TMDB id.
- **Season** — a grouping of episodes within a series.
- **Episode** — the unit of watch state. Marked watched / unwatched individually, by season, or up to a point.
- **Rewatch session** — a *named* watching pass through the series (default or named, e.g. "with Alice"). Episode-watched events are scoped to a rewatch session.
- **Default rewatch session** — the rewatch a Series defaults to when episode-marking commands omit an explicit one.
- **In Focus** — "I want to watch this soon"; pinned to dashboard. Auto-clears on first episode watched.
- **Next Up** — derived: the next unwatched episode in the current default rewatch session, surfaced on the dashboard.
- **Status** — Active / Finished / Abandoned, derived from completion + recent activity. (See `SeriesStatus` in Shared.)
- **Recommendation / want to watch with** — same meanings as Movies.

## Aggregates

- **Series** — protects: episode/season events require the series to exist; rewatch-session-scoped events require a valid rewatch id; default rewatch must reference an existing rewatch session.

## Key events

`Series_added_to_library`, `Series_categorized`, `Series_poster_replaced`, `Series_backdrop_replaced`, `Series_recommended_by`, `Series_recommendation_removed`, `Series_want_to_watch_with`, `Series_removed_want_to_watch_with`, `Series_personal_rating_set`, `Rewatch_session_created`, `Rewatch_session_removed`, `Default_rewatch_session_changed`, `Rewatch_session_friend_added`, `Rewatch_session_friend_removed`, `Episode_watched`, `Episode_unwatched`, `Season_marked_watched`, `Episodes_watched_up_to`, `Season_marked_unwatched`, `Episode_watched_date_changed`, `Series_refreshed`, plus the In Focus events (per vision).

`Series_refreshed` is narrowed to fire only when a nightly/manual TMDB refresh finds a **real airing-status transition** (ADR-0047) — a no-change refresh appends nothing. Everything else TMDB re-fetches (name, overview, poster, genres, rating, episode/season data) is third-party metadata, not an event (ADR-0043's event-worthiness doctrine): it lives only in the `series_season_cache`/`series_episode_cache` cache tier, seeded/cleaned up imperatively at command time (`Series_added_to_library`/`Series_removed_from_library`'s command handlers in `Api.fs`), never written from projection replay. `Status` is the one TMDB-sourced field that stays a `series_list`/`series_detail` projection column, because the narrowed event carries every transition into the log — it is fully replayable, unlike the rest.

**Reads compose the cache and views at query time, never at the API layer** (ADR-0048): `SeriesProjection.getAll`/`getBySlug`/`getRecentSeries`/`getRecentlyFinished`/`getDashboardSeriesNextUp` `LEFT JOIN` `series_metadata_cache` (TmdbRating/Overview/EpisodeRuntime) and the `series_next_up`/`series_episode_counts` views (NextUp/SeasonCount/EpisodeCount) directly inside their own query functions — every Shared DTO keeps its existing shape, and the internal `Api.fs` callers that read fields off `getBySlug`'s DTO to drive their own logic see the same fully-populated shape they always did. A cache miss degrades gracefully (`None`/`""`/empty seasons), never a synchronous TMDB fetch on the read path. `recalculateProgress` now only maintains `watched_episode_count`; `next_up_*` lives solely in the view. Two known gaps from this cutover are backlogged, not yet fixed: `series_metadata_cache` has no write path going forward (only a one-time seed — `series-t3jkv`), and `getRecentlyAbandoned` was not retargeted like its sibling `getRecentlyFinished` (`series-x9mfp`).

## Key commands

`Add_series_to_library`, `Categorize_series`, `Replace_series_poster`, `Replace_series_backdrop`, `Recommend_series`, `Remove_series_recommendation`, `Want_to_watch_series_with`, `Remove_want_to_watch_series_with`, `Set_series_personal_rating`, `Create_rewatch_session`, `Remove_rewatch_session`, `Set_default_rewatch_session`, `Add_friend_to_rewatch_session`, `Remove_friend_from_rewatch_session`, `Mark_episode_watched`, `Mark_episode_unwatched`, `Mark_season_watched`, `Mark_episodes_watched_up_to`, `Mark_season_unwatched`, `Change_episode_watched_date`, `Refresh_series_from_tmdb`.

## Relationships with other contexts

- **Upstream of:** Journal (publishes `Episode_watched` etc.), Intelligence.
- **Downstream of:** Friends.
- **Downstream of:** Integration via anticorruption (TMDB adapter, Jellyfin sync).
- **Consumed by:** Curation.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- In Focus events for Series (vision-promised, M1) not yet event-coded.
- Returning Soon (`ReturningSoonItem` in Shared) is partially modeled — language may need refinement.
