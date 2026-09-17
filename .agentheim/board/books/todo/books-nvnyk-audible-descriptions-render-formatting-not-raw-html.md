---
id: books-nvnyk
title: Audible-imported book descriptions show their intended formatting (paragraphs, emphasis, lists) instead of raw `<p>`/`<i>` tags — the Audible adapter keeps the publisher summary as a sanitized allowlisted HTML subset (Audible and Audnexus paths alike) and the book detail page renders it through a tag-allowlisting rich-text component, never `innerHTML`
status: todo
type: bug
context: books
created: 2026-09-17
completed:
depends_on: [design-system-001-formalize-styleguide]
blocks: []
tags: [books, audible, description, detail-page, frontend, integration, metadata-cache]
related_adrs: [0074, 0045, 0043]
related_research: [audible-api-surface-and-listening-progress-2026-09-16]
prior_art: [books-f33e2]
---

## Why

After importing books from Audible, the description on `/books/{slug}` shows literal HTML — `<p>`, `<i>`, `<b>`, `<br>` — inside the text. The builder does not want to see tags, but the formatting they encode (paragraph breaks, italics for titles, bold, the occasional list) is worth showing: a publisher summary flattened to one run-on paragraph reads worse than the original.

Root cause, traced 2026-09-17 in `src/Server/Audible.fs`:

- `Audible.decodeProduct` and `Audible.decodeLibraryItem` run `publisher_summary` through a private `stripHtml` (`Regex.Replace(s, "<[^>]+>", "")`) — tags gone, but so is every paragraph break and emphasis. Result: one flat block of text.
- `Audnexus.decodeAudnexusBook` takes `summary` (falling back to `description`) **verbatim** — no stripping at all. Audnexus' `summary` is the same publisher HTML. `Api.addBookFromAudible` (line ~1852) and `Api.importAudibleLibrary` (line ~2022) fall back to Audnexus whenever the Audible product/library item lacks a description, which is the common case for `/1.0/library` items. That is the path that puts raw tags in `book_metadata_cache.description`.
- `BookDetail.Views.detailsCard` renders `book.Description` as a single `Html.p` with `prop.text` — so whatever string arrives is shown literally.

## What

Two halves, shipped together (shipping the adapter half alone would put tags on the page for *every* Audible book, not just the Audnexus-sourced ones):

**Integration's Audible adapter (`src/Server/Audible.fs`) — stop destroying the formatting, stop passing raw HTML through.** Replace the tag-stripping `stripHtml` and the unstripped Audnexus passthrough with ONE shared sanitizer applied to every Audible-sourced description (`decodeProduct`, `decodeLibraryItem`, `decodeAudnexusBook`): keep an allowlisted subset of tags — `p`, `br`, `b`, `strong`, `i`, `em`, `ul`, `ol`, `li` — with all attributes dropped, and strip every other tag (`a`, `span`, `div`, `img`, `script`, `style`, `font`, headings, …) down to its text content. Entities stay as HTML entities (the renderer decodes them). The sanitized string is what lands in `book_metadata_cache.description` (source = audible), still cache tier per ADR-0043/ADR-0045 — no event, no projection handler involvement. Open Library and Goodreads paths are untouched (Open Library descriptions are plain text / light markdown, Goodreads supplies no description).

**Books' detail page — render the description as rich text, safely.** Add a small reusable client component (suggested: `src/Client/Components/RichText.fs`, `RichText.render : string -> ReactElement`) that parses the string with the browser's `DOMParser` (`text/html`) and maps nodes to Feliz elements: the allowlisted tags above become `Html.p`/`Html.br`/`Html.strong`/`Html.em`/`Html.ul`/`Html.ol`/`Html.li`, text nodes become text (entities decoded by the parser), any other element is replaced by its children (unwrap, never drop the text), every attribute is ignored. Never `dangerouslySetInnerHTML`. The component is the safety boundary on the client, so it is correct regardless of what the server stored — which also makes it **backward compatible with every row already in the cache**:

- a legacy plain-text row (earlier `stripHtml` output, or any Open Library description) has no tags → rendered as paragraphs split on blank lines / newlines;
- a legacy raw-Audnexus row (tags present, unsanitized) → rendered correctly without any backfill or re-import.

`BookDetail.Views.detailsCard` uses the component in place of the single `Html.p`, keeping the existing `text-base-content/70 leading-relaxed` voice; paragraphs get the design system's ordinary vertical rhythm (a `space-y-*` wrapper, no new tokens), lists the standard indented bullet/number style.

