# Journal

## Purpose
The **cross-media diary**. Aggregates *when* and *with whom* media was experienced — watch sessions (Movies), episode-watched events (Series), play-time changes (Games), and reading-progress observations (Books) — into a unified activity timeline. Powers "Recently Watched/Played" and the cross-media stats blocks on the dashboard today; the heatmap is a planned Journal surface (vision: "Later"), not yet live — see `intelligence-h4qk2`.

## Classification
**core** — The "diary" half of the product name (Mediatheca = library + diary). Without it the dashboard's intent-driven feel collapses to a catalog.

## Actors
Single user.

## Ubiquitous language

- **Activity** — any media-touching event from a core BC, projected into a single timeline.
- **Activity day** — one calendar day's aggregated activities, the unit of the heatmap.
- **Watch session** — sourced from Movies. Has date, friends.
- **Episode watched** — sourced from Series. Has date, friends (via rewatch session).
- **Play session** — sourced from Games as a first-class event (`Play_session_recorded`, ADR-0050), keyed on the gaming day it happened on, carrying a source (`SteamSync | Manual`). No friends on a session — "played with" is a Game-level relationship (`Game_played_with`), not a session-level one.
- **Watched-with / Played-with** — friend relationships projected from the source events into the journal's read model.
- **Recent activity** — a flat reverse-chronological list across all media types, with N items per type configurable.
- **Monthly breakdown** — activity rolled up by month for stats.
- **Reading day** — sourced from Books. One *book* on one *calendar day* that at least one `Reading_progress_observed` touched (distinct from **Activity day**, which is one day across all media). One per book per day, never one per observation: ADR-0076 already collapses same-day-same-source observations, and the residual multiplicity — two sources (e.g. Audible and a manual entry) both observing the same book that day — is integration topology, not diary fact. Unlike Play session it is **not** an upstream event: Journal derives it by grouping `book_progress` rows with `kind = 'observation'` on `(book, day)` — a `kind = 'prior'` row (ADR-0082's `Prior_reading_progress_recorded`, where the reader already was, not something read that day) never manufactures a reading day — so it survives as long as one observation-kind row for that book-day remains — removing the last observation for a book-day retracts the day; removing one of several does not. Carries **no source** (a day can be backed by several sources; "which source" is answered by the underlying `book_progress` rows, not by the day) and **no friends** — reading is solitary here, and `Book_recommended_by` is library-level provenance, not an activity relationship.
- **Book finished** — sourced from Books as the `Book_status_changed` transition to `Finished`; there is no separate finish event. The diary's punctuation mark for a book, the way a Watch session is for a movie — unlike Games' `Retired` (shelving, not an activity), this is diary-worthy. Dated by the effective-on date the event carries (ADR-0077), which may be **historical** when a manual back-dated finish (or an Audible `is_finished` import) backfills an already-read book — a future Recent-activity view must sort on that date, not on append order.

## Aggregates

Journal **has no write aggregates**. It is a projection-heavy read-side context: its data is derived from events published by Movies / Series / Games / Books. All sessions are owned upstream; Journal only re-shapes them.

Journal has no read-model file of its own either — there is no dedicated projection module for it. Its read models are owned by the source BCs' own projections: `GameProjection` queries over `game_play_session` for game-derived stats, `PlaytimeTracker` (`getDashboardPlaySessions`, `getPlaytimeSummary`) delegating to the checkpoint-tracked `PlaySessionProjection`, plus the equivalent Movies/Series projections for watch sessions and episodes watched. Journal reads these tables directly; it never re-derives play time from an event.

## Key events

None published. **Subscribes** to:
- Movies: `Watch_session_recorded`, `Watch_session_removed`, `Watch_session_date_changed`, friend-on-session events.
- Series: `Episode_watched`, `Episode_unwatched`, `Episode_watched_date_changed`, rewatch-session friend events.
- Games: `Play_session_recorded`, `Play_session_minutes_corrected`, `Play_session_moved`, `Play_session_removed`, `Game_status_changed` (status transitions, including the Retired terminal state).
- Books: `Reading_progress_observed`, `Reading_progress_observation_removed`, `Book_status_changed` (the `Finished` transition only; other transitions are library state, not diary).

## Key commands

None. Journal is read-only.

## Relationships with other contexts

- **Downstream of:** Movies, Series, Games, Books (conformist — Journal conforms to whatever the media BCs publish).
- **Upstream of:** Intelligence (Intelligence reads Journal's read models for stats).

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- Yearly journal recap (v2) — language and structure not yet seeded.
- A live reading-activity surface (heatmap, monthly rollup, or a Recent-activity list) has no `IMediathecaApi` method today — the All tab's former `ActivityDays`/`MonthlyBreakdown` payload was pruned (`intelligence-h4qk2`), and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer (found during `journal-k52j1`'s refinement, 2026-09-16). When one is built, it needs its own API method sized to that view — never a field bolted onto the All-tab landing payload — and should decide whether **Reading day** (high-frequency) or **Book finished** (low-frequency, narratively significant), or both in different views, belongs in a reverse-chronological list versus a heatmap / monthly rollup. `getDailyReadingActivity`'s deleted shape (`COUNT(DISTINCT book_slug)` grouped by `observed_on` over `book_progress`, see `git show ca464a1:src/Server/BookProjection.fs`) is exactly what Reading day encodes.
- No source BC's `*_removed_from_library` event is in the subscribed list, so deleting a movie, series, game or book may leave phantom activity in any future diary view. Not fixed here; for whoever builds the first live activity surface.
