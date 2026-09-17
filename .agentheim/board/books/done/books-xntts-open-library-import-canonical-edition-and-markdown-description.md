---
id: books-xntts
title: Open Library import takes the work's canonical English edition (`cover_edition_key`, language-filtered fallback) instead of the first of hundreds of unordered `edition_key`s, and converts the work's Markdown description into the sanitized HTML subset at import and refresh — no more Spanish titles/covers on an English work, no more literal `[link](url)` lists
status: done
type: bug
context: books
created: 2026-09-18
completed:
depends_on: [integration-sfmxg]
blocks: []
tags: [open-library, import, description, sanitizer, edition, language]
related_adrs: [0043, 0045, 0075]
related_research: []
prior_art: [books-nvnyk, books-g7g1j]
---

## Why

Importing "The Lord of the Rings" from the search modal's Books tab produced a book titled
*El señor de los anillos 2, las dos torres* with a Spanish cover, while the search tile had
shown the English cover and the description was English. Two deterministic causes, both
confirmed against the live Open Library API on 2026-09-18:

1. **Wrong edition.** `search.json`'s `edition_key` lists every edition of the work in no
   meaningful order — 253 entries for `/works/OL27448W`, the first being `OL62536872M`, a
   1966 Spanish edition. `OpenLibrary.decodeSearchDoc` (`src/Server/OpenLibrary.fs`, the
   `EditionKey = ... |> List.tryHead` line) keeps that head, and `addBookFromOpenLibraryImpl`
   (`src/Server/Api.fs`) then sources **title, authors, publish year and cover** from that one
   edition. The search tile shows the work's `cover_i` (14625765), which is why it looked
   English; the imported cover came from the Spanish edition's `covers[0]` (15252991).
   The description comes from the work record and is language-neutral, so it stayed English.
   `search.json` already exposes `cover_edition_key` — the edition Open Library itself picked
   as canonical (`OL51694024M`, `languages: [eng]`, `covers: [14828778, 14625765]`, i.e. it
   carries `cover_i`) — and a `language` list per doc.

2. **Raw Markdown description.** Open Library work descriptions are **Markdown**, not HTML.
   This work's ends with a `---` rule, a `**Contains**` heading and a bulleted list of
   Markdown links to the child works. The import stores the string raw in
   `book_metadata_cache.description`, and the client renders it through the HTML-subset
   `RichText.render` (`src/Client/Components/RichText.fs`, books-nvnyk), which splits plain
   text on blank lines and knows nothing about Markdown — so the list collapses into one
   paragraph of literal `- [The Two Towers](https://openlibrary.org/works/...)` text. No
   Markdown handling exists anywhere on the Open Library path; `DescriptionSanitizer.sanitize`
   (games-r1tx4) only allowlists HTML tags.

## What

Both fixes are server-side, inside Integration's Open Library anticorruption layer and the
`addBookFromOpenLibrary` / `refreshBookFromOpenLibrary` API paths that feed the Books cache
slice (ADR-0075, ADR-0045). No event, no projection, no renderer change — the identity card
stays event-carried (ADR-0043); only what is *derived from the search hit at add time* and
the cache-tier description change. The single client edit is `src/Client/State.fs`'s
`Import_openlibrary` handler copying two more fields from the search hit into the request.

### 1. Canonical edition selection

- `OpenLibrary.searchBooks` requests `cover_edition_key` and `language` in the `fields=`
  list alongside the existing fields.
- `decodeSearchDoc` populates `OpenLibrarySearchResult.EditionKey` by preference order:
  1. `cover_edition_key` when present;
  2. otherwise the first `edition_key` — **but only when the doc's `language` list is
     absent or contains `eng`**; the fallback must not pick an edition it cannot vouch for.
     If `language` is present and lacks `eng`, still take the first `edition_key` (the
     work simply has no English edition; today's behaviour is the ceiling, not a
     regression).
  `OpenLibrarySearchResult.EditionKey` keeps its `string option` shape — the search modal
  and its tests are untouched.
