# Mediatheca — Vision

> Your personal media diary and intelligence hub.

## Problem

Losing track of what you've watched, played, and read across multiple platforms. No unified way to record who you experienced media with, when, and what comes next. Wanting a single landing page that answers: "What should I watch/play right now?"

## Target User

Single user (self-hosted). Someone who actively tracks movies, TV series, and games, watches with friends, and wants an opinionated dashboard that surfaces what matters — not a catalog to browse, but an intent-driven view of what's next.

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
- A status in the lifecycle: Backlog → **InFocus** → Playing → Completed / Abandoned / OnHold
- InFocus means "I want to play this, I'm already thinking about it"
- InFocus games appear on the main dashboard

## Unified Dashboard

The dashboard is the landing page — a tabbed view across all media types.

### Tabs: All | Movies | TV Series | Games

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

## Remaining v1 Work

### In Focus (cross-cutting)
- Add In Focus toggle flag to Movies (new event: Movie_in_focus_set / Movie_in_focus_cleared, auto-clear on watch session)
- Add In Focus toggle flag to TV Series (new event: Series_in_focus_set / Series_in_focus_cleared, auto-clear on episode watched)
- Add InFocus status to Game lifecycle (Backlog → InFocus → Playing → Completed / Abandoned / OnHold)

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

## Out of Scope (v1)

- Books (v2)
- Trakt.tv / Jellyfin sync (v2)
- Yearly intelligence reports (v2)
- Friend-level intelligence (v2)
- Trailer playback (v2)

## Design Principles

- **Intent-driven**: The dashboard shows what you want to do next, not everything you own
- **Auto-clearing**: In Focus state manages itself — watch something and it moves along
- **Mobile-first**: Dashboard sections work as a vertical scroll on mobile
- **Unified, not siloed**: One dashboard with tabs, not separate dashboards per media type
