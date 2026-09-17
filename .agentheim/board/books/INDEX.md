# books — Index (task board)

Catalog of this bounded context's tasks by status.

> Updated by: `modeling` (tasks), the lifecycle verbs (promote / claim / complete / capture / dismiss / bounce / reroute).
> Hand-edits are fine but the verbs will append at the section markers below.

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 0
- **Doing:** 2
- **Done:** 9
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
- **books-d4wtc** — Prior reading progress — the bulk import's `Record_prior_reading_progress` command yields a `Prior_reading_progress_recorded` event for a book its source has not reported on before (never InFocus-promoting, finishing with its own date; the nightly sync and Manual keep producing ordinary observations), projected as `kind = 'prior'` so the History list, the Hours Listened stat and any future Reading day never mistake an import's starting position for a listening session (feature) — `doing/books-d4wtc-prior-reading-progress-event.md`
- **books-xntts** — Open Library import takes the work's canonical English edition (`cover_edition_key`, language-filtered fallback) instead of the first of hundreds of unordered `edition_key`s, and converts the work's Markdown description into the sanitized HTML subset at import and refresh — no more Spanish titles/covers on an English work, no more literal `[link](url)` lists (bug) — `doing/books-xntts-open-library-import-canonical-edition-and-markdown-description.md`
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **books-xyqyb** — Manual finish stamps today's local date and the hero's finished-date line is click-to-edit — `Set_book_status Finished` sends `Some localToday` instead of `None` (no more UTC-midnight drift), and the `finished {date}` line opens an `EditableDateInput` whose commit re-dates the finish via `setBookStatus slug Finished (Some picked)` (feature) — `done/books-xyqyb-finished-date-local-today-and-editable.md`
- **books-h4mq2** — book-detail-progress.spec.ts must open the hero ActionMenu before clicking "Update progress" (bug) — `done/books-h4mq2-book-detail-progress-spec-ts-must-open-the-hero-actionmenu-b.md`
- **books-jm7aa** — Book detail hero mirrors the game detail hero — the personal rating sits right of the status badge as a hero control, the reading state closes the hero meta block (finished date when Finished, otherwise a percent progress line), Details print on the page background like the movie synopsis, and Links move to the right column above Recommended By (feature) — `done/books-jm7aa-book-detail-hero-mirrors-game-detail.md`
- **books-depwh** — The book detail page's Details card no longer shows a date — the metadata line under the description reads just `Publisher · Language`, dropping the published/release date that Audible imports put at its end (feature) — `done/books-depwh-details-card-drops-published-date.md`
- **books-nvnyk** — Audible-imported book descriptions show their intended formatting (paragraphs, emphasis, lists) instead of raw `<p>`/`<i>` tags — the Audible adapter keeps the publisher summary as a sanitized allowlisted HTML subset (Audible and Audnexus paths alike) and the book detail page renders it through a tag-allowlisting rich-text component, never `innerHTML` (bug) — `done/books-nvnyk-audible-descriptions-render-formatting-not-raw-html.md`
- **books-f3sb2** — Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types (feature) — `done/books-f3sb2-book-detail-catalogs-card-and-removal-cascade.md`
- **books-g7g1j** — Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route (feature) — `done/books-g7g1j-search-modal-books-tab.md`
- **books-f33e2** — Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove (feature) — `done/books-f33e2-book-detail-page.md`
- **books-y9kxy** — Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared (feature) — `done/books-y9kxy-book-aggregate-projection-and-api.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->

## Pointers

- Done-list archive (entries rolled out beyond the live cap, if any): `done-archive/YYYY-MM.md` (ADR-0039 convention, agentic-workflow-c8j3w)
- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/books/INDEX.md`
