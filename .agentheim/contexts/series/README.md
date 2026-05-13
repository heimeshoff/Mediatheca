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
