# Mediatheca — Vision

> Your personal media diary and intelligence hub.

## Problem

Losing track of what you've watched, played, and read across multiple platforms. No unified way to record who you experienced media with, when, and what comes next. Wanting a single landing page that answers: "What should I watch/play right now?"

## Target User

Single user (self-hosted). Someone who actively tracks movies, TV series, games and books, watches with friends, and wants an opinionated dashboard that surfaces what matters — not a catalog to browse, but an intent-driven view of what's next.

## Core Concept: In Focus

A cross-cutting concept that lets the user signal intent — "I want to engage with this soon." In Focus bridges the gap between "in my library" and "actively consuming."

### Movies
- Toggle flag on any movie (watched or unwatched)
- Auto-clears when a watch session is recorded
- In Focus movies appear on the main dashboard

### TV Series
- Toggle flag on any series
- Auto-clears when the first episode is watched
- In Focus series are pinned to the top of the dashboard TV section
- Once cleared, the series stays visible via Next Up logic (sorted by most recent watch activity)
- Sorting: In Focus first, then by recency of last episode watched

### Games
- A status in the lifecycle: Backlog → **InFocus** → Retired / Abandoned (plus Dismissed for never-playing, kept for the record)
- InFocus means "I want to play this soon, I want to recommend it, or I'm actively playing it right now" — there is no separate Playing status
- Any recognized play session automatically pulls a game (from any status) into InFocus
- InFocus games appear on the main dashboard

### Books (recognized 2026-09-16)
- A status in the lifecycle, the Games shape: Backlog → **InFocus** → Finished / Abandoned
- InFocus means "reading or listening to this now, or next"
- Any reading-progress observation that moves the percent forward pulls a book (from any unfinished status) into InFocus; reaching 100 % (or the source saying "finished") moves it to Finished
- InFocus books appear on the main dashboard with their progress bar and the source of the progress (Audible, or entered by hand)

## Unified Dashboard

The dashboard is the landing page — a tabbed view across all media types.

### Tabs: All | Movies | TV Series | Games | Books

### All Tab (default landing page)

The curated overview. Answers "what's next?" at a glance.

**TV Series: Next Up**
- In Focus series pinned to top
- Active series with next unwatched episode, sorted by most recent watch activity
- Shows watch-with friends if there's a shared rewatch session
- Recently finished or abandoned series also appear
- ~5-6 items

**Movies: In Focus**
- Movies flagged In Focus, newest addition first
- Removed from this section once watched (In Focus auto-clears)
- ~5-6 items

**Games: In Focus**
- All games with InFocus status
- The "up next" queue for games

**Games: Recently Played**
- Sorted by most recent play session
- ~5-6 items

**Books: Reading**
- InFocus books, most recently progressed first, each with its progress percent and source
- Finished books linger for 7 days marked Finished, then leave
- ~5-6 items

### Movies Tab
- Recently added movies (newest first, filtered out once watched)
- Stats and details: total watch time, recent watch sessions
- Expandable over time with more intelligence

### TV Series Tab
- Full next-up list across all series
- Episode progress, recently finished/abandoned
- Stats: episodes watched, watch time
- Expandable over time

### Games Tab
- Recently added games (newest first)
- Recently played games
- Completion progress vs HowLongToBeat averages
- Play time stats
- Expandable over time

### Books Tab
- Currently reading (with progress), recently finished, recently added
- Reading stats: finished this year, pages read / hours listened
- Expandable over time

## Remaining v1 Work

### In Focus (cross-cutting)
- Add In Focus toggle flag to Movies (new event: Movie_in_focus_set / Movie_in_focus_cleared, auto-clear on watch session)
- Add In Focus toggle flag to TV Series (new event: Series_in_focus_set / Series_in_focus_cleared, auto-clear on episode watched)
- Add InFocus status to Game lifecycle (Backlog → InFocus → Retired / Abandoned / Dismissed — remodeled 2026-08-01: OnHold removed, Completed renamed Retired, no Playing status)

