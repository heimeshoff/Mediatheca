---
id: 0075
title: Goodreads is read through its public, key-free RSS feeds keyed by the user's Goodreads user id (shelf feed for membership and ratings, user-status feed for reading progress) — no API key exists any more, no session cookie is ever pasted; Open Library is the book search/metadata source and Audnexus the audiobook metadata fallback
scope: integration
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
amends: []
amended_by: [0083]
related_tasks: [integration-wmqn3, integration-y2ak4, integration-c8d4x]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
---

# ADR 0075: Goodreads via public feeds by user id; Open Library as the book metadata source

> **Amended by ADR-0083 (2026-09-18, integration-sfmxg):** §§1–4 — the Goodreads shelf/progress sync — are retired; the Goodreads integration was removed outright (a clean delete: no persisted event, setting or id ever referenced Goodreads). §5 — Open Library as the book search/metadata source and Audnexus as the audiobook metadata fallback — stays in force, unchanged.

## Context

The builder asked for Goodreads to serve books "the same way TMDB serves movies", including a
configurable key in Settings, search in the search modal, and progress "based on the Goodreads
percentage". Research (`goodreads-reading-progress-and-book-metadata-sources-2026-09-16`, corrected
through the review gate) established:

- **The Goodreads developer API is dead.** No new keys since 2020-12-08, the old endpoints retired,
  no successor from Goodreads or Amazon. There is no "Goodreads API key" for the builder to add in
  the morning — the setting the feature needs is the **Goodreads user id**, not a key.
- **Goodreads' RSS feeds are alive, public, and need neither key nor cookie for a public profile.**
  - `https://www.goodreads.com/review/list_rss/{user_id}?shelf={shelf}` — shelf membership with
    book id, title, author, ISBN/ISBN-13, cover URL, page count, average rating, the user's own
    rating, date added / date read, shelf names. Capped at the last 100 items per shelf. No
    progress field.
  - `https://www.goodreads.com/user_status/list/{user_id}?format=rss` — the user's status updates,
    including numeric reading progress as text ("is on page 137 of 248 of *Title*", "is 81% done
    with *Title*"), paginated with `page=`. Verified live by the review gate against a public
    profile. This is the doctrine-safe progress source the first research pass missed.
- **Goodreads has no search endpoint** any client can use. Search and rich metadata must come from
  elsewhere. **Open Library** (`openlibrary.org/search.json`, `/isbn/{isbn}.json`,
  `covers.openlibrary.org`) is free, key-less, ISBN-addressable, carries a crowd-sourced
  `identifiers.goodreads` cross-reference on many editions, and asks only for a descriptive
  `User-Agent` (3 req/s identified, 1 req/s anonymous; covers subdomain 100 req / 5 min by non-id
  keys). Google Books works too but Google's own docs say public requests must carry a key.
- Session-cookie scraping of the user's own Goodreads pages (the `goodreads-user-scraper` shape)
  would reach private data but impersonates a live session against an anti-bot-protected site.

ADR-0070's reasoning (no third-party login or credential ceremony performed by the server) applies.
A public RSS feed fetched by user id is the cleanest possible position under it: no credential at
all, nothing to reject, nothing to refresh.

## Decision

1. **The Goodreads setting is the user id, not a key.** Settings → Goodreads takes a profile URL or
   bare numeric id (`goodreads.com/user/show/12345678-name` → `12345678`), stored as
   `goodreads_user_id`. "Test" fetches the `currently-reading` shelf feed and reports the item
   count and profile name. The feature requires the Goodreads profile to be public; the Settings
   card says so and a 403/empty feed on a known id surfaces "profile private or id wrong".

2. **No cookie, ever.** Mediatheca never accepts a Goodreads session cookie, password, or any
   credential that impersonates the user's session. If a future need can't be met by the public
   feeds, it is met by manual entry in Mediatheca, not by a pasted cookie.

3. **Shelf membership, ratings and read dates come from the shelf feed.** The sync reads
   `currently-reading`, `read` and `to-read`, matches items to library books by Goodreads book id
   first, then ISBN-13, then ISBN-10, and maps `to-read → Backlog`, `currently-reading → InFocus`,
   `read → Finished` (with `user_read_at` as the finished-on date). `user_rating` seeds an unset
   personal rating once and never overwrites a rating the user set in Mediatheca. Unmatched
   `currently-reading` items are **imported** as books, with metadata resolved through Open Library
   by ISBN (falling back to the feed's own title/author/cover when Open Library has no edition).
   `to-read` and `read` items are imported only when the user opts in per shelf on the Settings card
   (default: currently-reading only), to keep a 900-book "read" shelf from flooding the library.

4. **Reading progress comes from the user-status feed.** The sync parses each status item's text
   for "on page N of M" / "N% done" / "finished", joins it to a book (by the Goodreads book id in
   the item's link when present, else by the exact title against the currently-reading shelf) and
   emits `Observe_reading_progress` with `Source = Goodreads`, `Position = Page (N, Some M)` or a
   bare percent, `ObservedOn = pubDate`. Only items newer than `goodreads_last_status_seen` are
   processed, so the sync is idempotent across runs. Parsing is locale-aware only to the extent the
   builder's feed language demands (English patterns first; a non-matching item is skipped, never
   an error).

5. **Open Library is the book search and metadata source** for the search modal and for enriching
   imported books: `search.json` (title/author keyword), `/isbn/{isbn}.json` (edition by ISBN),
   `/works/{key}.json` (description, subjects), `covers.openlibrary.org/b/id/{cover_i}-L.jpg`
   (cover, saved as `posters/book-{slug}.jpg`). Every call carries
   `User-Agent: Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)` and goes through one
   adapter-owned throttle (ADR-0066's shape) at 1 request/second; the covers host gets its own gate.
   **Audnexus** (`api.audnex.us/books/{asin}`) is the metadata fallback for audiobooks whose Audible
   product response is thin. Google Books is not integrated (it needs a key for no gain).

## Alternatives considered

- **Google Books as the metadata source** — comparable data, better search ranking, but Google's
  docs require an API key for public requests and quota numbers conflict across sources. Open
  Library's ISBN join and Goodreads cross-reference matter more for this feature than ranking.
- **Session-cookie scrape** — rejected under ADR-0070's reasoning and Goodreads' anti-bot posture.
- **CSV "Export Library" upload** — no progress column, manual and rate-limited; not needed once the
  feeds work. May return as a one-time bulk-import path if the 100-item shelf cap ever bites.
- **Hardcover.app** — a genuine Goodreads replacement with a pasteable token and page-level
  progress, but it requires the builder to adopt a second tracking service. Not now; noted as the
  fallback if Goodreads ever removes the public feeds.

## Consequences

- Nothing Goodreads-related can be "rejected" the way a token can; the only failure modes are a
  private profile, a wrong id, a network error, or a feed shape change. Each surfaces as data on
  the Settings card and in the job run, never as a retry loop.
- The 100-item shelf cap means a very large `read` shelf imports partially; acceptable, opt-in.
- Goodreads progress is only as fresh as the user's own status updates on Goodreads. That is the
  builder's stated intent ("progress based on the Goodreads percentage").
- Open Library's data quality is uneven (missing covers, duplicate works). The search modal shows
  what it returns with a source badge; the user picks. Audible's catalog search covers audiobooks.