Out of scope, noted for a later capture: Steam's `about_the_game`/`detailed_description` and RAWG descriptions go through the same `stripHtmlTags` flattening (`Api.fs`, `PlaytimeTracker.fs`, `Rawg.fs`) — the game detail page could adopt the same component and sanitizer later, but this task touches books only.

## Acceptance criteria

- [ ] `Audible.fs` exposes one sanitizer (e.g. `Audible.sanitizeDescription : string -> string`) used by `decodeProduct`, `decodeLibraryItem` and `Audnexus.decodeAudnexusBook`; the old `stripHtml` is gone and no Audible-sourced description reaches `book_metadata_cache` unsanitized.
- [ ] Expecto: sanitizing `<p>A <i>novel</i> by <a href="x"><b>Someone</b></a>.</p><div class="q"><p>Second.</p></div><script>alert(1)</script>` yields `<p>A <i>novel</i> by <b>Someone</b>.</p><p>Second.</p>alert(1)`-shaped output: `p`/`i`/`b` kept, `a`/`div`/`script` unwrapped to their text, every attribute dropped. (Exact whitespace normalisation is the worker's call; the test pins the tag set and attribute removal.)
- [ ] Expecto: `Audnexus.getBook` decoding a fixture whose `summary` is HTML returns a `Description` with only allowlisted tags (regression for the unstripped passthrough).
- [ ] Expecto: `importAudibleLibrary` (existing `AudibleLibrarySyncTests` harness) with a library item lacking `publisher_summary` and an Audnexus fixture carrying `<p>…</p><p>…</p>` persists a `book_metadata_cache.description` that contains `<p>` and no `<script>`/attributes.
- [ ] Vitest (`*.test.fs`, Fable.Mocha DSL, ADR-0064): the rich-text component's pure parse/map step, given `<p>One <em>two</em></p><p>Three<br>Four</p><ul><li>a</li></ul>`, produces two paragraphs (the first containing an `em`, the second containing a `br`) and one `ul` with one `li`; given `<span onclick="x">t</span><img src=x>u` produces the text `tu` with no `span`/`img` and no attributes; given plain text with a blank line produces two paragraphs.
- [ ] The component never uses `dangerouslySetInnerHTML` / `innerHTML` (grep over `src/Client/` is empty for both).
- [ ] `BookDetail.Views.detailsCard` renders `book.Description` through the component instead of a single `Html.p` with `prop.text`; a description containing `<p>` renders multiple `<p>` DOM elements and no literal `<` characters in the visible text.
- [ ] An Audible book imported before this change whose cached description still holds raw Audnexus HTML shows formatted text on `/books/{slug}` without a re-import or backfill (covered by the parse-step test with a raw-HTML input, plus a builder eye-check on the live page).
- [ ] `npm run build`, `npm test` and `npm run test:client` are green.

## Notes

- Design decision recorded here rather than as an ADR (cache tier, no event shape changes): the **stored** form is a sanitized HTML subset (not Markdown, not a custom rich-text DU) because it round-trips the source's own markup with the least translation and stays readable in the admin/DB views; the **rendered** form is Feliz elements built from a `DOMParser` walk with a tag allowlist, so the client never trusts the string. Both layers allowlist independently — belt and braces against a third-party feed.
- Why not Markdown at import: Audible/Audnexus summaries occasionally nest `<b>` inside `<i>` and use `<br>` runs for line breaks; an HTML→Markdown pass adds a second grammar for no gain when the renderer must parse *something* anyway.
- `DOMParser` is available in Fable via `Browser.Dom` (`Browser.Types.DOMParser` / `Fable.Core.JsInterop` `createNew`); the parse/map step should be a pure function over the parsed node tree so the Vitest case above can run under jsdom.
- Fixtures: `tests/Server.Tests/AudibleTests.fs` / `AudibleLibrarySyncTests.fs` already carry Audible and Audnexus JSON shapes to extend (worker: fixtures only — never the live DB, per the project's standing rule).
- Prior art: `books-f33e2` built the detail page and the description card this task changes. Integration's Audible adapter is documented in `contexts/integration/README.md` (integration-dhctm, integration-jjvg2) and ADR-0074; the README's Audible entry should gain one sentence on the sanitizer in the worker's README delta.
- Follow-up candidate (not captured): apply the same sanitizer + component to game descriptions (Steam `about_the_game`, RAWG).