- Belt and braces at import: after `getEditionByOlid` resolves the edition,
  `addBookFromOpenLibraryImpl` prefers the **search hit's `cover_i`** for the cover download
  when the request carries one, falling back to the edition's `covers[0]` only when it does
  not. `AddBookFromOpenLibraryRequest` (`src/Shared/Shared.fs`) today carries only
  `WorkKey` / `EditionKey` / `Isbn13` / `SkipDuplicateCheck` — add `CoverId: int option` and
  `Title: string` (both already on `OpenLibrarySearchResult`) and populate them in
  `src/Client/State.fs`'s `Import_openlibrary` handler next to `EditionKey`/`Isbn13`. The
  cover the user clicked is the cover they get.
- Title/authors/year continue to come from the resolved edition (now the canonical one).
  When no edition resolves at all, the title falls back to the **search hit's `Title`**, not
  the work key string (today's `Option.defaultValue request.WorkKey` yields a book literally
  titled `/works/OL27448W`) — the `Title` field added above.

### 2. Markdown → sanitized HTML subset for work descriptions

- Add `OpenLibrary.descriptionToHtml : string -> string` (or a small
  `OpenLibraryMarkdown` module compiled next to `DescriptionSanitizer.fs` — worker's call,
  but it must be a pure function with its own Expecto cases) that converts the Markdown
  subset Open Library actually uses into `DescriptionSanitizer`'s allowlisted HTML subset
  (`p`/`br`/`b`/`strong`/`i`/`em`/`ul`/`ol`/`li`):
  - blank-line-separated paragraphs → `<p>…</p>`; single newlines inside a paragraph → `<br>`;
  - `**x**` / `__x__` → `<strong>x</strong>`; `*x*` / `_x_` → `<em>x</em>`;
  - `[text](url)` → `text` (plain text; the renderer has no anchor support and external
    links to Open Library works are noise on a library card) — the same for reference-style
    `[text][ref]` if encountered; bare `<https://…>` autolinks are dropped;
  - `- ` / `* ` / `1. ` list items → `<ul><li>…</li></ul>` / `<ol><li>…</li></ol>`;
  - `#`-headings → `<p><strong>…</strong></p>`;
  - **the trailer is cut**: everything from the first line that is a horizontal rule
    (`---`, `***`, `___`, optionally surrounded by blank lines) to the end is discarded.
    Open Library uses that rule exclusively to separate the blurb from editorial appendices
    ("Contains", "See also", source attributions like `([source][1])`). Also strip a
    trailing `([source][n])` / `[n]: url` reference-definition block when it appears without
    a rule.
  - Markdown escapes `\[`, `\]`, `\*`, `\_` unescape to the literal character.
  - Output is finally passed through `DescriptionSanitizer.sanitize` so any stray HTML in
    the Markdown source (Open Library allows it) is allowlisted exactly like Audible's and
    Steam's descriptions — one sanitizer, no second allowlist (games-r1tx4's rule).
- `OpenLibrary.getWork` applies the conversion to `Description` at decode time, so both
  `addBookFromOpenLibraryImpl` and `refreshBookFromOpenLibraryImpl` write the converted
  string without either call site remembering to — mirroring how `Audible.fs` sanitizes at
  decode time (books-nvnyk) and `Steam.storeDescription` does (games-r1tx4).
- **Existing rows are repaired by the refresh action, not a backfill.** The book detail
  page already wires `refreshBookFromOpenLibrary` (`src/Client/Pages/BookDetail/State.fs`);
  running it on an affected book rewrites
  the description through the converter. State this in the README delta; do not add a
  startup job.

## Acceptance criteria

- [ ] `OpenLibrary.searchBooks`'s request URL includes `cover_edition_key` and `language` in
      `fields=` (Expecto: the URL captured by the fake `HttpMessageHandler` in
      `tests/Server.Tests/OpenLibraryTests.fs`).
- [ ] `decodeSearchDoc` with a doc carrying `cover_edition_key: "OL51694024M"` and
      `edition_key: ["OL62536872M", …]` yields `EditionKey = Some "OL51694024M"`.
- [ ] `decodeSearchDoc` with no `cover_edition_key` and `language: ["spa","eng"]` yields the
      first `edition_key`; with no `cover_edition_key` and no `language` yields the first
      `edition_key`; with `cover_edition_key` absent and `language: ["spa"]` (no `eng`) yields
      the first `edition_key` (documented ceiling).
- [ ] `addBookFromOpenLibrary` downloads the cover for the **search hit's `CoverId`** when
      the request carries one, even when the resolved edition's `covers[0]` differs (Expecto
      in `OpenLibraryApiTests.fs`: the covers-host URL requested by the fake handler is
      `/b/id/<request CoverId>-L.jpg`).
