# curation -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 2
- **Todo:** 1
- **Doing:** 1
- **Done:** 5
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **curation-tb0nn** — Guard Gate 1 / Gate 2 previews after the legacy purge — once `content_blocks`/`game_journal_blocks` are dropped both previews return HTTP 500 ("no such table"); with a `tableExists` check they report "legacy stores already purged" with zero owners/streams and Gate 2's confirm stays disabled (bug) — `todo/curation-tb0nn-guard-gate-previews-after-legacy-purge.md`
<!-- no tasks in todo -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
- **curation-n2nkm** — "`Notes_saved` is unformattable — `EventFormatting.formatEvent` dispatches by stream prefix and has no `Notes-` arm, so every Notes stream shows in the Health tab's Unformattable list and renders nothing in the event browser; add `formatNotesEvent` (block count + first text excerpt) and the guard test" (bug) — `doing/curation-n2nkm-notes-saved-event-formatter-arm.md`
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **curation-j4qqt** — Migrate and purge — two builder-triggered Administration gates: "Migrate to Notes" turns every content-block owner and game journal into one `Notes_saved` event (owners resolved by exact slug match, ambiguous/orphan ones reported, never guessed); "Purge legacy stores" bulk-deletes the `ContentBlocks-*` streams under ADR-0034 guardrails and drops both legacy tables; ContentBlocks, GameJournal, its boot migration and all content-block RPC members are deleted (ADR-0080, step 3 of 3) (feature) — `done/curation-j4qqt-migrate-content-blocks-and-game-journals-to-notes-and-purge.md`
- **curation-knqfj** — Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3) (feature) — `done/curation-knqfj-notes-editor-on-every-detail-page.md`
- **curation-w9fkq** — Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved) (feature) — `done/curation-w9fkq-backfill-media-type-on-legacy-catalog-entries-widen-catalog.md`
- **curation-cyxbc** — Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names (feature) — `done/curation-cyxbc-books-in-catalogs.md`
- **curation-h98ve** — Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3) (feature) — `done/curation-h98ve-notes-server-core.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **curation-kezpv** — Retire the one-off Notes migration tooling after the harbour cutover — delete Gate 1 "Migrate to Notes", Gate 2 "Purge legacy stores" (five `IAdminApi` members, DTOs, AdminSurgery sections, `ContentBlockConversion.fs` and tests) and the Health-tab catalog media-type backfill action, once both have run on the live store (chore) — `backlog/curation-kezpv-retire-notes-migration-tooling-after-harbour-cutover.md`
- **curation-h4k2p** — Clean up Notes-owned uploaded content images on media removal (parity with the deleted GameJournal.deleteForGame cleanup) (chore) — `backlog/curation-h4k2p-clean-up-notes-owned-uploaded-content-images-on-media-remova.md`
<!-- no tasks in backlog -->
<!-- backlog-list:end -->


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/curation/INDEX.md`
