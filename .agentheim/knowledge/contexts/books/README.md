# Books

## Purpose
Owns the **Book aggregate** — a book (audiobook, print or ebook) as a curated library entry with its identity card, external ids, lifecycle status, personal rating, and the **reading-progress observations** tied to it. Source of truth for "am I reading this", "how far am I", "did I finish it", "did I like it".

## Classification
**core** — The fourth media-type BC, alongside Movies, Series and Games. Recognized 2026-09-16 (vision previously listed Books as v2 / out of scope for v1).

## Actors
Single user (library owner). External progress sources (Audible) act through Integration's adapters, never directly.

## Ubiquitous language

- **Book** — a work in the user's library. Identified by an internal slug (`Slug.bookSlug title year`, stream `Book-{slug}`). Carries an **identity card** and zero or more **external ids**.
- **Identity card** (ADR-0043's term) — `Title`, `Authors`, `Year`, `CoverRef`, `Subjects` (genres). Ride in `Book_added_to_library` and are projection columns written by that event only; a refresh path never touches them.
- **External id** — one of `Isbn13`, `OpenLibraryWorkKey` (`/works/OL…W`), `OpenLibraryEditionKey`, `AudibleAsin`. A book may carry any subset; each is set at add time or later via `Link_external_id`. External ids are what Integration's adapters match on — the Audible library sync keys on ASIN. An external id is event-carried (it records that *this* library entry *is* that catalog item — a fact the user asserts, not a third party's description). `GoodreadsBookId` was removed 2026-09-18 (integration-sfmxg, ADR-0083) — no persisted event ever carried it.
- **Format** — `Audiobook | Print | Ebook | Unknown`, the primary format the user consumes. Set at add time from the source (Audible → `Audiobook`, Open Library → `Unknown` until the user says otherwise). Changeable via `Set_format`.
- **Status** — lifecycle position, exactly four states: `Backlog | InFocus | Finished | Abandoned`. Mirrors Games' shape rather than Movies' separate flag: `InFocus` means "reading this now or next". **Any progress observation that raises the percent promotes the book from any non-InFocus, non-Finished status to InFocus** (a `Finished` book that gets a higher observation — impossible above 100 % — stays `Finished`; ADR-0076 §5). An observation at 100 %, or an Audible `is_finished`, transitions to `Finished`. `Abandoned` is a manual stop. There is no `Dismissed`.
- **Reading progress** — the book's current position as a **percent** (0–100, integer). Derived from the latest **progress observation**; shown on the detail page, the dashboard and list cards with its **source** badge.
- **Progress observation** — one dated observation of the user's own position in a book: `{ Percent; Position; Source; ObservedOn }`. `Position` is the raw reading, optional: `Page of page * totalPages option` or `Minutes of minutes * totalMinutes option`. `Source` is `Audible | Goodreads | Manual`. **Event-worthy under ADR-0043** (an observation of the user's engagement at a moment that can't be re-observed later, exactly like a Steam-sourced play session) — never a cache write. Natural key `(bookSlug, observedOn, source)`: a second observation on the same day from the same source **replaces** the earlier one (latest wins, the day is what the Journal counts), and an observation whose percent equals the latest percent previously observed from that SAME SOURCE (default 0 when the source has no prior observation) is a **no-op** — the comparison is per-source, not the book's global current percent, so two sources sitting at different percents on the same day (e.g. Audible 42%, Goodreads 40%) each record independently without ping-ponging a status event, and a daily sync of an untouched library appends zero events. (Sharpened by books-y9kxy: the aggregate's `Observations: Map<(observedOn, source), percent>` is what makes the per-source baseline computable.)
- **Progress regression** — an observation lower than the SAME SOURCE's previously observed percent (books-y9kxy: the baseline is per-source, matching "Progress observation" above). Accepted, not refused (a re-read restarts at 0; Audible lets the user rewind), but it never promotes to InFocus and never un-finishes: a `Finished` book stays `Finished` on regression until the user changes status by hand; removing the observation that finished a book also never reverts status — `Change_status` is the only way back.
- **Length** — total pages (print) or runtime minutes (audio). A third party's description of the work — **cache tier** (`book_metadata_cache.page_count` / `runtime_minutes`, ADR-0045), never event-carried. A `Position` inside an observation may carry its own total (Goodreads says "page 120 of 300"); the percent is computed at observation time from that, so the event stays self-contained.
- **Metadata cache slice** — description, page count, runtime minutes, narrators, series name + position, publisher, published date, average rating, language: `book_metadata_cache`, written by Integration's adapters at add time and on refresh, read by `BookProjection.getBySlug` via a query-time join (`COALESCE` nowhere — identity-card columns are never cache-joined). A `ProjectionHandler` never reads or writes it (ADR-0045's hard constraint).
- **Personal rating** — integer rating set by the user; mutates over time. A source may seed it once (if unset), but never overwrites a rating the user set by hand.
- **Recommended by (friend)** — provenance: this book entered the library because a friend suggested it. Same shape as Movies.
- **Finished on** — the date of the `Book_status_changed Finished` event: a manual finish now stamps today's local date (not the event's UTC append timestamp, ADR-0082 §8 — avoids a late-evening finish landing on yesterday), while Audible's `is_finished` import and other sources carry their own effective-on date (ADR-0077). Click-to-edit on the book detail hero via `EditableDateInput` (books-xyqyb); re-dating an already-Finished book is a legitimate event (ADR-0077 §4), and a future date is rejected at the API edge. Drives the dashboard's 7-day "just finished" linger (the intelligence-b1nz5 pattern).
- **Description rendering** — `book_metadata_cache.description` (Audible/Audnexus-sourced) is stored as a sanitized, allowlisted HTML subset (`p`/`br`/`b`/`strong`/`i`/`em`/`ul`/`ol`/`li`, attributes always dropped) rather than raw HTML or a flattened plain-text blob; `BookDetail.Views.detailsCard` renders it through `RichText.render` (`src/Client/Components/RichText.fs`), a pure hand-rolled tokenizing renderer that never trusts the string a second time and is backward compatible with every row already in the cache, including pre-fix rows still carrying raw tags (books-nvnyk). Open Library work descriptions are Markdown, not HTML — `OpenLibrary.descriptionToHtml` (books-xntts) converts the Markdown subset Open Library actually uses (paragraphs, `**bold**`/`*italic*`, `- `/`1. ` lists, `[text](url)` links dropped to plain text, `#`-headings) into the same allowlisted HTML subset at decode time, discarding the trailing `---`-separated editorial appendix ("Contains"/"See also"/source citations) entirely; existing rows are repaired by re-running `refreshBookFromOpenLibrary`, not a startup backfill.