### Unified Dashboard (replaces REQ-207)
- Rework existing dashboard into tabbed layout (All / Movies / TV Series / Games)
- All tab with sections as described above
- Individual tabs with media-specific lists and stats
- Individual tabs will grow over time with more stats and intelligence

### Steam Import Enhancement (updates REQ-208)
- Steam library import is already functional
- Add: during import, detect existing games with missing descriptions and backfill from Steam Store API

### HowLongToBeat Integration (REQ-209)
- Fetch average completion times by game name
- Display comparison on game detail page (your play time vs average)
- Show on Games dashboard tab

### Books — Audible + Open Library (recognized 2026-09-16, pulled forward from v2; Goodreads dropped 2026-09-18)
Books join movies, series and games as the fourth media type, served the way TMDB serves movies and Steam serves games: configured in Settings, searchable in the search modal, a detail page, and progress that comes from the outside.
- **Sources:** Audible (audiobooks — catalog search needs no credential; the library and listening progress come through an auth file the user generates on their own machine with `audible-cli`, never a login performed by Mediatheca — ADR-0074). Open Library is the key-less search/metadata source for print books (ADR-0075). Goodreads was shipped on 2026-09-16 as a third source (public RSS shelf/status feeds) and **dropped on 2026-09-18** without ever being used — Audible and Open Library are the only book sources; see integration-sfmxg.
- **Progress:** a reading-progress observation (percent, source, date) is an event, like a Steam play session; length and description are cache (ADR-0076). Manual progress entry on the detail page is the always-available fallback.
- **Surfaces:** Settings card (Audible), search-modal Books tab (Open Library + Audible sources), `/books/{slug}` detail page, dashboard Books tab and All-tab Reading rail, scheduled progress sync job.
- **Later:** reading activity in the Journal, books in catalogs.

### Operability & Observability — Admin Console (recognized 2026-07-21)
The event-sourced store (ADR-0002) is only as trustworthy as it is inspectable. The `/admin` console gives the single user, **in an operator role**, the tools to see into and maintain the substrate the rest of the app is built on. This workstream was previously unlisted — the work skill's vision-conformance pass surfaced that a whole arc of admin-console work served no named roadmap item; it is absorbed here as recognized v1 work at the builder's direction.
- **Shipped:** tabbed `/admin` shell + its own `IAdminApi` contract (ADR-0017); event explorer with FTS5 payload search, composable filters, and keyset pagination (ADR-0020), plus a live-tail Follow mode (ADR-0023); Health tab — event volume, per-BC breakdown, storage sizes (ADR-0021); per-stream drill-in — formatted+raw history, projection state, cross-links (ADR-0022); projection dashboard — checkpoint/lag overview and rebuild-by-command with streamed progress (ADR-0024).
- **Backlog (recognized, not yet scheduled):** projection-drift integrity checks, compensating-event composer, event surgery (guarded raw edit/delete/rename), NDJSON event-log export/import, scheduled-job runs console, image-cache admin.
- **Boundary:** operator tooling stays proportionate to a single-user, single-operator app. It makes the event substrate inspectable and repairable — it is **not** an end-user product surface (that is the Unified Dashboard). When admin-console scope competes with the media-experience roadmap (In Focus, Unified Dashboard, Steam Import, HLTB), the media experience wins.

## Out of Scope (v1)

- ~~Books (v2)~~ — pulled into v1 on 2026-09-16, see "Books — Audible + Open Library" above
- Goodreads integration — shipped 2026-09-16, removed 2026-09-18 (integration-sfmxg); not coming back
- Trakt.tv / Jellyfin sync (v2)
- Yearly intelligence reports (v2)
- Friend-level intelligence (v2)
- Trailer playback (v2)

## Design Principles

- **Intent-driven**: The dashboard shows what you want to do next, not everything you own
- **Auto-clearing**: In Focus state manages itself — watch something and it moves along
- **Mobile-first**: Dashboard sections work as a vertical scroll on mobile
- **Unified, not siloed**: One dashboard with tabs, not separate dashboards per media type
- **Replayable**: rebuilding projections from the event log always yields the same result. Third-party metadata is cached, not evented. (ADR-0043)
