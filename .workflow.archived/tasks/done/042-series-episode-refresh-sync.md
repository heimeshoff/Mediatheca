# Task 042: Series Episode Refresh Sync (Upcoming & New Episode Awareness)

**Status:** Todo
**Size:** Medium
**Created:** 2026-04-16
**Milestone:** --

## Description

Surface upcoming episodes, series return dates, and newly-released episodes for TV series. Today, Mediatheca only syncs series data once at the moment a series is added to the library — episode `air_date` fields are stored but never queried, and there is no mechanism to learn that a returning series now has new episodes or a return date.

This task introduces:

1. **A generalized nightly scheduled-service runner.** The existing daily background timer in `src/Server/PlaytimeTracker.fs:415` runs once per day for Steam playtime sync. Promote this into a system-wide scheduled job runner so additional nightly jobs (this series refresh, and future ones) can register into it without each building their own timer.

2. **A TMDB series refresh job** that runs nightly against all series with status `Returning Series` or `In Production`, updating series status, season/episode metadata (including renames, overview edits, poster/still swaps), and newly-announced upcoming episodes with their air dates.

3. **A manual refresh button** in the series detail page context menu (same menu as event log / delete series) that triggers the same refresh on demand for a single series.

4. **UI surfaces for upcoming episodes and return dates:**
   - **Series detail page:** "Next episode airs {date} (in X days)" line near the status badge.
   - **Series library cards:** Return/next-episode date indicator for returning series.
   - **TV series dashboard:** New list-style card showing the next ~5 returning series sorted by return date (e.g. "The Bear — returns Apr 28", "Severance — returns May 12").

Newly-released episodes do **not** need explicit notifications — they simply repopulate the existing "Next Up" sections (series detail page and dashboard) quietly, so the user sees them on their next visit.

## Acceptance Criteria

### Scheduler generalization
- [ ] A generic nightly scheduler abstraction exists in `src/Server/` that can register multiple jobs to run at a configured daily time
- [ ] Existing Steam playtime sync is migrated to use this scheduler (no behavior change, same 04:00-ish slot)
- [ ] Startup log shows all registered nightly jobs and the next scheduled run time

### Series refresh job
- [ ] Nightly job iterates all series with status `Returning Series` or `In Production`
- [ ] Per series, refetches from TMDB: series status, seasons, episodes (including unaired ones), air dates, overview edits, poster/still updates
- [ ] Status transitions persisted (e.g. `Returning Series` → `Ended` or `Canceled` when TMDB updates)
- [ ] New episodes added to the projection with their air dates (including future/unaired episodes)
- [ ] Episode metadata edits (renamed episodes, updated overviews/stills) reflected after refresh
- [ ] Refresh emits a domain event (e.g. `Series_refreshed`) capturing timestamp and a summary of what changed
- [ ] Rate-limit-safe: refresh is throttled so it doesn't burst-hit TMDB for large libraries

### Manual refresh
- [ ] Series detail page context menu (alongside event log / delete) has a "Refresh from TMDB" action
- [ ] Action triggers the same refresh logic for that single series and surfaces success/failure feedback
- [ ] UI updates immediately after refresh completes

### UI surfaces
- [ ] Series detail page shows "Next episode airs {YYYY-MM-DD} (in X days)" when a future-dated episode exists
- [ ] Series detail page shows "Returns {YYYY-MM-DD}" for returning series with a known next-season air date but no specific episode date yet
- [ ] Series library cards show return/next-episode date for returning series with known future dates
- [ ] TV series dashboard has a new list-style card, "Returning Soon", showing up to 5 returning series sorted ascending by next air date (closest first), with title + date per row
- [ ] Cards/labels gracefully handle the no-date case (returning series where TMDB has no announced air date yet — show nothing or a subtle "TBA")

### Quiet new-episode behavior
- [ ] When a new episode has aired since the last visit, the existing series detail "Next Up" and dashboard "Next Up" sections simply repopulate — no separate badge/notification mechanism

## Implementation Notes