## Aggregates

- **Book** — protects: observations and status changes only after `Book_added_to_library`; percent within 0–100; same-percent observation is a no-op; a raising observation promotes to InFocus; a 100 % observation (or an explicit finished flag from the source) sets `Finished`; regression never un-finishes; an external id of a given kind is set once and re-linking to a different value is refused (`Link_external_id` is idempotent on the same value).

## Key events

`Book_added_to_library`, `Book_removed_from_library`, `Book_cover_replaced`, `Book_external_id_linked`, `Book_format_set`, `Book_status_changed`, `Reading_progress_observed`, `Reading_progress_observation_removed`, `Book_personal_rating_set`, `Book_recommended_by`, `Book_recommendation_removed`.

## Key commands

`Add_book_to_library`, `Remove_book_from_library`, `Replace_cover`, `Link_external_id`, `Set_format`, `Change_status`, `Observe_reading_progress`, `Remove_reading_progress_observation`, `Set_personal_rating`, `Recommend_by`, `Remove_recommendation`.

## Read models

- `book_list` — slug, identity card, status, current progress percent + source + observed-on, personal rating, format, external ids, finished-on. Projected.
- `book_detail` — `book_list` plus `recommended_by`. Projected.
- `book_progress` — `(book_slug, observed_on, source) → percent, position_json`. Projected; the Journal's reading-activity source.
- `book_metadata_cache` — cache tier (see Metadata cache slice).

## Relationships with other contexts

- **Upstream of:** Journal (publishes `Reading_progress_observed`, `Book_status_changed`), Intelligence (dashboard Books tab / All-tab reading rail).
- **Downstream of:** Friends (friend slugs on recommendations).
- **Downstream of:** Integration via anticorruption — the Open Library adapter (search, ISBN lookup, covers) and the Audible adapter (catalog search, library import, listening-progress sync) translate into `Add_book_to_library`, `Link_external_id`, `Observe_reading_progress`, `Change_status`, `Set_personal_rating` commands and `book_metadata_cache` writes. See ADR-0074 (Audible credentials), ADR-0075 §5 (Open Library as the metadata source; its Goodreads half was retired by ADR-0083).
- **Consumed by:** Curation — catalogs reference books by `(MediaType.Book, slug)` (ADR-0079); the book detail page's catalog pill row/picker and `removeBook`'s catalog-entry removal cascade shipped in `books-f3sb2`.

## Frontend gate

Frontend tasks in this BC (search-modal Books tab, book detail page) **must** `depends_on` the design-system styleguide task (`design-system-001-formalize-styleguide`). See [[design-system]].

## Open questions

- Whether a book should carry *two* formats at once (owned on Audible *and* in print) or stay single-format. Single-format for now; the external ids already allow both an ASIN and an ISBN on one book, so the data isn't lost.
