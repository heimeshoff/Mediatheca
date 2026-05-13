# Journal

## Purpose
The **cross-media diary**. Aggregates *when* and *with whom* media was experienced — watch sessions (Movies), episode-watched events (Series), and play-time changes (Games) — into a unified activity timeline. Powers the heatmap, "Recently Watched/Played", and the cross-media stats blocks on the dashboard.

## Classification
**core** — The "diary" half of the product name (Mediatheca = library + diary). Without it the dashboard's intent-driven feel collapses to a catalog.

## Actors
Single user.

## Ubiquitous language

- **Activity** — any media-touching event from a core BC, projected into a single timeline.
- **Activity day** — one calendar day's aggregated activities, the unit of the heatmap.
- **Watch session** — sourced from Movies. Has date, friends.
- **Episode watched** — sourced from Series. Has date, friends (via rewatch session).
- **Play session** — sourced from Games (currently derived from `Game_play_time_set` / `Game_steam_last_played_set`). Open question whether this becomes a first-class event.
- **Watched-with / Played-with** — friend relationships projected from the source events into the journal's read model.
- **Recent activity** — a flat reverse-chronological list across all media types, with N items per type configurable.
- **Monthly breakdown** — activity rolled up by month for stats.

## Aggregates

Journal **has no write aggregates**. It is a projection-heavy read-side context: its data is derived from events published by Movies / Series / Games. All sessions are owned upstream; Journal only re-shapes them.

## Key events

None published. **Subscribes** to:
- Movies: `Watch_session_recorded`, `Watch_session_removed`, `Watch_session_date_changed`, friend-on-session events.
- Series: `Episode_watched`, `Episode_unwatched`, `Episode_watched_date_changed`, rewatch-session friend events.
- Games: `Game_play_time_set`, `Game_steam_last_played_set`, `Game_status_changed` (Playing/Completed transitions).

## Key commands

None. Journal is read-only.

## Relationships with other contexts

- **Downstream of:** Movies, Series, Games (conformist — Journal conforms to whatever the media BCs publish).
- **Upstream of:** Intelligence (Intelligence reads Journal's read models for stats).

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- Should Games emit a real `Play_session_recorded` event (modeling parity with Movies/Series) or stay derived? Decision lives here even though the event would originate in [[games]].
- Yearly journal recap (v2) — language and structure not yet seeded.