- **Current scheduler:** `src/Server/PlaytimeTracker.fs:415` (`startBackgroundTimer`) runs a single daily callback. Registered from `src/Server/Program.fs:241`.
- **Current TMDB sync:** `src/Server/Api.fs:174-306` (`addSeriesToLibrary`) fetches full series + seasons + episodes on add; image downloads at lines 214-222. Much of this logic can be reused for refresh — extract shared fetch helpers.
- **Data already in place:** `series_episodes.air_date` and `series_seasons.air_date` exist in `src/Server/SeriesProjection.fs:56, 68` but are never queried. This task will query them.
- **TMDB status values:** `src/Server/Tmdb.fs:460` already maps the 5 `SeriesStatus` values; reuse.
- **Event model:** Add a `Series_refreshed` event under `src/Server/Series.fs` (existing events at lines 91-118). Payload should include timestamp, new-episode count, metadata-change summary, and status transition if any. This supports future "what changed on last refresh" UI.
- **Rate limiting:** TMDB allows 50 req/s. For a library of N returning series with S seasons each, plan on staggering or batching; a small `Task.Delay` between series is probably sufficient. Worth a sanity check against current library size during implementation.
- **Glassmorphism:** The new "Returning Soon" dashboard card and any new popovers must follow the project's glassmorphism rules (see `CLAUDE.md` → Conventions).
- **Time zone / "in X days":** Be explicit about which timezone the countdown is computed in — TMDB air dates are date-only (no TZ). Pick the user's local date for "in X days" math.

## Dependencies

None.

## Work Log

### 2026-04-16 19:00 -- Work Completed

**What was done:**
- Added a generic `ScheduledJobs` module (`src/Server/ScheduledJobs.fs`) that registers `JobSpec` records (name, hour-of-day UTC, async body) and fires each job 5 s after startup then once per day. Startup logs show every registered job with its next scheduled run time.
- Removed `PlaytimeTracker.startBackgroundTimer` and migrated the Steam playtime sync onto the new scheduler (same default 04:00 UTC slot, behavior unchanged).
- Added a `Series_refreshed` domain event on `Series.fs` with a `SeriesRefreshedData` payload capturing `RefreshedAt`, `NewEpisodeCount`, `PreviousStatus`, `NewStatus`. All fields are optional at deserialization for backward compatibility.
- New `SeriesRefresh` module (`src/Server/SeriesRefresh.fs`) encapsulates: fetching TMDB data (details + seasons + episodes + images), applying updates directly to the projection tables (`series_list`, `series_detail`, `series_seasons`, `series_episodes`), and appending the `Series_refreshed` event to the stream. Nightly job iterates candidates (status = `Returning` or `InProduction`) with a 500 ms throttle between series.
- Registered the series refresh job from `Program.fs` next to the Steam job (separate `series_refresh_hour` setting, defaults to 04:00 UTC).
- Added `refreshSeriesFromTmdb` to the `IMediathecaApi` contract and implemented it in `Api.fs` — drives the same refresh logic for a single series.
- `SeriesProjection` now computes `NextEpisodeAirDate` / `NextSeasonAirDate` (helpers `getNextEpisodeAirDate`, `getNextSeasonAirDate`, `getNextAirDate`) and surfaces them on `SeriesListItem.NextAirDate`, `SeriesDetail.NextEpisodeAirDate`, `SeriesDetail.NextSeasonAirDate`. Added `SeriesProjection.getReturningSoon` returning up to N returning/in-production series sorted by soonest air date.
- Updated `Api.getDashboardSeriesTab` to include the `ReturningSoon` list.
- Added `Series_refreshed` formatter to `EventFormatting.fs` so the event log renders it.
- Series detail page: added "Refresh from TMDB" action to the hero context menu; added "Next episode airs {date} (in X days)" / "Returns {date}" indicator near the status badge; plumbed IsRefreshing/RefreshMessage model fields + success alert.
- Series library cards: added a subtle "Returns {Mon D}" line under the progress text for returning/in-production series with a known future date.
- Dashboard TV Series tab: new glassmorphic "Returning Soon" list-style card placed between "Next Up" and "Recently Finished/Abandoned", showing up to 5 series with poster thumbnail, name, and countdown label.