- [ ] `addBookFromOpenLibrary` with an edition key that resolves to a 404 stores the search
      hit's `Title`, never the work key string.
- [ ] `OpenLibrary.descriptionToHtml` (or the module the worker names) has Expecto cases for:
      paragraphs; `**bold**` and `*italic*`; a `[text](url)` link becoming plain `text`; a `- `
      list becoming `<ul><li>`; a `1. ` list becoming `<ol><li>`; `\[2/2\]` unescaping to
      `[2/2]`; and the exact Lord of the Rings trailer (blurb, blank line, `---`, blank line,
      `**Contains**`, blank line, eight `- [..](..)` bullets) producing only the blurb's
      `<p>` blocks with **no** `Contains`, no `[`, no `https://` in the output.
- [ ] A description with **no** Markdown syntax converts to the same output
      `DescriptionSanitizer.sanitize` would give for blank-line-split `<p>` paragraphs, so
      plain-prose works are unchanged in substance.
- [ ] Stray HTML inside the Markdown (e.g. `<script>x</script>` or `<a href>`) is allowlisted
      by `DescriptionSanitizer.sanitize` — the output contains no `<script>`/`<a` tag.
- [ ] `getWork` decoding a work JSON whose `description` is the `{type,value}` object form
      returns the converted HTML, and `refreshBookFromOpenLibrary` writes that converted
      string to `book_metadata_cache.description` (Expecto in `OpenLibraryApiTests.fs`).
- [ ] `RichText.parse` (Vitest, `src/Client/Components/RichText.test.fs`) given the
      converter's Lord of the Rings output renders it as paragraphs only — extend the
      existing suite with the converter's expected output string as a fixture; no client
      code change is expected.
- [ ] `npm run build`, `npm test` and `npm run test:client` are green.

## Notes

- **Dependency:** `integration-sfmxg` (remove Goodreads) rewrites `OpenLibrary.fs`'s edition
  decoder (drops `GoodreadsIds`) and `addBookFromOpenLibraryImpl`'s external-id list — the
  same lines this task edits. It is in `todo/`; this task waits behind it to avoid a textual
  merge conflict in the same batch, not for any domain reason.
- **BC routing:** the code lives in Integration's Open Library anticorruption layer
  (integration-c8d4x, ADR-0075), but the defect and every acceptance criterion are about
  what a *Book* ends up with — the builder filed it under Books deliberately. The README
  delta belongs to Books (the "Description rendering" ubiquitous-language entry gains
  "Open Library descriptions are Markdown converted to the same subset at decode time") with
  a one-line pointer in Integration's README under the Open Library adapter.
- **Live evidence (2026-09-18):** `GET /search.json?q=Lord of the Rings` → doc
  `/works/OL27448W`, `cover_i 14625765`, `cover_edition_key OL51694024M`, `edition_key[0]
  OL62536872M` (253 entries), `language[0..5] = glg, ger, bul, jpn, tur, ita`.
  `GET /books/OL62536872M.json` → title `El señor de los anillos 2, las dos torres`,
  `languages [/languages/spa]`, `covers [15252991]`. `GET /books/OL51694024M.json` → title
  `The Lord of the Rings`, `languages [/languages/eng]`, `covers [14828778, 14625765]`.
  `GET /works/OL27448W.json` → `description` ends with `---`, `**Contains**`, eight
  `- [title](https://openlibrary.org/works/…)` bullets, some with `\[2/2\]` escapes.
- **Language preference is hard-coded to `eng`** for now — the app has no UI language
  setting and the builder reads English. If a preferred-language setting ever lands in
  Administration, the fallback filter is the one place to read it.
- **Out of scope:** anchor support in `RichText`; a startup backfill of existing Open Library
  descriptions (refresh covers it); Audible/Audnexus descriptions (already HTML, books-nvnyk).
- Prior art: books-nvnyk (Audible descriptions rendered through the allowlisted subset —
  the same storage contract this task now honours for Open Library), books-g7g1j (the
  search-modal Books tab whose tile shows `cover_i`). ADR-0075 (Open Library as the book
  metadata source), ADR-0045 (cache tier), ADR-0043 (identity card is event-carried; this
  task changes only what the add-time request derives from the hit plus a cache column).

## Outcome

