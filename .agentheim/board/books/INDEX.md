# books — Index (task board)

Catalog of this bounded context's tasks by status.

> Updated by: `modeling` (tasks), the lifecycle verbs (promote / claim / complete / capture / dismiss / bounce / reroute).
> Hand-edits are fine but the verbs will append at the section markers below.

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 1
- **Doing:** 1
- **Done:** 4
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **books-depwh** — The book detail page's Details card no longer shows a date — the metadata line under the description reads just `Publisher · Language`, dropping the published/release date that Audible imports put at its end (feature) — `todo/books-depwh-details-card-drops-published-date.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
- **books-nvnyk** — Audible-imported book descriptions show their intended formatting (paragraphs, emphasis, lists) instead of raw `<p>`/`<i>` tags — the Audible adapter keeps the publisher summary as a sanitized allowlisted HTML subset (Audible and Audnexus paths alike) and the book detail page renders it through a tag-allowlisting rich-text component, never `innerHTML` (bug) — `doing/books-nvnyk-audible-descriptions-render-formatting-not-raw-html.md`
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
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