**Acceptance criteria status:**
- [x] Generic nightly scheduler abstraction — `ScheduledJobs.JobSpec` + `startAll`
- [x] Steam playtime sync migrated — registered as a `JobSpec` from `Program.fs` (behavior unchanged)
- [x] Startup log shows all registered jobs + next scheduled run — verified in `ScheduledJobs.startTimer`
- [x] Nightly job iterates `Returning` + `InProduction` — `SeriesRefresh.getRefreshCandidates`
- [x] Per-series refetch includes status/seasons/episodes (incl. unaired)/air dates/overview/images — `SeriesRefresh.fetchFromTmdb`
- [x] Status transitions persisted — `applyToProjection` updates status, `Series_refreshed` event captures transition
- [x] New episodes added with air dates — `INSERT OR REPLACE` handles new + updated episodes
- [x] Edits reflected — same upsert path overwrites name/overview/still_ref/runtime
- [x] Refresh emits `Series_refreshed` — stream append, payload has timestamp + new-episode count + status transition
- [x] Rate-limit-safe — 500 ms `Async.Sleep` between series in `runNightlyJob`
- [x] Manual refresh context-menu action — "Refresh from TMDB" in `ActionMenu.heroView` on series detail
- [x] Triggers same refresh logic for single series — reuses `SeriesRefresh.refreshOne`
- [x] Success/failure feedback — `RefreshMessage` + `Error` shown as Daisy alerts
- [x] UI updates immediately — `Refresh_from_tmdb_result Ok` dispatches `Load_detail`
- [x] Series detail "Next episode airs {date} (in X days)" — added near status badge, episode-date preferred
- [x] Series detail "Returns {date}" fallback — rendered when only season-level date is known
- [x] Library cards show return/next date — `seriesCard` now shows "Returns Mon D" for returning series
- [x] Dashboard "Returning Soon" list card — up to 5, sorted ascending by air date, glass styling
- [x] Graceful no-date case — card hidden / line omitted when no date known
- [x] Quiet new-episode behavior — existing "Next Up" sections repopulate automatically (no separate badge)

**Build & test verification:**
- `dotnet build src/Server/Server.fsproj` — 0 warnings, 0 errors
- `npm run build` — Fable + Vite production client build succeeded
- `npm test` — 233 / 233 Expecto tests pass

**Files changed:**
- `src/Server/ScheduledJobs.fs` — new generic daily-job scheduler
- `src/Server/SeriesRefresh.fs` — new TMDB refresh module (fetch + projection apply + event append + nightly loop)
- `src/Server/Series.fs` — `SeriesRefreshedData`, `Series_refreshed` event, `Refresh_series_from_tmdb` command, serialization, evolve/decide updates
- `src/Server/SeriesProjection.fs` — `Series_refreshed` no-op handler, `getNextEpisodeAirDate`/`getNextSeasonAirDate`/`getNextAirDate` helpers, `NextAirDate` on list items, `NextEpisodeAirDate`/`NextSeasonAirDate` on detail, `getReturningSoon` query
- `src/Server/PlaytimeTracker.fs` — removed `startBackgroundTimer` (logic migrated to `Program.fs` via `ScheduledJobs`)
- `src/Server/Program.fs` — registers Steam + series refresh jobs via `ScheduledJobs.startAll`
- `src/Server/Api.fs` — `refreshSeriesFromTmdb` endpoint + `ReturningSoon` in `DashboardSeriesTab`
- `src/Server/EventFormatting.fs` — formatter for `Series_refreshed`
- `src/Server/Server.fsproj` — registered `ScheduledJobs.fs` and `SeriesRefresh.fs`
- `src/Shared/Shared.fs` — `NextAirDate` on `SeriesListItem`, `NextEpisodeAirDate`/`NextSeasonAirDate` on `SeriesDetail`, new `ReturningSoonItem`, `ReturningSoon` on `DashboardSeriesTab`, `refreshSeriesFromTmdb` on `IMediathecaApi`
- `src/Client/Pages/SeriesDetail/Types.fs` — `Refresh_from_tmdb` Msg + `IsRefreshing`/`RefreshMessage` Model fields
- `src/Client/Pages/SeriesDetail/State.fs` — init + update handlers for manual refresh
- `src/Client/Pages/SeriesDetail/Views.fs` — "Refresh from TMDB" in ActionMenu, next-air-date indicator near status badge, success alert, date/countdown helpers
- `src/Client/Pages/Series/Views.fs` — "Returns {date}" line on library cards for returning series with known future date
- `src/Client/Pages/Dashboard/Views.fs` — `returningSoonCard` + `formatReturningDate` + `returningCountdown` helpers, inserted between Next Up and Recently Finished/Abandoned in `seriesTabView`