Fixed both deterministic causes behind the "El señor de los anillos" import bug (wrong-language title/cover, literal Markdown in the description), server-side, inside Integration's Open Library anticorruption layer (`src/Server/OpenLibrary.fs`, `src/Server/Api.fs`) and the client's Open Library import request (`src/Client/State.fs`), with no event or projection change (ADR-0043's identity-card clause honoured — only what the add-time request derives from the search hit, plus the cache-tier description, changed).

**1. Canonical edition selection.** `OpenLibrary.searchBooks`'s request URL now also requests `cover_edition_key` and `language` in `fields=`. `decodeSearchDoc` (via the new private `preferredEditionKey`) prefers `cover_edition_key` — the edition Open Library itself picked as canonical, carrying the same `cover_i` the search tile renders — over the first (unordered) `edition_key`; when `cover_edition_key` is absent, the fallback is still the first `edition_key` in every case, `language` only documenting why (today's blind pick is the ceiling, not a regression, per the task's own Notes) rather than changing the outcome. `AddBookFromOpenLibraryRequest` (`src/Shared/Shared.fs`) gained `CoverId: int option` and `Title: string`, populated in `src/Client/State.fs`'s `Import_openlibrary` handler from the search hit's own `OpenLibrarySearchResult.CoverId`/`.Title` (both fields already existed on that type). `addBookFromOpenLibraryImpl` (`src/Server/Api.fs`) now prefers `request.CoverId` for the cover download over the resolved edition's own `covers[0]`, and falls back to `request.Title` (never the raw `request.WorkKey` string) when no edition resolves at all.

**2. Markdown → sanitized HTML for work descriptions.** Added `OpenLibrary.descriptionToHtml : string -> string` (`src/Server/OpenLibrary.fs`, private helper functions plus the public entry point) — a pure function converting the Markdown subset Open Library actually uses (blank-line paragraphs with internal `<br>`, `**bold**`/`__bold__`, `*italic*`/`_italic_`, `[text](url)`/`[text][ref]` links dropped to plain text, dropped bare `<https://…>` autolinks, `- `/`* `/`1. ` lists, `#`-headings bolded into their own `<p>`, `\[`/`\]`/`\*`/`\_` escapes unescaped via private-use placeholders) into `DescriptionSanitizer`'s allowlisted HTML subset, cutting everything from the first horizontal-rule line (`---`/`***`/`___`) onward (Open Library's blurb/appendix separator) plus a best-effort trailing reference-definition/citation strip for the rare appendix that skips the rule, then running the result through `DescriptionSanitizer.sanitize` itself — one allowlist, no second one, matching games-r1tx4's rule. `OpenLibrary.getWork` applies the conversion to the decoded `Description` so both `addBookFromOpenLibraryImpl` and `refreshBookFromOpenLibraryImpl` write the converted string without either call site remembering to. Existing affected rows are repaired by re-running `refreshBookFromOpenLibrary` (no startup backfill added, per the task's own scope).

**Tests (TDD, 23 new):** `tests/Server.Tests/OpenLibraryTests.fs` gained the `fields=` URL assertion, four `decodeSearchDoc` preference-order cases (cover_edition_key present; no cover_edition_key with `eng` in `language`; no `language`; `language` present without `eng`), and a new `openLibraryDescriptionToHtmlTests` list (15 cases: paragraphs, `<br>`, bold/italic both marker families, inline and reference-style links, autolink drop, unordered/ordered lists, headings, both escape families, the exact Lord-of-the-Rings-shaped trailer cut, no-markdown parity with `DescriptionSanitizer.sanitize`'s own output, and stray-HTML allowlisting) — plus updated the two existing `getWork` description tests to expect the now-converted `<p>…</p>` output. `tests/Server.Tests/OpenLibraryApiTests.fs` gained two cases (cover preference over the resolved edition's own cover; Title fallback on a 404'd edition, never the work-key string) and updated both existing description assertions to the converted `<p>…</p>` shape. `src/Client/Components/RichText.test.fs` gained one case rendering the converter's own Lord-of-the-Rings output as plain paragraphs (no client code change, per the task's own scope). `src/Client/Components/SearchModal.test.fs` updated two `AddBookFromOpenLibraryRequest` record literals for the new `CoverId`/`Title` fields.

All three gates green: `npm run build`, `npm test` (932/932 Expecto), `npm run test:client` (114/114 Vitest).
