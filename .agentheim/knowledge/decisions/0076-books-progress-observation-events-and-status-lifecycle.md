---
id: 0076
title: Books model — a reading-progress observation is an event (ADR-0043's engagement test), book length and description are cache tier, the status lifecycle mirrors Games (Backlog | InFocus | Finished | Abandoned) with progress-driven promotion and 100 %-driven finish
scope: books
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
amended_by: [0082, 0085]
amends: []
related_tasks: [books-y9kxy, books-f33e2, integration-jjvg2, integration-y2ak4]
related_research: [audible-api-surface-and-listening-progress-2026-09-16, goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
---

# ADR 0076: Books model — progress observations are events; length is cache; status mirrors Games

## Context

Books is the fourth media-type BC. Its progress signal differs from the other three: Movies have a
binary watch session, Series count episodes, Games sum play minutes — Books have a **position**
(page 137 of 248, or 81 % of an audiobook) reported by an external source (Audible's
`percent_complete`, Goodreads' status feed) or typed by the user. The vision's In Focus concept
must apply ("what am I reading next / now"), the dashboard needs a "just finished" linger, and the
Journal needs a dated activity to put on the heatmap.

ADR-0043 gives the test: an event records an observation of the user's own engagement; a cache
records a third party's description. ADR-0050 (Games) already settled the closest analogue — a
Steam-observed play-time delta is a first-class `Play_session_recorded` event with a source, never a
republished total.

## Decision

1. **`Reading_progress_observed` is an event.** Payload `{ Percent: int; Position: ReadingPosition
   option; Source: Audible | Goodreads | Manual; ObservedOn: date }` where `ReadingPosition =
   Page of page * total option | Minutes of minutes * total option`. It is the user's engagement at
   a moment that cannot be re-observed later — it passes the re-derivability test as an event, not
   a cache. The percent is computed at observation time (from the source's own percent, or from
   page/total) so the event is self-contained and replay never needs the cache.

2. **Same-day, same-source observations collapse; same-percent observations are no-ops.** The
   natural key is `(slug, ObservedOn, Source)` — a later observation on the same day replaces the
   earlier one in the projection (latest wins; the *day* is what the Journal counts), and `decide`
   emits nothing when the incoming percent equals the aggregate's current percent. A daily sync of
   an untouched library therefore appends zero events. `Reading_progress_observation_removed
   (ObservedOn, Source)` exists for corrections, mirroring `Play_session_removed`.

3. **Length and description are cache tier.** `page_count`, `runtime_minutes`, `description`,
   `narrators`, `series_name` / `series_position`, `publisher`, `published_date`,
   `average_rating`, `language` live in `book_metadata_cache` (ADR-0045), written by Integration's
   adapters and read by a query-time join in `BookProjection`. A `ProjectionHandler` never touches
   them. The identity card (`title`, `authors`, `year`, `cover_ref`, `subjects`) rides
   `Book_added_to_library` and stays a projection column under ADR-0043's identity-card clause.

4. **External ids are event-carried.** `Book_external_id_linked of Isbn13 | OpenLibraryWork |
   OpenLibraryEdition | AudibleAsin | GoodreadsBookId` records that this library entry *is* that
   catalog item — a fact the user (or an import acting for them) asserts, not a third party's
   description. One value per kind; re-linking a different value is refused, the same value is
   idempotent. Adapters match on them (ASIN for Audible, Goodreads id then ISBN for Goodreads).

5. **Status mirrors Games, not Movies.** `Backlog | InFocus | Finished | Abandoned`, a status rather
   than a flag, because "reading" is a sustained state like "playing". Rules in `decide`:
   - an observation whose percent is **higher** than the current one promotes any non-InFocus,
     non-Finished book to `InFocus` (a Finished book that gets a higher observation — impossible
     above 100 — stays Finished);
   - an observation at **100 %**, or a source's explicit finished flag (`Observe_reading_progress`
     carries `Finished: bool`), emits `Book_status_changed Finished` after the observation;
   - a **lower** observation (re-read, rewind) is recorded but never changes status;
   - `Change_status` is the manual override for everything else (Abandoned, back to Backlog, a
     re-read from Finished to InFocus).
   `Finished` carries no separate event; the dashboard's 7-day linger keys on the status-change
   event's timestamp projected into `book_list.finished_at` (the intelligence-b1nz5 pattern).

6. **Format is a single value** (`Audiobook | Print | Ebook | Unknown`), event-carried, set from the
   add source and changeable. A book owned on Audible *and* in print is one book with both an ASIN
   and an ISBN linked; the format records how the user is reading it now.

## Alternatives considered

- **Percent as a mutable projection column written by the sync (cache)** — fails ADR-0043: the
  observation is engagement, and a rebuild would lose every reading day.
- **A `Reading_session` with minutes, like games** — Audible exposes percent, not session minutes,
  and Goodreads exposes pages; a synthetic minutes figure would be a fabricated measurement.
- **In Focus as a flag (Movies' shape)** — needs a separate "finished" signal; Games' status shape
  carries both in one place and the dashboard already renders it.
- **Refusing progress regression** — would make a re-read impossible without deleting history.

## Consequences

- `BookProjection` gets a `book_progress (book_slug, observed_on, source, percent, position_json)`
  table; `book_list.progress_percent / progress_source / progress_observed_on` are denormalized from
  the latest observation and rebuild deterministically.
- The Audible progress sync and the Goodreads status sync both funnel through one command,
  `Observe_reading_progress`, so the aggregate — not the adapters — owns promotion and finishing.
- The Journal folds `book_progress` rows as reading activity days (`journal-k52j1`).
- Drift check (ADR-0031) and rebuild stay lossless: nothing about a book's engagement lives outside
  the event log.
