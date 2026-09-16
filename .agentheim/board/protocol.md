# Protocol

Chronological log of everything that happens in this project.
Newest entries on top.

---

## 2026-09-16 19:54 -- Modeling / Promoted: curation-j4qqt - Migrate and purge — two builder-triggered Administration gates: "Migrate to Notes" turns every content-block owner and game journal into one `Notes_saved` event (owners resolved by exact slug match, ambiguous/orphan ones reported, never guessed); "Purge legacy stores" bulk-deletes the `ContentBlocks-*` streams under ADR-0034 guardrails and drops both legacy tables; ContentBlocks, GameJournal, its boot migration and all content-block RPC members are deleted (ADR-0080, step 3 of 3)

**Type:** Modeling / Promote
**BC:** curation
**From → To:** backlog → todo

---

## 2026-09-16 18:44 -- Work session ended

**Type:** Work / Session end
**Duration:** 1h15m
**Completed:** 4 (first-try PASS: 2, re-dispatched: 2, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Lost-result re-dispatches:** 0
**Lost-result escalations:** 0
**Dispatches:** journal-k52j1: 1 (iteration-2 PASS after the conductor applied the two non-bullet README edits on main, no worker re-dispatch), books-f3sb2: 1, curation-w9fkq: 2 (+1 ADR-0072 resolve-conflict dispatch after a real merge-back conflict with books-f3sb2 on CatalogProjectionTests.fs; rung-6 two-dot re-verify was the iteration-2 PASS), curation-knqfj: 1
**Commits:** 7 (2 batch-start, 4 integration, 1 session-end)
**Vision-conformance:** none — batch aligns with vision (vision.md carries no "What success looks like" / "Non-goals" sections; judged against Design Principles and the Books arc's "Later: reading activity in the Journal, books in catalogs" — both shipped this session; the Notes editor consolidation serves the Replayable principle per ADR-0043/0080; the media_type backfill repairs ADR-0079's deferred §5)
**Batch mix:** 75% product-facing / 25% harness / 0% bookkeeping (4 tasks) — the 25% is journal-k52j1, a README-only chore with zero source files, which the classifier cannot place as bookkeeping
**Carry-over:** none — working tree clean (5 stranded RESULT sidecars swept; `.agentheim/salvage/curation-w9fkq-merge-conflict.patch` kept, gitignored; all four worktrees torn down — the books-f3sb2 teardown at 18:01 ran before its node_modules junction was unlinked and emptied the main tree's node_modules, restored via npm ci at 18:15; full suite on main after the batch: build OK, Expecto 956/956, Vitest 108/108)

---

## 2026-09-16 18:40 -- Task verified and completed: curation-knqfj - Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)

**Type:** Work / Task completion
**Task:** curation-knqfj - Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)
**Summary:** Notes editor on every detail page — JournalEditor becomes NotesEditor keyed on (MediaType, slug) over getNotes/saveNotes, mounted as a plain panel on Movie/Series/Book detail and on the Game detail tab (relabelled Notes); ContentBlockEditor and its two StyleGuide specimens deleted, one NotesEditor.viewDemo specimen over a fixed sample document replaces them (ADR-0080, step 2 of 3)
**Duration:** 38m
**Verification:** PASS (iteration 1)
**Files changed:** 16
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-16 18:37 -- Task verified and completed: curation-w9fkq - Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved)

**Type:** Work / Task completion
**Task:** curation-w9fkq - Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved)
**Summary:** Backfill legacy catalog_entries media_type via a corrective per-catalog Entry_media_types_inferred event (exact-match resolution against all four *_list tables, ambiguous/orphan slugs reported by name and left untyped, one no-preview IAdminApi action on the Health tab), self-heal catalog_entries' UNIQUE to (catalog_slug, media_type, movie_slug) at every projection Init, and restore Add_entry to strict (MediaType, slug) pair identity via Seq.tryPick (ADR-0079 §5 resolved)
**Duration:** 1h08m
**Verification:** PASS (iteration 2 — iteration 1 failed on a missing checkProjectionDrift test and a missing formatCatalogEvent arm, both added; a real merge-back conflict with books-f3sb2 on CatalogProjectionTests.fs was resolved via the ADR-0072 ladder and the iteration-2 verdict is the rung-6 two-dot re-verify against main)
**Files changed:** 11
**Tests added:** 18
**ADRs written:** none

---

## 2026-09-16 18:16 -- Batch started: [curation-knqfj]

**Type:** Work / Batch start
**Tasks:** curation-knqfj - Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)
**Parallel:** yes (1 worker — second wave; curation-knqfj was held from wave 1 because it overlapped books-f3sb2 on all four detail-page Views.fs and Client.fsproj — books-f3sb2 is now integrated (d81eaab), so knqfj branches from a main that already carries the CatalogManager extraction; dispatched while curation-w9fkq's iteration-2 re-dispatch runs in its own worktree)

---

## 2026-09-16 18:14 -- Verification failed: curation-w9fkq - Backfill legacy catalog_entries media_type via corrective event, self-heal the widened UNIQUE at Init, restore Add_entry pair identity

**Type:** Work / Verification failure
**Task:** curation-w9fkq - Backfill legacy catalog_entries media_type via corrective event, self-heal the widened UNIQUE at Init, restore Add_entry pair identity
**Iteration:** 1 of 3
**Reasons:** the acceptance criterion's `checkProjectionDrift` zero-discrepancy conjunct (after backfill + rebuild) has no test; `Entry_media_types_inferred` is in `handledEventTypes` but has no `EventFormatting.formatCatalogEvent` arm (would surface in UnformattableEventTypes and render nothing in the stream drill-in); `npm run test:client` unverifiable at verification time because the conductor emptied the main tree's node_modules by removing a sibling worktree before unlinking its junction (restored via npm ci)
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-16 18:00 -- Task verified and completed: books-f3sb2 - Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types

**Type:** Work / Task completion
**Task:** books-f3sb2 - Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types
**Summary:** Book detail page joins catalogs — pill row + "Add to Catalog" picker via getCatalogsForBook / typed AddCatalogEntryRequest { MediaType = Book }, removeBook cascades the book's catalog entries like the other three media types, and the thrice-copied CatalogManager modal is one shared Components/CatalogManager.fs consumed by all four detail pages
**Duration:** 31m
**Verification:** PASS (iteration 1)
**Files changed:** 11
**Tests added:** 7
**ADRs written:** none

---

## 2026-09-16 17:44 -- Task verified and completed: journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language — a README-only change, because no live activity surface exists to attach code to (the All-tab heatmap/monthly payload was pruned, and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer)

**Type:** Work / Task completion
**Task:** journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language — a README-only change, because no live activity surface exists to attach code to (the All-tab heatmap/monthly payload was pruned, and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer)
**Summary:** Reading day and Book finished join the Journal's ubiquitous language — README-only: two UL bullets, Books' three subscribed events in Key events, Books in Purpose/Aggregates/Downstream-of, two open questions recorded; no source or test file touched
**Duration:** 17m
**Verification:** PASS (iteration 2 — iteration 1 failed only on the bullet-only README_DELTA grammar; the conductor applied the two Purpose/Aggregates prose edits from the worker's Outcome on main per the task's Notes, no worker re-dispatch)
**Files changed:** 0
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-16 17:41 -- Verification failed: journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language (README-only)

**Type:** Work / Verification failure
**Task:** journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language (README-only)
**Iteration:** 1 of 3
**Reasons:** acceptance criterion 3's Purpose and Aggregates prose edits are unreachable by the bullet-only README_DELTA grammar (reported in the worker's Outcome as conductor hand-edits, not carried by any op); bullets append after Monthly breakdown rather than after Play session (positional insert not expressible)
**Iteration hint:** task-under-specified
**Next:** conductor applied the delta ops plus the two prose edits on main (the task's Notes assign README application to the conductor; curation-cyxbc precedent), then re-verified as iteration 2 without a worker re-dispatch

---

## 2026-09-16 17:29 -- Batch started: [books-f3sb2, curation-w9fkq, journal-k52j1]

**Type:** Work / Batch start
**Tasks:** books-f3sb2 - Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types, curation-w9fkq - Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved), journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language — a README-only change, because no live activity surface exists to attach code to (the All-tab heatmap/monthly payload was pruned, and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer)
**Parallel:** yes (3 workers — 4 ready; curation-knqfj held to next wave: cap 3, and it overlaps books-f3sb2 on all four detail-page Views.fs and Client.fsproj, so it merges after f3sb2)

---

## 2026-09-16 17:09 -- Modeling / Promoted: curation-w9fkq - Backfill legacy `catalog_entries` rows with an exact-match-resolved `media_type` via a corrective `Entry_media_types_inferred` event (ambiguous/orphan slugs reported by name, never guessed), self-heal `catalog_entries`' UNIQUE to `(catalog_slug, media_type, movie_slug)` at projection Init, and restore `Add_entry` to strict `(MediaType, slug)` pair identity (ADR-0079 §5 resolved)

**Type:** Modeling / Promote
**BC:** curation
**From → To:** backlog → todo

---

## 2026-09-16 17:09 -- Modeling / Refined: curation-w9fkq - Backfill legacy catalog_entries media_type via corrective event, self-heal the widened UNIQUE at Init, restore Add_entry pair identity

**Type:** Modeling / Refine
**BC:** curation
**Status after:** todo
**Summary:** ADR-0079 §5's open backfill shape resolved (tactical-modeler + architect via orchestrator): a corrective `Entry_media_types_inferred` event per catalog, appended through ADR-0032's compensating path by a single no-preview admin action; resolution is exact-match against all four `*_list` tables with ambiguous/orphan slugs reported by name, never the read-time join-order guess written back (that guess never consults game_list/book_list and a frozen wrong type would drop a row out of its type-scoped lookup and removal cascade). The widened UNIQUE self-heals at projection Init (CREATE TABLE IF NOT EXISTS would otherwise leave the narrow index live until a manual Rebuild-all while the relaxed decide is already live). Add_entry restored to pair identity with Seq.tryPick (tryHead is a latent bug once same-slug entries coexist). Acceptance criteria rewritten as Expecto cases plus one human-eye UI check; README delta drafted in Notes; styleguide gate added for the admin button.
**Split into:** none
**ADRs written:** ADR-0079 amendment (2026-09-16, curation-w9fkq)

---

## 2026-09-16 17:04 -- Modeling / Promoted: journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language — a README-only change, because no live activity surface exists to attach code to (the All-tab heatmap/monthly payload was pruned, and `DashboardCrossMediaStats` / `getRecentActivity` are confirmed-dead payload with no client consumer)

**Type:** Modeling / Promote
**BC:** journal
**From → To:** backlog → todo

---

## 2026-09-16 17:04 -- Modeling / Refined: journal-k52j1 - Reading day and Book finished join the Journal's ubiquitous language (README-only)

**Type:** Modeling / Refine
**BC:** journal
**Status after:** todo
**Summary:** Premise corrected: besides the heatmap/monthly payload pruned by intelligence-h4qk2, `DashboardCrossMediaStats` and `getRecentActivity`/`RecentActivityItem` turned out to have zero client consumers, so no live activity surface exists to wire reading into. Task reduced to a README-only chore (Reading day + Book finished in the ubiquitous language, Books in the subscribed-events list, Purpose/Aggregates/Relationships extended, two open questions recorded). Dropped: recent-activity entries, monthly breakdown Reading series, cross-media stats fields, Recently Read rail (covered by intelligence-dnv2y). Dependencies cleared (all done; no client change, so no styleguide gate). Follow-up candidate noted, not captured: prune the dead cross-media stats / recent-activity payload in intelligence/.
**Split into:** none
**ADRs written:** none

---

## 2026-09-16 16:55 -- Modeling / Promoted: books-f3sb2 - Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types

**Type:** Modeling / Promote
**BC:** books
**From → To:** backlog → todo

---

## 2026-09-16 16:55 -- Modeling / Promoted: curation-knqfj - Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)

**Type:** Modeling / Promote
**BC:** curation
**From → To:** backlog → todo

---

## 2026-09-16 14:36 -- Work session ended

**Type:** Work / Session end
**Duration:** 44m
**Completed:** 3 (first-try PASS: 3, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Lost-result re-dispatches:** 0
**Lost-result escalations:** 0
**Dispatches:** intelligence-h4qk2: 1, curation-h98ve: 1, curation-cyxbc: 1
**Commits:** 4
**Vision-conformance:** none — batch aligns with vision (vision.md carries no "What success looks like" / "Non-goals" sections; judged against Design Principles and Out of Scope (v1) — the heatmap prune serves the intent-driven All tab, Notes event-sources the user's own writing per ADR-0043, typed entries fix games in catalogs)
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (3 tasks)
**Carry-over:** none — working tree clean (3 stranded RESULT sidecars swept; `.agentheim/state/whats-next.md` rewritten at session start with the Audible-fix churn advisory, gitignored)

---

## 2026-09-16 14:32 -- Task verified and completed: curation-cyxbc - Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names

**Type:** Work / Task completion
**Task:** curation-cyxbc - Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names
**Summary:** Typed catalog entries (ADR-0079) — Entry_added carries MediaType, legacy entries stay untyped with read-time inference as the NULL fallback, the projection resolves all four media types (fixing games), getCatalogsForBook plus type-filtered lookups and removal cascade, Shared DTOs lose their Movie* names; Add_entry rejects any same-slug add per catalog until the UNIQUE widens (ADR-0079 amendment, follow-up curation-w9fkq)
**Duration:** 38m
**Verification:** PASS (iteration 1)
**Files changed:** 13
**Tests added:** 11
**ADRs written:** none

---

## 2026-09-16 14:28 -- Task verified and completed: curation-h98ve - Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)

**Type:** Work / Task completion
**Task:** curation-h98ve - Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)
**Summary:** Notes server core (ADR-0080 step 1) — Notes.fs Notes_saved snapshot stream keyed by (MediaType, slug), notes_blocks projection, getNotes/saveNotes on IMediathecaApi, HasNotesContent on all four detail DTOs replacing GameDetail.HasJournalContent, registered in every Administration registry; ContentBlocks and GameJournal untouched
**Duration:** 34m
**Verification:** PASS (iteration 1)
**Files changed:** 49
**Tests added:** 19
**ADRs written:** none

---

## 2026-09-16 14:08 -- Task verified and completed: intelligence-h4qk2 - Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them

**Type:** Work / Task completion
**Task:** intelligence-h4qk2 - Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them
**Summary:** Pruned the dead 365-day activity-heatmap / 12-month monthly-breakdown payload end to end — the two Shared types, the two DashboardAllTab fields, the two computation blocks in Api.getDashboardAllTab, all seven feeder queries and the two tests that existed only to exercise them
**Duration:** 13m
**Verification:** PASS (iteration 1)
**Files changed:** 8
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-16 13:54 -- Batch started: [intelligence-h4qk2, curation-cyxbc, curation-h98ve]

**Type:** Work / Batch start
**Tasks:** intelligence-h4qk2 - Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them, curation-cyxbc - Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names, curation-h98ve - Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)
**Parallel:** yes (3 workers — the whole ready set; all three touch src/Shared/Shared.fs and src/Server/Api.fs, so squash-merges are ordered sequentially: intelligence-h4qk2 (smallest, pure prune) first, then curation-cyxbc, then curation-h98ve)

---

## 2026-09-16 13:22 -- Modeling / Captured: curation-j4qqt - Migrate and purge — two builder-triggered Administration gates: "Migrate to Notes" turns every content-block owner and game journal into one `Notes_saved` event (owners resolved by exact slug match, ambiguous/orphan ones reported, never guessed); "Purge legacy stores" bulk-deletes the `ContentBlocks-*` streams under ADR-0034 guardrails and drops both legacy tables; ContentBlocks, GameJournal, its boot migration and all content-block RPC members are deleted (ADR-0080, step 3 of 3)

**Type:** Modeling / Capture
**BC:** curation
**Filed to:** backlog
**Summary:** Step 3 of 3 of ADR-0080: two builder-triggered Administration gates — Migrate to Notes (one `Notes_saved` per resolved legacy owner, ambiguous/orphan slugs reported never guessed, games sourced from game_journal_blocks directly) and Purge legacy stores (bulk stream delete under ADR-0034 guardrails, both legacy tables dropped) — then ContentBlocks, GameJournal, the Composition.fs boot migration and every content-block RPC member are deleted. Builder chose purge over keeping the events as history. Parked in backlog until both parents are done.

---

## 2026-09-16 13:22 -- Modeling / Captured: curation-knqfj - Notes editor on every detail page — `JournalEditor` becomes `NotesEditor` taking `(MediaType, slug)` over `getNotes`/`saveNotes`, mounted where `ContentBlockEditor` sits on Movie/Series/Book detail and on the Game detail tab (label "Notes"); `ContentBlockEditor` and its StyleGuide specimens go, one NotesEditor specimen replaces them (ADR-0080, step 2 of 3)

**Type:** Modeling / Capture
**BC:** curation
**Filed to:** backlog
**Summary:** Step 2 of 3 of ADR-0080: JournalEditor becomes NotesEditor over `(MediaType, slug)`, mounted on Movie/Series/Book detail where ContentBlockEditor sits and on the Game detail tab (label "Notes"); ContentBlockEditor and its StyleGuide specimens retire, one NotesEditor specimen replaces them. Fully refined; parked in backlog only because the promote gate needs curation-h98ve in done/.

---

## 2026-09-16 13:22 -- Modeling / Captured: curation-h98ve - Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)

**Type:** Modeling / Capture
**BC:** curation
**Filed to:** todo
**Summary:** Step 1 of 3 of ADR-0080's Notes unification: the event-sourced Notes document stream (one `Notes_saved` snapshot per debounced save, owner `(MediaType, slug)`), `notes_blocks` projection, `getNotes`/`saveNotes`, `HasNotesContent` on all four detail DTOs, full Administration registry wiring. Strictly additive — ContentBlocks and GameJournal untouched until curation-j4qqt. Refined via the orchestrator (tactical-modeler + architect) and filed straight to todo.

---

## 2026-09-16 13:05 -- Modeling / Dismissed: books-n8fpz

**Type:** Modeling / Dismiss
**Dismissed:**
- books-n8fpz - Collapse the three per-type content-block method families (Books, Series, Games) into the one generic IMediathecaApi family — they are byte-identical copies over the single ContentBlocks aggregate; BookDetail and SeriesDetail call the generic methods for all seven operations, and the never-called Games family goes (books)

---

## 2026-09-16 12:54 -- Modeling / Promoted: curation-cyxbc - Typed catalog entries — an entry references `(MediaType, slug)` (ADR-0079): `Entry_added` carries the media type, legacy entries stay untyped and fall back to today's read-time inference, the projection resolves all four media types (fixing games, which render as bare slugs today), `getCatalogsForBook`, type-filtered lookups and removal cascade, and the Shared vocabulary loses its `Movie*` names

**Type:** Modeling / Promote
**BC:** curation
**From → To:** backlog → todo

---

## 2026-09-16 12:53 -- Modeling / Refined: curation-cyxbc - Typed catalog entries (was: Books in catalogs)

**Type:** Modeling / Refine
**BC:** curation
**Status after:** todo
**Summary:** Code-fact check found the catalog entry key is an untyped slug resolved by join order, that games are already broken in catalog detail (`getEntries` never joins `game_list`: raw slug, year 0, no cover, `/movies/` link), that `getCatalogsForGame` is literally the movie lookup, that book removal has no catalog cascade, and that every media type slugs as `slugify(title)-year` so cross-type collisions are refused and the slug-only removal cascade would cross-delete. Builder chose **typed entries** over keeping the untyped slug or renaming DTO fields only ("I am all about specific ubiquitous language in this bounded context") and folded the games fix in. Orchestrator → tactical-modeler settled the shape: `MediaType option` on `Entry_added`/`EntryState` (None only on legacy replays, key omitted when None), a conservative duplicate rule (legacy on either side → slug alone rejects, because the projection's UNIQUE stays), `media_type TEXT NULL` via the idempotent ALTER TABLE guard, read-time join-order inference kept only as the NULL fallback, type filters on `getCatalogsFor*` / the series-children lookup / `getEntriesByMediaSlug`, `RoutePrefix` replaced by `MediaType` + a Shared `MediaType.routePrefix` helper, Shared DTOs renamed (`MediaSlug`/`Title`/`Year`/`PosterRef`), stored JSON key and column kept. Task rewritten with machine-checkable criteria (Expecto serialization + duplicate rule, projection query tests per media type incl. the NULL fallback and a slug-collision case, typed cascade). depends_on gains the done styleguide task; `blocks: [books-f3sb2]`. Follow-ups noted, not captured: widening UNIQUE + strict pair identity after a media_type backfill (ADR-0079 §5); `EventFormatting.crossLinkFields` mislinking non-movie catalog entries (pre-existing).
**Split into:** books-f3sb2 (book detail page catalog pill row + picker with `CatalogManager` extracted into `Components/` for all four pages, and `removeBook` cascading catalog entries; depends_on curation-cyxbc)
**ADRs written:** 0079 (catalog entries typed by MediaType; legacy entries untyped, resolved at read time; stored shapes kept)

---

## 2026-09-16 12:53 -- Modeling / Captured: books-f3sb2 - Book detail page joins catalogs — the catalog pill row + "Add to Catalog" picker on `/books/{slug}` via `getCatalogsForBook` (sending `MediaType.Book`), with the thrice-copied `CatalogManager` modal extracted into `Components/` and consumed by all four detail pages, and `removeBook` cascading the book's catalog entries like the other media types

**Type:** Modeling / Capture
**BC:** books
**Filed to:** backlog
**Summary:** Split out of curation-cyxbc during its 2026-09-16 refinement: the book detail page's catalog pill row + picker (extracting the thrice-copied CatalogManager modal into Components/ with BookDetail as its fourth consumer) and removeBook cascading the book's catalog entries through the type-filtered lookup curation-cyxbc introduces (ADR-0079). depends_on curation-cyxbc.

---

## 2026-09-16 12:47 -- Modeling / Promoted: books-n8fpz - Collapse the three per-type content-block method families (Books, Series, Games) into the one generic IMediathecaApi family — they are byte-identical copies over the single ContentBlocks aggregate; BookDetail and SeriesDetail call the generic methods for all seven operations, and the never-called Games family goes

**Type:** Modeling / Promote
**BC:** books
**From → To:** backlog → todo

---

## 2026-09-16 12:47 -- Modeling / Refined: books-n8fpz - Collapse the three per-type content-block method families (Books, Series, Games) into the one generic IMediathecaApi family

**Type:** Modeling / Refine
**BC:** books
**Status after:** backlog
**Summary:** Code-fact check plus one builder decision. The capture's premise (Books' family is uniquely incomplete, so its fallback to the generic methods is fragile) does not hold: Series has the identical 3-of-7 split, the Games family has no caller at all (GameDetail uses the Game Journal), the three `get*ContentBlocks` variants have no client caller (detail DTOs embed the blocks), and every per-type `Api.fs` body is a byte-identical copy of the generic one with `sessionId = None`. Events, aggregate, stream id and projection are already single. Builder's stated goal — one set of events with one behavior regardless of owning entity — is already true at the event level; the duplication is only the RPC surface. Builder chose **remove all three families** over completing the Book family, a Books-only removal, or an owner-kind key. Task rewritten from a two-way judgment call (`chore`) into a prescribed behavior-preserving `refactor`: twelve `IMediathecaApi` members and their `Api.fs` bodies go, BookDetail and SeriesDetail call the generic family for all seven operations, one Expecto test proves the generic path serves a book and a series slug end to end, no diff on ContentBlocks.fs/ContentBlockProjection.fs/ContentBlocksTests.fs, one books README bullet. Latent cross-media slug collision recorded as out of scope (owner identity, Curation, needs a live-data migration). prior_art set to books-y9kxy, books-f33e2. No orchestrator round — the facts were fully settled by reading the code.
**Split into:** none
**ADRs written:** none

---

## 2026-09-16 12:32 -- Modeling / Promoted: intelligence-h4qk2 - Prune the dead activity-heatmap payload — DashboardAllTab.ActivityDays/MonthlyBreakdown, their two Shared types and the seven daily/monthly feeder queries go end to end (mirroring intelligence-p4t7k); the All tab stopped rendering them in intelligence-dq8rk and no client reads them

**Type:** Modeling / Promote
**BC:** intelligence
**From → To:** backlog → todo

---

## 2026-09-16 12:32 -- Modeling / Refined: intelligence-h4qk2 - Prune the dead activity-heatmap payload (DashboardAllTab.ActivityDays/MonthlyBreakdown) end to end

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** todo
**Summary:** Code-fact check plus one builder decision. The heatmap + monthly breakdown were real All-tab UI (archive tasks 027/036) that `intelligence-dq8rk` deliberately removed and `intelligence-wecjh` deleted the views for; `intelligence-p4t7k` pruned only the sibling `NewGames` payload, and `intelligence-dnv2y` then added a `Reading` field to the still-dead `ActivityDays` on a false premise. Builder chose **prune, not rebuild** (no Journal page exists, vision lists Journal reading activity as "Later", `journal-k52j1` is itself backlog). Task rewritten from a build-or-prune judgment call (`chore`) into a prescribed end-to-end prune (`refactor`) mirroring p4t7k: two Shared types, two `DashboardAllTab` fields, the `getDashboardAllTab` blocks, the seven feeder queries (`getDaily*Activity` x4, `getMonthly*Minutes` x3), the two tests that exist only for them, and README code-claims retired without deleting any ubiquitous-language term; no client edits, Fable compile is the proof. `journal-k52j1`'s false "dnv2y already puts reading days on the heatmap" premise corrected in its Why/Notes and it now `depends_on` intelligence-h4qk2 (reverse `blocks` edge set). prior_art widened to p4t7k, wecjh, dq8rk, dnv2y.
**Split into:** none
**ADRs written:** none

---

## 2026-09-16 06:27 -- Work session ended

**Type:** Work / Session end
**Duration:** 4h07m (first "Batch started" 02:16 → 06:23)
**Completed:** 9 (first-try PASS: 6, re-dispatched: 3, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Lost-result re-dispatches:** 0
**Lost-result escalations:** 0
**Dispatches:** books-y9kxy: 3, integration-c8d4x: 2, integration-dhctm: 1, books-f33e2: 1, integration-wmqn3: 1, books-g7g1j: 1, intelligence-dnv2y: 1, integration-y2ak4: 1, integration-jjvg2: 2
**Commits:** 19 (6 batch starts, 9 task integrations, 1 README reconcile, 1 merge-back arity reconcile, this entry) — plus the modeling/research commits earlier this session (2 research, 1 BC creation, 11 captures, 1 refine)
**Vision-conformance:** none — batch aligns with vision. All nine tasks serve the "Books — Audible + Goodreads" workstream modeling pulled into v1 this session (vision.md updated in the same session: Books left "Out of Scope (v1)", gained an In Focus section, a dashboard tab and a Remaining-v1-Work entry). vision.md still has no "What success looks like"/"Non-goals" headings — `extractVisionSections` returns two empty lists, so the judgement was made against the Books section, "Unified Dashboard" and "Design Principles" (Replayable: every progress observation is an event, ADR-0076; Intent-driven: In Focus books on the All tab).
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (9 tasks)
**Carry-over:** none — working tree clean, no registered worktrees remain; 13 stranded RESULT sidecars (one per iteration) swept from `.worktrees/.results/` and the empty `.worktrees/` removed.

**Result source (all nine tasks, ADR-0080 §6):** sidecar · layout both+sentinel · sidecar present, every iteration — `selectResultSource` picked the sidecar first-try each time; two transcript copies were truncated by the harness (wmqn3's to its last line, f33e2's from the top) and the sidecar rescued both, no repairs.

**Session-start churn note:** 0 recognized machine-shape commits flagged for governed surfaces; the 15 commits between the 2026-09-14 16:13 boundary and this session's first batch were this session's own modeling/research bookkeeping (2 research report commits, 1 `model(books): create` BC-creation commit touching vision.md/context-map.md deliberately, 11 captures, 1 refine) — known cause, no re-alignment task; `whats-next.md` not written.

**Verification history:** books-y9kxy PASS iteration 3 (iter 1: missing finished_at-default test, drift-zero stream lacked the status event, README Status bullet stale; iter 2: Books missing from `handledEventTypesByBoundedContext` so the Health tab would list every Book event as unhandled — fixed with a registry-completeness guard). integration-c8d4x PASS iteration 2 (iter 1: the import's cover download bypassed `OpenLibrary.downloadCover` — no User-Agent, no covers throttle, dead code — and the OLID `edition_key` was fed to the ISBN endpoint). integration-jjvg2 PASS iteration 2 (iter 1: both sync paths cleared `audible_last_error` on an empty library response against ADR-0068, and the import stamped `ObservedOn` in UTC). dhctm, f33e2, g7g1j, wmqn3, dnv2y, y2ak4 PASS iteration 1. **Integrated main, full suite:** the first run after the last squash FAILED to build the test project — `DashboardBooksTests.fs` (new in intelligence-dnv2y, forked before wmqn3/jjvg2 added three `Api.create` parameters) still called the old arity; git had auto-merged every sequential squash without a textual conflict, so no ladder rung fired and each worktree was green in isolation. Reconciled on main by the conductor (three stub arguments, the same every other test call site carries, its own scoped commit), then re-run: build clean, Expecto 906/906, Vitest 102/102 (15 files).

**Builder checks pending ([human-eye] criteria):** the Audible and Goodreads Settings cards read as one of the existing integration cards; the search modal's Books tab feels identical in rhythm to the Games tab with matching source badges; the book detail page reads as the movie page's family with the progress bar as its one extra card; the All-tab Reading card sits beside Games with the same poster cap, progress bars legible at rail size, the Finished pill matching Watched/Retired. Also worth a glance after deploy: paste the `audible-cli` auth file and the Goodreads user id in Settings, then "Import library" / "Sync now" — the first real feeds may surface parser gaps the pinned fixtures could not (German-locale Goodreads status text, an Audible marketplace other than `de`).

**Backlog items filed by workers:** books-n8fpz (Books' content-block method family is missing four operations — BookDetail falls back to the generic bare-slug methods), intelligence-h4qk2 (DashboardAllTab's ActivityDays/MonthlyBreakdown payload has no client consumer — build the heatmap or prune it). Plus the two modeling captured: journal-k52j1, curation-cyxbc.

**Harness notes:** the plugin cache `0.9.6/lib/` is complete; every verb ran from it. Conductor helpers lived in the scratchpad as `.mjs` files written with the Write tool (a Bash heredoc collapses backslashes — three bites, now in memory): `wt.mjs` (worktree + node_modules junction + sidecar clear; `cmd /c rmdir` on the junction before `git worktree remove`, main copy verified intact each time), `assemble.mjs` (full worker prompt into one file, handed by path), `result.mjs` (source ladder → blocks on disk), `assemble-verifier.mjs` (diff against the branch's merge-base, not `main..HEAD` — the latter picked up the reverse of a sibling's merge once main moved on), `integrate.mjs` (squash → README delta with a leading-dash normaliser → ADRs + finalize → outcome → complete → backlog capture → index-add → one scoped commit). One README delta on books-y9kxy disposed `merged` because the worker's `expected` omitted the bullet dash; reconciled by hand in its own scoped commit, and the normaliser prevented a repeat.

---

## 2026-09-16 06:23 -- Task verified and completed: integration-jjvg2 - Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice

**Type:** Work / Task completion
**Task:** integration-jjvg2 - Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice
**Summary:** Audible library import (importAudibleLibrary creates a Book per unmatched ASIN from the paged /1.0/library response's own fields, Audnexus filling description/narrators only when thin) and a daily "Audible progress sync" scheduled job (AudibleSync.runProgressSync, matched-by-ASIN books only, never creates one), both funnelling through the pure AudibleSync.percentOf / observationFor (floor, never round; is_finished with no percent → 100; local calendar date) into Observe_reading_progress; a rejected auth file resolves the job run to error with the fixed notice; an empty library response is inconclusive and never clears the standing notice (ADR-0068); Settings shows Import library / Sync progress now with persisted last-import/last-sync via getAudibleSyncStatus. Iteration 2 closed the verifier's two findings.
**Duration:** 44m
**Verification:** PASS (iteration 2)
**Files changed:** 27
**Tests added:** 17
**ADRs written:** none

---

## 2026-09-16 06:09 -- Verification failed: integration-jjvg2 - Audible library import and daily listening-progress sync

**Type:** Work / Verification failure
**Task:** integration-jjvg2 - Audible library import and daily listening-progress sync
**Iteration:** 1 of 3
**Reasons:** both the import and the sync clear audible_last_error on an empty library response (ADR-0068: empty is inconclusive, never clears the standing notice), and the import stamps ObservedOn in UTC while the sync uses the local calendar date
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-16 05:56 -- Task verified and completed: integration-y2ak4 - Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs

**Type:** Work / Task completion
**Task:** integration-y2ak4 - Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs
**Summary:** Goodreads reading progress from the public user-status feed: Goodreads.getStatusUpdates and the pure parseProgress recognize page-N-of-M / N%-done / finished / started items (HTML-entity-decoded, case/whitespace tolerant), and GoodreadsSync's new progress step joins them by normalized title (currently-reading shelf items resolved this run, then every library book; any ambiguity is Unmatched, never a guess), floors page/total to a percent, processes oldest-first, emits Observe_reading_progress (source Goodreads) and persists goodreads_last_status_id/at for idempotent re-runs, inside the same "Goodreads shelf sync" job with new Progress counts on its result.
**Duration:** 24m
**Verification:** PASS (iteration 1)
**Files changed:** 7
**Tests added:** 18
**ADRs written:** none

---

## 2026-09-16 05:37 -- Task verified and completed: intelligence-dnv2y - Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap

**Type:** Work / Task completion
**Task:** intelligence-dnv2y - Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap
**Summary:** Dashboard Books presence: the All tab's "Books coming soon" placeholder becomes a Reading card (In Focus books with progress bars and source glyphs, 7-day finished linger via date-string comparison per ADR-0077, expand-in-place keyed on slug) and a new Books tab with Currently Reading / Recently Finished / Recently Added rails plus a stats tile row (Total, In Focus, Finished this year / all time, Pages read, Hours listened), backed by DashboardBookItem / DashboardBooksTab / DashboardBookStats DTOs, new BookProjection queries, four DashboardCardQuery cases and DashboardActivityDay.Reading. The heatmap tooltip line was not built because no client heatmap exists (backlog item filed).
**Duration:** 34m
**Verification:** PASS (iteration 1)
**Files changed:** 10
**Tests added:** 7
**ADRs written:** none

---

## 2026-09-16 05:32 -- Batch started: [integration-jjvg2, integration-y2ak4]

**Type:** Work / Batch start
**Tasks:** integration-jjvg2 - Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice, integration-y2ak4 - Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs
**Parallel:** yes (2 workers — integration-jjvg2 and integration-y2ak4 both became ready when integration-wmqn3 integrated; they are the last two todo tasks. intelligence-dnv2y's verifier is still running in its own worktree. Merge order if both pass: y2ak4 (extends GoodreadsSync's job body) then jjvg2 (appends a second JobSpec + Api/Shared tail).)

---

## 2026-09-16 05:32 -- Task verified and completed: integration-wmqn3 - Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN

**Type:** Work / Task completion
**Task:** integration-wmqn3 - Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN
**Summary:** Goodreads adapter (Goodreads.fs: parseUserId, throttled public review/list_rss shelf-feed fetch/parse with ProfilePrivateOrUnknown / FeedUnavailable / ParseFailed) plus the daily shelf sync (GoodreadsSync.fs: match by Goodreads id / ISBN-13 / ISBN-10, import unmatched currently-reading items via Open Library or the feed's own data, adapter-owned never-demote status mapping with ADR-0077 backdating of read dates, one-time rating seeding, idempotent replay), a Goodreads Settings card (user id or profile URL, Test, shelf opt-ins, Sync now, standing notice) and a "Goodreads shelf sync" scheduled job whose manual trigger shares the ADR-0026 job-run recorder (ADR-0078).
**Duration:** 42m
**Verification:** PASS (iteration 1)
**Files changed:** 27
**Tests added:** 16
**ADRs written:** 0078-goodreads-manual-sync-shares-job-run-recorder-via-wrapper-jobspec.md

---

## 2026-09-16 05:24 -- Task verified and completed: books-g7g1j - Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route

**Type:** Work / Task completion
**Task:** books-g7g1j - Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route
**Summary:** Search modal Books tab: Open Library and Audible source checkboxes (both on by default, the games-k3vps toggle shape), a merged cover grid with source badges (Audible cards show narrator + runtime), a version-guarded applyBooksSearchResults reducer seam, the duplicate-prompt flow generalized to book imports via pendingImportMediaType / forceDuplicateImport (also fixing the prompt's previously hardcoded Game navigation), and navigation to /books/{slug} on import; the route itself was inherited from books-f33e2.
**Duration:** 26m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 9
**ADRs written:** none

---

## 2026-09-16 04:58 -- Batch started: [books-g7g1j, intelligence-dnv2y]

**Type:** Work / Batch start
**Tasks:** books-g7g1j - Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route, intelligence-dnv2y - Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap
**Parallel:** yes (3 workers — books-g7g1j and intelligence-dnv2y became ready when books-f33e2 integrated and join the still-running integration-wmqn3; integration-jjvg2 and integration-y2ak4 stay blocked on wmqn3. Merge order if all pass: wmqn3 (Shared/Api tail), then dnv2y, then g7g1j.)

---

## 2026-09-16 04:58 -- Task verified and completed: books-f33e2 - Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove

**Type:** Work / Task completion
**Task:** books-f33e2 - Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove
**Summary:** Book detail page at /books/{slug} mirroring MovieDetail: cover hero with format badge, length line and series line, the Games-shaped status control, a reading-progress card with a paper-overlay Update-progress popover (percent or page/total via the pure Progress.buildProgressRequest seam) and a removable history list, personal rating, details/links cards, recommended-by friends, content blocks, event history and remove — wired through Router.fs's new Book_detail page and the root MVU delegation, with a Playwright spec proving 100 % finishes and removal re-derives.
**Duration:** 44m
**Verification:** PASS (iteration 1)
**Files changed:** 15
**Tests added:** 15
**ADRs written:** none

---

## 2026-09-16 04:45 -- Batch started: [integration-wmqn3]

**Type:** Work / Batch start
**Tasks:** integration-wmqn3 - Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN
**Parallel:** yes (adds 1 worker beside the still-running books-f33e2 — integration-wmqn3 became ready when integration-dhctm integrated; books-g7g1j and intelligence-dnv2y stay blocked on books-f33e2)

---

## 2026-09-16 04:45 -- Task verified and completed: integration-dhctm - Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible`

**Type:** Work / Task completion
**Task:** integration-dhctm - Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible`
**Summary:** Audible adapter (Audible.fs + Audnexus module) on ADR-0074's imported-auth-file contract — validateAuthFile, locale→host mapping, refreshAccessToken via POST /auth/token, withAccessToken cache/proactive-refresh/retry-once with the fixed "audible auth file rejected: " notice, and no code path that logs in or registers a device — plus unauthenticated catalog search and product detail with Audnexus fallback, the full IMediathecaApi surface (getAudibleStatus never leaks the file, set/clear auth file, test connection, marketplace, searchAudibleBooks, addBookFromAudible) and an Audible Settings card after qBittorrent.
**Duration:** 31m
**Verification:** PASS (iteration 1)
**Files changed:** 25
**Tests added:** 31
**ADRs written:** none

---

## 2026-09-16 04:13 -- Batch started: [integration-dhctm, books-f33e2]

**Type:** Work / Batch start
**Tasks:** integration-dhctm - Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible`, books-f33e2 - Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove
**Parallel:** yes (2 workers — integration-dhctm and books-f33e2 are the whole ready set; the orchestrator's pre-scan found no shared source regions between them. Merge order if both pass: dhctm first (Shared/Api tail-append), then f33e2.)

---

## 2026-09-16 04:13 -- Task verified and completed: integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled

**Type:** Work / Task completion
**Task:** integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled
**Summary:** Open Library adapter (OpenLibrary.fs): keyword search, work / ISBN-edition / OLID-edition lookup with best-effort author resolution, cover download, two independent adapter-owned throttles (1s API, 3s covers) and an identifying User-Agent on every request including covers; searchOpenLibraryBooks, addBookFromOpenLibrary (shape-dispatched edition lookup, explicit Isbn13 field, cover routed through downloadCover) and refreshBookFromOpenLibrary (cache slice only) on IMediathecaApi, threaded through Composition and every Api.create test call site. Iteration 2 closed the verifier's two findings (cover download bypassing the adapter; OLID edition_key fed to the ISBN endpoint).
**Duration:** 43m
**Verification:** PASS (iteration 2)
**Files changed:** 19
**Tests added:** 12
**ADRs written:** none

---

## 2026-09-16 03:57 -- Verification failed: integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned throttle with an identifying User-Agent

**Type:** Work / Verification failure
**Task:** integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned throttle with an identifying User-Agent
**Iteration:** 1 of 3
**Reasons:** the import's cover download bypasses OpenLibrary.downloadCover (no User-Agent, no covers throttle; downloadCover is dead code), and the edition lookup feeds the search result's OLID edition_key into /isbn/{isbn}.json so a real search hit would 404 and create a book titled by its work key
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-16 03:30 -- Batch started: [integration-c8d4x]

**Type:** Work / Batch start
**Tasks:** integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled
**Parallel:** no (1 worker — integration-c8d4x is the only ready task now that books-y9kxy is done; integration-dhctm and books-f33e2 both depend on c8d4x's Shared/Api/Composition tail-append landing first, per the orchestrator's ordering)

---

## 2026-09-16 03:29 -- Task verified and completed: books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared

**Type:** Work / Task completion
**Task:** books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared
**Summary:** Built the Book aggregate (Books.fs), BookProjection (book_list/book_detail/book_progress), the book_metadata_cache slice, every Administration registry entry (incl. handledEventTypesByBoundedContext with a registry-completeness guard), and the full Remoting API for books plus a fourth content-blocks method family, threading MediaType.Book through Shared and the client's search/navigation with zero incomplete-match warnings. Verifier iterations 1–2 added the finished_at-default test, the status event in the drift-zero rebuild, the README Status bullet correction, and the Health-tab unknown-event registration.
**Duration:** 1h14m
**Verification:** PASS (iteration 3)
**Files changed:** 21
**Tests added:** 36
**ADRs written:** none

---

## 2026-09-16 03:15 -- Verification failed: books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC

**Type:** Work / Verification failure
**Task:** books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC
**Iteration:** 2 of 3
**Reasons:** Administration.handledEventTypesByBoundedContext has no Books entry, so the Health tab's unknown-event report would list every Book event type as unhandled (criterion 4); the iteration-1 findings are closed
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-16 03:02 -- Verification failed: books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC

**Type:** Work / Verification failure
**Task:** books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC
**Iteration:** 1 of 3
**Reasons:** no test for the projection's finished_at default when effectiveOn is None, the drift-zero rebuild test omits the Book_status_changed event the criterion names, README "Status" bullet still says any non-InFocus status promotes (aggregate excludes Finished)
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-16 02:16 -- Batch started: [books-y9kxy]

**Type:** Work / Batch start
**Tasks:** books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared
**Parallel:** no (1 worker — books-y9kxy is the only ready task; the other eight Books tasks all depend on it, directly or transitively)

---

## 2026-09-16 02:15 -- Modeling / Refined: Books integration task set (9 todo tasks) — orchestrator pass before work

**Type:** Modeling / Refine
**BC:** books, integration, intelligence
**Status after:** todo (all nine unchanged)
**Summary:** Orchestrator (tactical-modeler + architect) refined books-y9kxy, integration-c8d4x, integration-dhctm, integration-jjvg2, integration-wmqn3, integration-y2ak4, books-g7g1j, books-f33e2, intelligence-dnv2y in place against the actual code. Found and fixed: a guaranteed Router.fs duplicate-DU-case collision (g7g1j now depends on f33e2, which owns the route); a wrong ContentBlocks "owner key" assumption (no such key exists — a fourth parallel IMediathecaApi method family, as Series/Games do); JournalEditor is game-specific (skipped for books); tail-append collisions in Shared/Api/Composition resolved by ordering (dhctm depends on c8d4x; wmqn3 depends on dhctm; jjvg2 depends on wmqn3); decide rules sharpened (percent outside 0–100 refused not clamped, per-source same-percent no-op, observation removal never reverts status, duplicate-kind external ids refused on add); derived percents floor, never round-half-up, in both syncs; Started status items never emit. Known limitations recorded (removed books resurface under a new slug on the next sync; ContentBlocks streams are bare-slug-keyed across media types).
**ADRs written:** 0077 (Book status changes carry an effective-on date; amends 0076 §5) — written by modeling from the orchestrator's folded text so every worker sees it on disk.
**Dispatch order:** y9kxy → c8d4x → dhctm ∥ f33e2 → wmqn3 ∥ g7g1j ∥ dnv2y → jjvg2 ∥ y2ak4.

---

## 2026-09-16 01:55 -- Modeling / Captured: curation-cyxbc - Books in catalogs — a catalog entry can reference a book (RoutePrefix "books", cover as poster), with `getCatalogsForBook` / add-to-catalog on the book detail page and books in the catalog detail list, the same conformist treatment catalogs give movies, series and games

**Type:** Modeling / Capture
**BC:** curation
**Filed to:** backlog
**Summary:** Books as catalog entries (RoutePrefix books, getCatalogsForBook, Catalogs card on the book page). Backlog: needs the CatalogEntryDto key shape confirmed before promotion.

---

## 2026-09-16 01:55 -- Modeling / Captured: journal-k52j1 - Reading activity in the Journal — fold `book_progress` observation days and `Book_status_changed Finished` into the cross-media activity timeline, "Recently Read" list and monthly breakdown alongside watch sessions, episodes and play sessions

**Type:** Modeling / Capture
**BC:** journal
**Filed to:** backlog
**Summary:** Fold reading days and book finishes into the Journal's recent-activity list, monthly breakdown and cross-media stats. Backlog: needs a decision on entry granularity and rail placement.

---

## 2026-09-16 01:55 -- Modeling / Captured: intelligence-dnv2y - Dashboard Books tab and All-tab "Reading" rail — the All tab's "Books coming soon" placeholder becomes a Currently Reading card (In Focus books with progress bars and source badges, finished books lingering 7 days marked "Finished"), plus a Books tab with Currently Reading, Recently Finished, Recently Added and a reading-stats block, and reading days on the activity heatmap

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Dashboard Books tab and All-tab Reading rail replacing the Books coming soon placeholder: In Focus books with progress bars, 7-day finished linger (b1nz5 pattern), Recently Finished / Recently Added, reading stats, reading days on the heatmap, expand-in-place with FLIP keys.

---

## 2026-09-16 01:55 -- Modeling / Captured: books-f33e2 - Book detail page at /books/{slug} mirroring the movie detail page — cover hero with title/authors/year/format, a reading-progress bar with source badge and a manual "update progress" control (page or percent), the Games-shaped status control, personal rating, description/narrators/series/length from the cache, external links, recommended-by friends, progress history, content blocks, event history and remove

**Type:** Modeling / Capture
**BC:** books
**Filed to:** todo
**Summary:** Book detail page at /books/{slug} mirroring MovieDetail: cover hero, progress bar with source badge and a paper-overlay manual update-progress control, Games-shaped status control, rating, cache-fed details, external links, recommended-by, progress history, content blocks, event history, remove; e2e for the 100 % -> Finished rule.

---

## 2026-09-16 01:55 -- Modeling / Captured: books-g7g1j - Search modal Books tab — Open Library (default on) and Audible (default on, no credential needed) as selectable sources with source badges on a merged cover grid, the existing duplicate-prompt flow for import, library books in the Library tab, and navigation to the new book detail route

**Type:** Modeling / Capture
**BC:** books
**Filed to:** todo
**Summary:** Search modal Books tab with Open Library and Audible source toggles (games-k3vps template), merged cover grid with source badges, the existing duplicate-prompt flow, books in the Library tab, and the /books/{slug} route case.

---

## 2026-09-16 01:55 -- Modeling / Captured: integration-y2ak4 - Goodreads reading progress from the public user-status feed — parse "is on page N of M of Title" / "is N% done with Title" / "finished reading" items from `user_status/list/{id}?format=rss`, join them to currently-reading books by normalized title, and emit `Observe_reading_progress` (source Goodreads) as part of the shelf sync, idempotent across runs

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Goodreads reading progress from the public user-status RSS feed: parse page-N-of-M / N%-done / finished items, join by normalized title to currently-reading books, emit Observe_reading_progress (source Goodreads) inside the shelf sync, idempotent via a persisted last-status marker. Originally a spike; the review gate verified the feed live, so captured as a feature.

---

## 2026-09-16 01:55 -- Modeling / Captured: integration-wmqn3 - Goodreads adapter, Settings card and daily shelf sync — the user's public Goodreads user id (no key exists, no cookie ever, ADR-0075) drives a sync of the currently-reading / read / to-read shelf feeds into book statuses, ratings and finished dates, importing unknown currently-reading books through Open Library by ISBN

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Goodreads adapter, Settings card (user id, not a key — no API exists; no cookie ever, ADR-0075) and a daily shelf sync of currently-reading / read / to-read feeds into statuses, ratings and finished dates, importing unknown currently-reading books via Open Library by ISBN.

---

## 2026-09-16 01:55 -- Modeling / Captured: integration-jjvg2 - Audible library import and daily listening-progress sync — "Import Audible library" creates a Book per library title (matched by ASIN) and a scheduled "Audible progress sync" job reads `/1.0/library` `percent_complete`/`is_finished` into `Observe_reading_progress` commands, with the run recorded as a job run and a rejected auth file surfaced as a standing notice

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Import Audible library (one Book per library title, matched by ASIN, diff-before-enrich) and a scheduled Audible progress sync job that turns /1.0/library percent_complete / is_finished into Observe_reading_progress commands, recorded as job runs; the job never creates books.

---

## 2026-09-16 01:55 -- Modeling / Captured: integration-dhctm - Audible adapter and Settings card — an imported audible-cli auth file (never a login or device registration, ADR-0074) with refresh-token → access-token minting, a "Test connection" that names the customer and marketplace, unauthenticated catalog search and product detail with Audnexus as metadata fallback, and `addBookFromAudible`

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Audible adapter and Settings card under ADR-0074: an imported audible-cli auth file (never a login or device registration), refresh-token to access-token minting with one retry, Test connection, unauthenticated catalog search and product detail with Audnexus fallback, addBookFromAudible, and the audible auth file rejected standing notice.

---

## 2026-09-16 01:55 -- Modeling / Captured: integration-c8d4x - Open Library adapter — keyword search, ISBN and work lookup, cover download, an adapter-owned 1 req/s throttle with an identifying User-Agent — plus the `searchOpenLibraryBooks` / `addBookFromOpenLibrary` API that turns a search hit into a Book with its metadata cache slice filled

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Open Library adapter (search.json, /isbn, /works, covers) with an identifying User-Agent and adapter-owned throttles, plus searchOpenLibraryBooks / addBookFromOpenLibrary / refreshBookFromOpenLibrary. The key-less book search and metadata source ADR-0075 picks.

---

## 2026-09-16 01:55 -- Modeling / Captured: books-y9kxy - Book aggregate, projection, metadata cache slice and API — the server core of the new Books BC (identity card, external ids, format, Games-shaped status, event-sourced reading-progress observations, personal rating, recommended-by), registered in every Administration registry, with MediaType.Book threaded through Shared

**Type:** Modeling / Capture
**BC:** books
**Filed to:** todo
**Summary:** Server core of the new Books BC: Book aggregate with identity card, external ids, format, Games-shaped status and event-sourced reading-progress observations (ADR-0076), BookProjection with book_list/book_detail/book_progress, book_metadata_cache slice, every Administration registry, MediaType.Book and the Remoting surface. No UI, no adapters. Filed directly to todo — decisions made in modeling, builder asleep.

---

## 2026-09-16 01:53 -- Research: Audible API surface and listening progress

**Type:** Research
**Requested by:** model
**Report:** knowledge/research/audible-api-surface-and-listening-progress-2026-09-16.md
**Review:** PASS (iteration 3) — iterations 1 and 2 failed on response_groups / image_sizes endpoint attribution (library vs catalog, then search vs detail); each fixed against the raw mkb79 docs. Iteration 1's reviewer also confirmed live that catalog search needs no credential.
**Summary:**
- No official Audible API. Catalog search (`/1.0/catalog/products?keywords=`) and product detail (`/1.0/catalog/products/{asin}`, includes `series`) work unauthenticated on `api.audible.com` and `api.audible.de`; `/1.0/library` (`percent_complete`, `is_finished`, `listening_status`) returns 403 without auth.
- The only route to a library credential is Amazon OAuth + `POST /auth/register` device registration — even the external-browser login variant registers from the calling program. A user-run `audible-cli quickstart` auth file carries `refresh_token`, `adp_token`, `device_private_key`, `locale_code`, `customer_info`; `POST https://api.amazon.<tld>/auth/token` (five form fields, no grant_type) mints 60-minute access tokens from it without any login.
- Audnexus (`api.audnex.us/books/{asin}`) is a live, key-less metadata fallback (cover, narrators, series, description, runtime; no title search; 100 req/min); its maintainer points new work at AudiobookDB but keeps it online.
**Unverified (labelled):** refresh-token lifetime; absence of ban reports is absence of evidence; whether the `x-main` cookie scrape exposes progress; audible-cli export column list.

---

## 2026-09-16 01:52 -- Research: Goodreads reading progress and book metadata sources

**Type:** Research
**Requested by:** model
**Report:** knowledge/research/goodreads-reading-progress-and-book-metadata-sources-2026-09-16.md
**Review:** PASS (iteration 2) — iteration 1 FAIL overturned the report's central negative (the reviewer found the public `user_status/list/{id}?format=rss` feed carries numeric progress); researcher re-verified live and corrected in place
**Summary:**
- The Goodreads developer API is dead (no keys since 2020-12-08, no successor) — the setting a Goodreads integration needs is the user id, not a key.
- Public, key-free, cookie-free RSS feeds still work for a public profile: the shelf feed (`review/list_rss/{id}?shelf=…`, 100-item cap, book id/ISBN/cover/ratings/dates, no progress) and the user-status feed (`user_status/list/{id}?format=rss`, paginated, "is on page N of M of Title" / "N% done with Title" / "is finished with Title" as text, no book id — title-only join).
- Open Library (key-less, ISBN endpoints, covers host, `identifiers.goodreads`, 1–3 req/s by User-Agent) is the fit for search/metadata; Google Books requires a key; Hardcover is the token-based fallback with page progress; StoryGraph has no API.
**Unverified (labelled):** ISBNdb pricing tiers; Hardcover's exact GraphQL shape and limits; Google Books quota figures.

---

## 2026-09-14 16:13 -- Work session ended

**Type:** Work / Session end
**Duration:** 31m (first "Batch started" 15:42 → 16:13)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Lost-result re-dispatches:** 0
**Lost-result escalations:** 0
**Dispatches:** intelligence-b1nz5: 1
**Commits:** 3 (batch start, task integration, this entry)
**Vision-conformance:** none — batch aligns with vision. The one task extends the finished-series 7-day linger to movies and games on the All tab, serving "Unified Dashboard" / "Remaining v1 Work" and the "In Focus" core concept (a finished item visibly leaves the queue instead of vanishing); it touches no "Out of Scope (v1)" item. vision.md still has no "What success looks like"/"Non-goals" headings — `extractVisionSections` returns two empty lists, so this judgement was made against "Unified Dashboard"/"Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as the prior four sessions also did.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `type: feature`, all nine touched files product surfaces under `src/` and `tests/`.
**Carry-over:** none — working tree clean, no registered worktrees remain; `.worktrees/` removed after the one stranded RESULT sidecar (`intelligence-b1nz5.iter-1.md`, its worktree already torn down) was swept.

**Result source (intelligence-b1nz5, ADR-0080 §6):** sidecar · layout both+sentinel · sidecar present — the first task in this project whose worker wrote the sidecar; `selectResultSource` picked it first-try, no repairs.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-12 20:46 boundary — all three commits in the window (`8c203b6` session-end bookkeeping, `ddd045e` capture, `b637fd2` refine) carry a `[<task-id>]` trailer. Nothing flagged; `whats-next.md` not written.

**Verification history:** intelligence-b1nz5 PASS iteration 1. The verifier re-ran all three suites from the worktree (build exit 0 with only the pre-existing FS0020; Expecto 786/0, up from 769; Vitest 62 / 10 files, up from 61), walked every non-[human-eye] criterion to a named Expecto case in `tests/Server.Tests/DashboardLingerTests.fs`, confirmed the Movies tab and the `MoviesToWatchQuery` card still call the untouched strict query while only the All-tab call sites moved to `getAllTabMoviesToWatch`, and checked that `retired_at` on both paths is `DateTimeOffset.UtcNow.ToString("o")` (EventStore.appendToStream vs the handler), so text comparison against `date('now','-7 days')` is sound. FLIP keys untouched (ADR-0073). No ADR required: the boot-time idempotent backfill follows ADR-0056's automate-at-boot branch and the event-derived column follows the `prior_play_time`/ADR-0044 precedent. README delta on the intelligence `Linger window` bullet disposed `applied` (through the LF-normalise/CRLF-restore wrapper; the upstream `lib/readme-delta.mjs` CRLF bug is still open). The worker found one thing the task didn't foresee: `Projection.replayIntoShadow` (ADR-0031) calls the same `Init` against a shadow connection without an `events` table, so the backfill is wrapped in the same `try/with` idiom the other migrations use.

**Builder checks pending (the one [human-eye] criterion on intelligence-b1nz5):** on the All tab, a movie watched in the last 7 days shows a green "Watched" pill top-left (replacing the In Focus crosshair and the Jellyfin play button) and a game retired in the last 7 days shows a green "Retired" pill — both should read as "done" and sit naturally with the finished series card's green treatment. Also worth a glance after deploy: the `retired_at` backfill runs once at startup and any game retired in the 7 days before the deploy will appear on the Games rail immediately — intended, per the task.

**Harness notes:** the homedir plugin cache `0.9.5/lib/` is complete this session, so every verb ran from it directly (`node <cache>/0.9.5/lib/task-lifecycle-cli.mjs <verb>`), no local-checkout fallback. Two new gotchas: (1) a bash quoted heredoc written through the Bash tool collapses `\\` to `\` inside the file — the scoped-commit runner's cwd came out as `C:srcheimeshoff…` and `execFile('git')` failed with a bare ENOENT that looked like git missing from PATH; writing the runner via the Write tool with forward-slash paths fixed it. (2) `scoped-commit` refuses an already-staged deletion by absolute path (`pathspec did not match`) — pass the deleted `todo/` path only while its deletion is still unstaged. Same as prior sessions: JSON opts in scratchpad files, root `node_modules` junctioned into the worktree and `rmdir`'d before `git worktree remove` (main copy verified intact, 210 entries). Pre-loaded ADRs/prior art/return format were handed to the worker as one assembled scratchpad file rather than pasted inline, to keep the conductor lean; the worker read it and returned in the strict shape first time.

---

## 2026-09-14 16:11 -- Task verified and completed: intelligence-b1nz5 - All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode"

**Type:** Work / Task completion
**Task:** intelligence-b1nz5 - All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode"
**Summary:** A watched movie now lingers on the All-tab "Movies to Watch" rail for 7 days (marked "Watched", any movie — not just In Focus/Jellyfin ones — while the Movies tab's own card stays strictly unwatched-only via a new split query), and a retired game lingers on the "Games" rail for 7 days (marked "Retired") via a new event-derived `game_list.retired_at` column with an idempotent startup backfill for already-retired games — the same "just finished this" linger `SeriesProjection.getDashboardSeriesNextUp` already gives a finished series.
**Duration:** 28m
**Verification:** PASS (iteration 1)
**Files changed:** 9
**Tests added:** 18
**ADRs written:** none

---

## 2026-09-14 15:40 -- Batch started: [intelligence-b1nz5]

**Type:** Work / Batch start
**Tasks:** intelligence-b1nz5 - All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode"
**Parallel:** no (1 worker — intelligence-b1nz5 is the only task in todo/ across every BC; nothing held back by the cap)

---

## 2026-09-14 15:37 -- Modeling / Refined: intelligence-b1nz5 - All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** todo
**Summary:** Builder asked that already-retired games get their empty retired_at filled from the event that retired them. Added an idempotent startup backfill in GameProjection.createTables: each Retired game with NULL retired_at takes the timestamp of its latest Game_status_changed event whose status is Retired (or legacy Completed), in the same text format the handler writes; plus an Expecto criterion covering re-retire, untouched rows, idempotence and format parity. Ships as code, never run by hand against the live DB.

---

## 2026-09-14 15:29 -- Modeling / Captured: intelligence-b1nz5 - All-tab dashboard — a watched movie stays on "Movies to Watch" and a retired game stays on "Games" for 7 days, marked finished, the same way a finished series already lingers on "Next episode"

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Builder wants finished movies and retired games to linger on the All-tab dashboard like finished series already do (7 days after latest watched episode, green). Movies to Watch keeps any movie watched in the last 7 days (All tab only; Movies tab stays strict); the Games rail keeps games retired in the last 7 days via a new event-derived game_list.retired_at column; both get a finished/retired mark and sort first. Filed to todo: window, movie scope, retirement timestamp and look all decided with the builder.

---

## 2026-09-12 20:46 -- Work session ended

**Type:** Work / Session end
**Duration:** 26m (first "Batch started" 20:18 → 20:46)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-cs2dm: 1
**Commits:** 3 (batch start, task integration, this entry)
**Vision-conformance:** none — batch aligns with vision. The one task fixes a remount defect on dashboard card expand/collapse the builder reported on the running app, serving "Unified Dashboard" / "Remaining v1 Work" and the "Intent-driven" design principle; it touches no "Out of Scope (v1)" item. vision.md still has no "What success looks like"/"Non-goals" headings — `extractVisionSections` returns two empty lists, so this judgement was made against "Unified Dashboard"/"Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as the prior three sessions also did.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `type: bug`, both touched files product surfaces (`src/Client/Pages/Dashboard/Views.fs`, `tests/e2e/dashboard-card-collapse-persists.spec.ts`).
**Carry-over:** none — working tree clean, no registered worktrees remain. `.worktrees/` held regenerated .NET `obj/` residue (3 files each) under `design-system-btmdx`, `intelligence-m09d4` and `intelligence-cs2dm` — not registered worktrees, no source, recreated after earlier sessions' teardown (most likely Ionide in VS Code re-restoring the removed worktrees' `Server.fsproj`); deleted as build residue and `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-12 19:49 boundary — both commits in the window (`cf88717` session-end bookkeeping, `50c1140` the cs2dm capture) carry a `[<task-id>]` trailer. Nothing flagged; `whats-next.md` not written.

**Verification history:** intelligence-cs2dm PASS iteration 1. The verifier re-ran all three suites from the worktree (build exit 0 with only the pre-existing FS0020; Expecto 769/0; Vitest 61 / 10 files) and also ran the worker's new Playwright spec itself (1 passed, cold-started against an isolated temp `DATA_DIR`, no live DB touched). It confirmed structurally that `prop.ref setContainerRef` still sits on the root of every `[data-flip-key]` in both states and that `#dashboard-expanded-card` remains a sibling of the hidden clone rather than an ancestor — the class of defect that failed intelligence-m09d4's iteration 1 is not reintroduced. README delta on the intelligence `Card grow` bullet disposed `applied` (through the LF-normalise/CRLF-restore wrapper; the upstream `lib/readme-delta.mjs` CRLF bug is still open). The worker could not reproduce symptom 2's exact Movies-tab artifact (no TMDB API key in the sandbox to seed movies; with empty cards the index-remap happens to line up correctly) and confirmed the same root mechanism hermetically on the Games tab instead.

**Builder checks pending (the three [human-eye] criteria on intelligence-cs2dm):** collapsing any dashboard card — the cards reappear in place with no fade-in-up entrance, on the All tab and the Movies tab; Movies tab — expand Recently Watched, let it finish, collapse: Recently Added shows only its own content and both cards are normal collapsed height (and reversed); rapid expand/collapse toggling leaves no stale content, height, or stuck transform. The verifier flagged one residual layout risk worth a glance during these: the collapsed-state outer container changed from `flex flex-col gap-4` to `grid grid-cols-1` (flex-col moved one level down into the keyed wrapper), so horizontal poster rails now sit inside a grid item — collapsed-state rail width should be eyeballed.

**Harness notes:** same as prior sessions — verbs invoked against the local source install at `C:\src\heimeshoff\agentic\agentheim\lib\`; all JSON opts written to scratchpad files; root `node_modules` junctioned into the worktree and removed via `rmdir` before `git worktree remove`, main copy verified intact. Two new notes: (1) the verifier flagged that the worker verified TDD red/green with a bare `git stash` on `Views.fs` — the stash stack is shared across worktrees, so a temporary WIP commit is the safer technique; nothing was lost here. (2) `references/worker-return-format.md` gained an ADR-0080 sidecar-write requirement upstream mid-session; this session's worker prompt predated it and returned in-transcript normally.

---

## 2026-09-12 20:45 -- Task verified and completed: intelligence-cs2dm - Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4)

**Type:** Work / Task completion
**Task:** intelligence-cs2dm - Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4)
**Summary:** growingTabArea now renders one keyed structure in both expand states — a stable prop.key "content" wrapper (always mounted, only its visibility/placement classes change) and a stable prop.key "surface" sibling (mounted only when a card is grown) — so React mounts/unmounts the surface on its own instead of reconciling the two shapes by index, which used to remount every collapsed card on each expand/collapse and could repurpose the surface's DOM node as a sibling card
**Duration:** 25m
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 1
**ADRs written:** none

---

## 2026-09-12 20:17 -- Batch started: [intelligence-cs2dm]

**Type:** Work / Batch start
**Tasks:** intelligence-cs2dm - Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4)
**Parallel:** no (1 worker — intelligence-cs2dm is the only task in todo/ across every BC; nothing held back by the cap)

---

## 2026-09-12 20:12 -- Modeling / Captured: intelligence-cs2dm - Collapsing a dashboard card remounts every collapsed card (fade-in-up replays, and the grown surface's DOM node is repurposed into a sibling card) — give `growingTabArea`'s two render branches stable keys so the collapsed subtree really stays mounted across expand/collapse (ADR-0073 §3/§4)

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Two builder-reported symptoms after design-system-btmdx — collapse still fades the initial items, and on the Movies tab collapsing Recently Watched leaves Recently Added showing Recently Watched's large tiles with its own content below — share one root cause: growingTabArea's two render branches (flat list vs invisible-wrapper + surface) are unkeyed, so React reconciles them by index and remounts every collapsed card on each expand and collapse (replaying chromeClass's animate-fade-in-up) and repurposes the surface node as a sibling card. Fix: stable prop.key on the always-present content wrapper and on the surface, so the collapsed subtree really stays mounted as ADR-0073 §3/§4 already describe. Filed straight to todo: root cause read from code, criteria concrete.

---

## 2026-09-12 19:49 -- Work session ended

**Type:** Work / Session end
**Duration:** 23m (first "Batch started" 19:29 → 19:52)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-btmdx: 1
**Commits:** 3 (batch start, task integration, this entry)
**Vision-conformance:** none — batch aligns with vision. The one task removes a fade the builder rejected on the running dashboard card grow, serving "Remaining v1 Work → Unified Dashboard" and the "Intent-driven" design principle; it touches no "Out of Scope (v1)" item. vision.md still has no "What success looks like"/"Non-goals" headings — `extractVisionSections` returns two empty lists, so this judgement was made against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as the 2026-09-10 and 2026-09-12 18:22 sessions also did.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `type: bug`, both touched files product surfaces under `src/Client/`.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-12 18:22 boundary — both commits in the window (`161bfad` session-end bookkeeping, `09d8a0b` the btmdx capture) carry a `[<task-id>]` trailer. Nothing flagged; `whats-next.md` not written.

**Verification history:** design-system-btmdx PASS iteration 1. The verifier independently confirmed the two judgement calls the worker made beyond the spec's letter: (1) the "no `FadeIn` under `src/Client/`" criterion is met in intent — the only surviving matches are the pre-existing, unrelated `DesignSystem.animateFadeIn`/`animateFadeInUp` Tailwind class tokens, present unchanged on the batch-start commit; (2) `src/Client/Pages/StyleGuide/Views.fs` was correctly left untouched — the "Grow Transition" caption never claimed size-changed items fade. README delta on the design-system `Motion primitive` bullet disposed `applied` (through the LF-normalise/CRLF-restore wrapper noted last session; the upstream `lib/readme-delta.mjs` CRLF bug is still open).

**Builder checks pending (the two [human-eye] criteria on design-system-btmdx):** expanding and collapsing a dashboard card — every item present in both views only moves, no item fades in or out at either end, on a poster rail (Movies to Watch), a list card that re-flows into tiles (Recently Finished), and a games card; and the StyleGuide "Grow Transition" specimen's four tiles travel without fading.

**Harness notes:** same three as the 18:22 entry, unchanged — verbs invoked against the local source install at `C:\src\heimeshoff\agentic\agentheim\lib\` (cached 0.9.2/0.9.3 unusable); all JSON opts written to scratchpad files; root `node_modules` junctioned into the worktree and removed via `rmdir` before `git worktree remove`, main copy verified intact. One new note: hand-composing a `scoped-commit` path list inside a bash-quoted `node -e` string stripped every backslash from the Windows paths (`git add` refused them as outside the repository) — building the list from a small `.mjs` script taking the paths as argv worked first time.

---

## 2026-09-12 19:48 -- Task verified and completed: design-system-btmdx - Surviving FLIP items must never fade — drop `FadeIn` from `Motion.fs` so an item present in both the collapsed and expanded view only ever translates (ADR-0073 §1 amended)

**Type:** Work / Task completion
**Task:** design-system-btmdx - Surviving FLIP items must never fade — drop `FadeIn` from `Motion.fs` so an item present in both the collapsed and expanded view only ever translates (ADR-0073 §1 amended)
**Summary:** Dropped `FadeIn` from the FLIP motion primitive — `FlipMove` is now `{ Key; Dx; Dy }`, `Flip.play` emits translate-only WAAPI keyframes unconditionally, and `Flip.plan` drops a pure resize with no position delta as a sub-threshold move; a surviving item only ever translates (ADR-0073 §1/§9, already amended)
**Duration:** 15m
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-12 19:31 -- Batch started: [design-system-btmdx]

**Type:** Work / Batch start
**Tasks:** design-system-btmdx - Surviving FLIP items must never fade — drop `FadeIn` from `Motion.fs` so an item present in both the collapsed and expanded view only ever translates (ADR-0073 §1 amended)
**Parallel:** no (1 worker — design-system-btmdx is the only task in todo/ across every BC; nothing held back by the cap)

---

## 2026-09-12 19:23 -- Modeling / Captured: design-system-btmdx - Surviving FLIP items must never fade — drop `FadeIn` from `Motion.fs` so an item present in both the collapsed and expanded view only ever translates (ADR-0073 §1 amended)

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** Surviving FLIP items must never fade. Flip.plan sets FadeIn on any size change and Flip.play layers opacity 0->1 on those items, but plan only ever returns keys present in BOTH snapshots — so the fade's only audience is the surviving items, which should read as one continuous motion. Drop FadeIn from FlipMove and make Flip.play translate-only unconditionally; the threshold carve-out for size-only changes goes with it. ADR-0073 §1 and §9 amended in place as part of this capture. Filed straight to todo: decision settled, criteria concrete.

---

## 2026-09-12 18:22 -- Work session ended

**Type:** Work / Session end
**Duration:** 1h32m (first "Batch started" 16:51 → 18:23)
**Completed:** 2 (first-try PASS: 1, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** design-system-m2v88: 1, intelligence-m09d4: 2
**Commits:** 5 (2 batch start, 2 task integration, this entry)
**Vision-conformance:** none — batch aligns with vision. Both tasks serve "Remaining v1 Work → Unified Dashboard" ("individual tabs will grow over time with more stats and intelligence") and the "Intent-driven" design principle; neither touches an "Out of Scope (v1)" item, and the admin-console boundary clause's tie-break (media experience wins) is not in play since this session was entirely media experience. vision.md still has no "What success looks like"/"Non-goals" headings — `extractVisionSections` returns two empty lists, so this judgement was made against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as the 2026-09-10 session also did. The stale "Trakt.tv / Jellyfin sync (v2)" Out-of-Scope line remains a vision-text staleness, not a drift signal.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (2 tasks) — both `type: feature`, all touched files product surfaces under `src/Client/`.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 3 human commits since the 2026-09-10 20:07 boundary. `74e1ab8` (expandable dashboard cards) is the surface both tasks build on and was already re-grounded by the 2026-09-12 16:45 modeling pass; `0a343bb` (qBittorrent 5.x login contract, ADR-0072) is recorded as a hotfix at 2026-09-11 09:50; `1dfb0b5` is the ADR-0078 two-root layout migration itself — a pure path move. Nothing needed re-alignment.

**Verification history:** design-system-m2v88 PASS iteration 1. intelligence-m09d4 FAIL iteration 1 → PASS iteration 2. The iteration-1 FAIL was a genuine catch, not a nit: `tabArea` deliberately keeps the collapsed subtree mounted `invisible`, so while a card is grown every item's `data-flip-key` exists twice in the DOM; `Motion.Flip.play` resolves its element with `querySelector`, which returns the first (hidden) match, so on expand every transform landed on an invisible clone and no item visibly travelled — the exact behaviour the task exists to replace, and the hazard ADR-0073 already names as its rejection reason (a) against the View Transitions API. The same pass also caught that the task's `## What` requirement for card-scoped flip keys had been deferred to a self-filed follow-up bug (`intelligence-fk3p9`) rather than implemented. Iteration 2 fixed both — scoping only the *play* lookup (never the snapshot, which must still span the whole container for the before→after key connection to survive the subtree swap) and threading a required `card: DashboardCard` parameter through every renderer so a missing scope is a compile error — and dropped the follow-up as fixed rather than carried.

**Builder checks pending (the six [human-eye] criteria on intelligence-m09d4 — never a worker/verifier action):** expanding a poster rail (Movies to Watch) — posters slide upward to the top of the viewport, no whole-list fade, fetched posters fill below without disturbing them; a list card re-flowing into tiles (Recently Finished, Most Watched With) — no text stretching, the tier that takes `Flip.plan`'s `FadeIn` size-changed path; collapse mirrors expand within the same ~0.5s budget; `prefers-reduced-motion: reduce` shows no travel; rapid expand/collapse toggling leaves nothing stuck. Plus design-system-m2v88's own StyleGuide "Grow Transition" specimen. The verifier flagged one asymmetry worth watching, within doctrine: the effect scrolls on expand only (ADR-0073 §6), so on collapse the document shortens and the browser may clamp `scrollTop`, and because boxes are viewport coordinates that clamp is carried into the travel.

**Harness notes:** (1) **`lib/readme-delta.mjs` silently mis-applies every delta on a CRLF file.** `SECTION_HEADER_RE = /^## (.+)$/` never matches a line ending in `\r` (JS `.` excludes line terminators; `$` without `/m` demands true end-of-string), so `findSectionLineRange` returns null for every section, `applyReadmeDelta` reports `appended-fallback`, and the bullet lands at the END of the document with the stale one left in place — a silent duplicate, not a refusal. Hit on the design-system README (CRLF); reverted and re-applied through an LF-normalise/CRLF-restore wrapper, disposition then `applied`. Suggested fix upstream: `/^## (.+?)\r?$/` or normalise inside `applyReadmeDelta`, plus a CRLF fixture. (2) The homedir→cache→semver-max bootstrap resolves to cached plugin `0.9.2`, whose `lib/` predates `scoped-commit.mjs`, `layout-migration.mjs`, `merge-conflict-ladder.mjs` and the `migrate`/`bounce`/`log`/`index-add` verbs; the `0.9.3` cache entry is a partial copy with no `lib/` at all. Every verb this session was therefore invoked against the local source install at `C:\src\heimeshoff\agentic\agentheim\lib\`. (3) Inline-JSON CLI arguments remain unusable under Git Bash — Windows backslash paths break JSON escaping and backticked identifiers are eaten by command substitution inside double quotes; all opts were written to scratchpad files and read in-process. One protocol `Reasons:` line was written with its backticked identifiers stripped by this and repaired in place. (4) Root `node_modules` junctioned into each worktree for `npm run build`/`test:client`, removed via `rmdir` before `git worktree remove`; main copy verified intact both times.

---

## 2026-09-12 18:20 -- Task verified and completed: intelligence-m09d4 - Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)

**Type:** Work / Task completion
**Task:** intelligence-m09d4 - Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)
**Summary:** Wired the design-system FLIP primitive into the dashboard card expand/collapse — growingTabArea snapshots on click and plays item travel plus the card-box height grow from one useLayoutEffect against the visible face, card-scoped flip keys, and the 50ms scroll guess in State.fs retired
**Duration:** 52m
**Verification:** PASS (iteration 2)
**Files changed:** 4
**Tests added:** 1
**ADRs written:** none

---

## 2026-09-12 17:57 -- Verification failed: intelligence-m09d4 - Dashboard card expand/collapse grows in place

**Type:** Work / Verification failure
**Task:** intelligence-m09d4 - Dashboard card expand/collapse grows in place
**Iteration:** 1 of 3
**Reasons:** expand animates the hidden collapsed clone, not the grown surface — `tabArea` keeps both faces mounted, so every item key appears twice and `Flip.play`'s `querySelector` resolves the first (invisible) match, so no item visibly travels on expand (collapse is unaffected, making the directions asymmetric); the `## What` requirement that flip keys be card-scoped is unimplemented and was deferred to a follow-up bug instead; secondary — the layout effect re-fires on `ExpandedItemsLoaded` and re-issues the instant scroll
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-12 17:21 -- Batch started: [intelligence-m09d4]

**Type:** Work / Batch start
**Tasks:** intelligence-m09d4 - Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)
**Parallel:** no (1 worker — intelligence-m09d4 is the only ready task across every BC; it unblocked this session when its dependency design-system-m2v88 completed. Nothing held back by the cap)

---

## 2026-09-12 17:20 -- Task verified and completed: design-system-m2v88 - Grow / shared-element FLIP motion primitive — a new `Motion.fs` with a pure `Flip.plan`, a WAAPI `snapshot`/`play`/`growSurface` shell, `flipKey`, a reduced-motion gate, `--duration-grow` (0.5s) / `--ease-grow` tokens, and a StyleGuide specimen (ADR-0073)

**Type:** Work / Task completion
**Task:** design-system-m2v88 - Grow / shared-element FLIP motion primitive — a new `Motion.fs` with a pure `Flip.plan`, a WAAPI `snapshot`/`play`/`growSurface` shell, `flipKey`, a reduced-motion gate, `--duration-grow` (0.5s) / `--ease-grow` tokens, and a StyleGuide specimen (ADR-0073)
**Summary:** Shipped the grow/shared-element FLIP motion vocabulary — src/Client/Motion.fs (pure Flip.plan, DOM shell snapshot/play/growSurface/cancel, flipKey, prefersReducedMotion, growDurationMs/growEasing mirroring new --duration-grow/--ease-grow CSS tokens) plus a live "Grow Transition" StyleGuide specimen, per ADR-0073
**Duration:** 17m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 8
**ADRs written:** none

---

## 2026-09-12 16:51 -- Batch started: [design-system-m2v88]

**Type:** Work / Batch start
**Tasks:** design-system-m2v88 - Grow / shared-element FLIP motion primitive — a new `Motion.fs` with a pure `Flip.plan`, a WAAPI `snapshot`/`play`/`growSurface` shell, `flipKey`, a reduced-motion gate, `--duration-grow` (0.5s) / `--ease-grow` tokens, and a StyleGuide specimen (ADR-0073)
**Parallel:** no (1 worker — design-system-m2v88 is the only ready task across every BC; intelligence-m09d4 is in todo/ but blocked on it via depends_on and is held for the next wave, not capped out)

---

## 2026-09-12 16:45 -- Modeling / Refined: intelligence-m09d4 - Dashboard card expand/collapse grows in place

**Type:** Modeling / Refine
**BC:** intelligence (and design-system-m2v88's Box/snapshot wording)
**Status after:** todo
**Summary:** Builder ran the app expecting the captured tasks to be built; nothing had been — what they saw is 74e1ab8's `animate-fade-in-up` on the expanded surface. Their description of the intended effect (existing movies slide upward to the top of the screen, new entries fill the rest below) is now the poster-rail acceptance criterion. Consequences: the scroll-into-view becomes instant and runs before the after-snapshot; FLIP boxes are viewport coordinates (not document coordinates) so the travel carries the scroll shift; the expanded surface drops `animate-fade-in-up`. ADR-0073 amended in place (decision 1 coordinates, decision 6, new 6a) — unshipped, no worker has read it.
**Split into:** none
**ADRs written:** none (0073 amended)

---

## 2026-09-12 16:40 -- Modeling / Captured: intelligence-m09d4 - Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Intelligence half of the dashboard card grow animation: wire the FLIP primitive into expand/collapse so the card surface and its already-rendered items travel to their expanded positions in ~0.5s, later-fetched items append without disturbing them, and State.fs's 50ms scroll guess is retired (ADR-0073). Depends on design-system-m2v88.

---

## 2026-09-12 16:40 -- Modeling / Captured: design-system-m2v88 - Grow / shared-element FLIP motion primitive — a new `Motion.fs` with a pure `Flip.plan`, a WAAPI `snapshot`/`play`/`growSurface` shell, `flipKey`, a reduced-motion gate, `--duration-grow` (0.5s) / `--ease-grow` tokens, and a StyleGuide specimen (ADR-0073)

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** Design-system half of the dashboard card grow animation: a reusable key-based FLIP motion primitive (pure Flip.plan + WAAPI shell, flipKey, reduced-motion gate, --duration-grow 0.5s token, StyleGuide specimen), per ADR-0073. Filed straight to todo: technique settled, criteria concrete.

---

## 2026-09-11 09:50 -- Hotfix: qBittorrent 5.x login contract (integration)

**Type:** Hotfix / Conductor
**Trigger:** first live "Test connection" against qBittorrent 5.2.3 reported "authentication failed" while qBittorrent's log recorded `WebAPI login success` for the same request.
**Root cause:** integration-qb7tk implemented the 4.x wiki contract (HTTP 200 `Ok.`/`Fails.`, cookie `SID`); 5.x answers a good login with HTTP 204 + cookie `QBT_SID_<port>` and a bad one with HTTP 401. Additionally the shared `HttpClient`'s cookie jar replayed the session cookie on the next login. Verified against `release-5.2.3` source and a single live probe (one `invalid credentials` entry in qBittorrent's log, username `mediatheca-probe`).
**Decision:** ADR-0072 — `Session` carries the cookie name, success = 2xx + session cookie, 401/403 → `AuthFailed`, dedicated `UseCookies=false` client for qBittorrent.
**Outcome:** fix + tests, redeployed via `/deploy`. No task file (hotfix on a shipped task); ADR + README updated.

## 2026-09-10 20:07 -- Work session ended

**Type:** Work / Session end
**Duration:** 27m (first "Batch started" 19:40 → 20:07)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-mqsd3: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is the UI half of "Remove local copy" (integration BC, generic): a paper-overlay dialog (ADR-0016) over integration-r4vzm's plan/execute API, client-tested per ADR-0064, reload-don't-patch after success so the projection stays the source of truth. vision.md still has no "What success looks like"/"Non-goals" headings; judged against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles". The stale "Trakt.tv / Jellyfin sync (v2)" Out-of-Scope line remains a vision-text staleness, not a drift signal.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `type: feature`, all touched files product surfaces.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-10 19:16 boundary — both commits (21564bd refine, 65da8af promote) carry the integration-mqsd3 trailer. Nothing to re-align.

**Verification history:** iteration 1 PASS — `npm run build` clean, 759 server tests (+1 in `SeriesProjectionReadsTests.fs`), 43 client tests (+14 in `LocalCopyRemovalDialog.test.fs`). Verifier audited paper-overlay conformance by hand against `design-rules.md` (the design-check Skill is not in the verifier's toolset): no backdrop-filter, no translucent fills, no colour literals. Two advisory nits, not violations: the action pill repeats the sibling "Search on IPTorrents" raw Tailwind string; per-torrent rows use `velvetCard` inside the modal (opaque, non-floating content). Task Outcome says "16 test cases", runners show 15 — immaterial.

**Builder checks pending (post-deploy, live on harbour — never a worker/verifier action):** after a successful removal the movie page shows neither "Play in Jellyfin" nor "Remove local copy" without a browser reload or a Jellyfin sync, and the series page loses "Remove local copy"; plus the three [human-eye] criteria — hit-and-run copy legible without a warning wall, pack warning visibly more prominent than the ratio flag, the outcome reads as progress not a log dump. The r4vzm builder checks (curl the plan/execute API, ssh-verify files gone, play-state preserved, `JELLYFIN_MEDIA_ROOT`/`QBITTORRENT_DOWNLOAD_ROOT` match harbour's mounts) are still outstanding from the previous session.

**Harness notes:** (1) cached plugin 0.9.2 `checkpoint` again omitted the vacated `doing/` path from its manifest — staged explicitly, git recorded the move as a rename. (2) The `claim`/`checkpoint` CLI's inline-JSON third argument fails under Git Bash with Windows backslash paths ("Bad escaped character"); worked around by writing the opts JSON to the scratchpad and reading it in-process. (3) Root `node_modules` junctioned into the worktree for `npm run build`/`test:client`, removed via `rmdir` before `git worktree remove`; main copy verified intact. (4) Board is empty across every BC; follow-ups named in the task (series "Play in Jellyfin" link, season-level and single-episode removal, hard-refuse hit-and-run mode) are uncaptured.

---

## 2026-09-10 20:04 -- Task verified and completed: integration-mqsd3 - "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API

**Type:** Work / Task completion
**Task:** integration-mqsd3 - "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API
**Summary:** Shipped the "Remove local copy" button and paper-overlay dialog on the movie and series detail pages — a shared LocalCopyRemovalDialog phase machine (Planning/PlanFailed/Confirming/Removing/Finished) over integration-r4vzm's plan/execute API with pure acknowledgedHashes/canRemove/outcomeRows seams, plus SeriesDetail.JellyfinId populated by SeriesProjection.getBySlug
**Duration:** 24m
**Verification:** PASS (iteration 1)
**Files changed:** 13
**Tests added:** 15
**ADRs written:** none

---

## 2026-09-10 19:39 -- Batch started: [integration-mqsd3]

**Type:** Work / Batch start
**Tasks:** integration-mqsd3 - "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API
**Parallel:** no (1 worker — integration-mqsd3 is the only ready task across every BC; nothing held back)

---

## 2026-09-10 19:38 -- Modeling / Promoted: integration-mqsd3 - "Remove local copy" — the action on the movie and series detail pages, with a paper-overlay confirmation dialog showing the resolved path, the case, and one acknowledged row per matched torrent (ratio, seeding time, hit-and-run flag, pack warning), then the step-by-step outcome; UI over integration-r4vzm's plan/execute API

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-09-10 19:38 -- Modeling / Refined: integration-mqsd3 - "Remove local copy" UI (second pass, code-grounded after integration-r4vzm shipped)

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo (promoted in the same session — see the Promoted entry above)
**Summary:** Checked every client seam against the code as it stands after integration-r4vzm: the two `IMediathecaApi` members and their wire types in `Shared.fs`, the movie page's action row and its existing "Remove movie" confirm, the series page's action row and model, `ModalPanel`/`DesignSystem.modalPanel`, the `*.test.fs` harness and `Client.fsproj` test block. Two corrections: (1) `Shared.SeriesDetail` carries no `JellyfinId` and the series page never loads the Jellyfin server URL, so the series entry point needs one small server addition — the DTO field, populated in `SeriesProjection.getBySlug` via r4vzm's `JellyfinStore.getSeriesJellyfinId`, mirroring `MovieProjection`; the task is no longer "pure client work". (2) No client test inspects an Elmish `Cmd`, and the tests' `Unchecked.defaultof<IMediathecaApi>` is `null` in Fable, so the dialog is specified as a shared `Components/LocalCopyRemovalDialog.fs` phase machine (`Planning | PlanFailed | Confirming of plan * ticked | Removing of acknowledged | Finished of outcome`) whose `update` takes a plain effects record, with pure `acknowledgedHashes`/`canRemove` seams — every criterion is now assertable on the model. Wire shapes corrected to what shipped (`removeLocalCopy` takes a tuple, `SeedingTimeDays`, `AlreadyGoneInJellyfin` with an empty path, the sync-in-progress refusal is an outcome not an exception). Live-on-harbour criterion marked builder-run after deploy. Orchestrator not re-dispatched — the design decisions were settled in the first pass (r4vzm Notes, ADR-0071); this pass verified seams against code. Readiness gate passed: dependencies design-system-001 and integration-r4vzm are both done.
**Split into:** none
**ADRs written:** none

---

## 2026-09-10 19:16 -- Work session ended

**Type:** Work / Session end
**Duration:** 33m (first "Batch started" 18:43 → 19:16)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-r4vzm: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is the server-side half of "Remove local copy" (integration BC, generic): projection-only cache invalidation per ADR-0071, the item's play state preserved first through the existing `Record_watch_session` / `Mark_episode_watched` paths (Replayable principle honored, no new event), no UI. vision.md still has no "What success looks like"/"Non-goals" headings; judged against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles". The stale "Trakt.tv / Jellyfin sync (v2)" Out-of-Scope line remains a vision-text staleness, not a drift signal.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `type: feature`, all touched files product surfaces.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-10 18:14 boundary — both commits (ddf508e refine, 0266284 promote) carry the integration-r4vzm trailer. Nothing to re-align.

**Verification history:** iteration 1 PASS — 758 tests (+60 across `LocalCopyRemovalTests.fs`, `JellyfinItemTests.fs`, `JellyfinStoreTests.fs`, `JellyfinImportTests.fs`), `npm run build` clean, the six pinned `JellyfinReauthTests.fs` untouched, no new event case in Movies/Series, mount roots read in `Composition.fs` via env and not seeded into SettingsStore.

**Builder checks pending (post-deploy, live on harbour — never a worker/verifier action):** the two `curl` criteria against `planLocalCopyRemoval` / `removeLocalCopy` (movie → Jellyfin 404, hash gone from `torrents/info`, no `jellyfin_movie` row; series → same plus no `jellyfin_series`/`jellyfin_episode` rows), the ssh check that the files are gone from `/mnt/media/files` [human-eye], and the play-state-preserved check with a movie marked played right before removal. `JELLYFIN_MEDIA_ROOT` / `QBITTORRENT_DOWNLOAD_ROOT` env vars default to `/media` / `/downloads` — confirm they match harbour's mounts before the first live removal.

**Harness notes:** (1) cached plugin 0.9.2 `checkpoint` again omitted the vacated `doing/` path from its manifest — staged explicitly, git recorded the move as a rename. (2) Root `node_modules` junctioned into the worktree for `npm run build`, removed via `rmdir` before `git worktree remove`; main copy verified intact. (3) Board is empty; integration-mqsd3 (the detail-page action + confirmation dialog) sits in backlog blocked on this task, now unblocked for promotion. (4) vision.md has no "## Open questions" section, so the vacuum guard had nothing to surface.

---

## 2026-09-10 19:13 -- Task verified and completed: integration-r4vzm - Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071)

**Type:** Work / Task completion
**Task:** integration-r4vzm - Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071)
**Summary:** Shipped the server-side Remove local copy flow: planLocalCopyRemoval/removeLocalCopy API members over a pure LocalCopyRemoval.fs (mount-path mapping, deletion scope, torrent matching, seed-risk, plan/execute orchestrators), Jellyfin GET/DELETE /Items/{id} on withReauthRetry, per-item JellyfinStore clears, and the extracted JellyfinImport.syncMovieWatchHistory seam; no UI (integration-mqsd3)
**Duration:** 29m
**Verification:** PASS (iteration 1)
**Files changed:** 24
**Tests added:** 60
**ADRs written:** none

---

## 2026-09-10 18:43 -- Batch started: [integration-r4vzm]

**Type:** Work / Batch start
**Tasks:** integration-r4vzm - Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071)
**Parallel:** no (1 worker — integration-r4vzm is the only ready task across every BC; nothing held back)

---

## 2026-09-10 18:33 -- Modeling / Promoted: integration-r4vzm - Local copy removal, server side — a plan-then-execute flow (no UI) that imports the item's Jellyfin play state, deletes the acknowledged torrents with files from qBittorrent, DELETEs the Jellyfin item, verifies both gone, then clears the Jellyfin ids; pure `LocalCopyRemoval.fs` seams, Jellyfin DELETE support, per-item `JellyfinStore` clears (ADR-0071)

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-09-10 18:31 -- Modeling / Refined: integration-r4vzm - Local copy removal, server side (second pass, code-grounded after integration-qb7tk shipped)

**Type:** Modeling / Refine
**BC:** integration
**Status after:** todo (promoted in the same session — see the Promoted entry above)
**Summary:** Checked every seam the first pass named against the code as it stands after integration-qb7tk: the qBittorrent adapter, `JellyfinStore`, `JellyfinImport`, `Jellyfin.withReauthRetry`/`FetchError`, `JellyfinSync`'s in-progress flag, and the pinned test files all match. Two corrections: (1) torrents now match against the **deletion scope** (the movie's own folder, the bare file when it sits in a library root, the show folder for a series) instead of the Jellyfin file path — the first rule would have classified every ordinary one-folder movie torrent as a `PackAncestor` and pre-unticked it with a warning; the library-root rejection is now "fewer than two segments below the Jellyfin root" instead of hardcoded `movies`/`shows`; a hand-made mixed subfolder is an accepted, visible-by-name hazard. (2) The env-var precedent (`STEAM_DEVICE_NAME`) was deleted with integration-v0xmv; the mount roots now follow `Composition.fs`'s `TMDB_API_KEY`/`STEAM_ID` read without seeding SettingsStore. The two live-on-harbour criteria are marked builder-run after deploy so no worker touches the live system. Dependency integration-qb7tk is done; readiness gate passed.
**Split into:** none
**ADRs written:** none

---

## 2026-09-10 18:14 -- Work session ended

**Type:** Work / Session end
**Duration:** 41m (first "Batch started" 17:33 → 18:14)
**Completed:** 1 (first-try PASS: 0, re-dispatched: 1, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-qb7tk: 2
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is a qBittorrent adapter + Settings credentials card (integration BC, generic) feeding the "Remove local copy" workstream; it honors the Replayable design principle (third-party state cached, not evented — ADR-0071) and adds no Out-of-Scope (v1) item. vision.md still carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles". Note: the Out-of-Scope list still names "Jellyfin sync (v2)" although Jellyfin sync shipped long ago — a stale vision line, not a drift signal.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `formatBatchMixLine` from the source-repo `lib/vacuum-guard.mjs` (installed plugin 0.9.2 carries no such module).
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-04 10:55 boundary — all three commits (cbf3dbd capture, 14f09de refine, ab78eff promote) carry task trailers. Nothing to re-align.

**Verification history:** iteration 1 FAIL — acceptance criterion 2 (`testQbittorrentConnection` success + auth-failure shapes) had no test; everything else was already green. Iteration 2 added the two `testConnection` cases plus a SettingsStore round-trip for the three `qbittorrent_*` keys → PASS (705 tests, build clean).

**Builder eye-check pending:** criterion 9 (`[human-eye]`) — the qBittorrent card on Settings beside the Jellyfin card. Structural parity confirmed by the verifier (`integrationCard`/`statusBadge`/`feedbackAlert`/`Daisy.input`), not rendered. Also from the task Notes: once deployed, the harbour fleet docs get a one-line note that mediatheca holds qBittorrent credentials.

**Harness notes:** (1) cached plugin 0.9.2 `checkpoint` again omitted the vacated `doing/` path from its manifest — staged explicitly, git recorded the move as a rename. (2) `checkpoint`'s JSON opts choke on backslash-escaped Windows paths through Bash — forward-slash paths work. (3) Root `node_modules` junctioned into the worktree for `npm run build`, removed via `rmdir` before `git worktree remove`; main copy verified intact. (4) Board is empty after this task; integration-r4vzm (server-side removal flow) sits in backlog blocked on this task, now unblocked for promotion.

---

## 2026-09-10 18:11 -- Task verified and completed: integration-qb7tk - qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on

**Type:** Work / Task completion
**Task:** integration-qb7tk - qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on
**Summary:** Shipped the typed qBittorrent adapter (Qbittorrent.fs: login-once-use-once session, torrents/info + files decoders, deleteTorrents primitive, no Origin/Referer) with its Settings card and SettingsStore credential storage, mirroring the Jellyfin card without its ADR-0011 re-auth seam; iteration 2 closed the verifier gap with testConnection success/auth-failure coverage and a SettingsStore round-trip test
**Duration:** 38m
**Verification:** PASS (iteration 2)
**Files changed:** 10
**Tests added:** 12
**ADRs written:** none

---

## 2026-09-10 17:54 -- Verification failed: integration-qb7tk - qBittorrent adapter and Settings card

**Type:** Work / Verification failure
**Task:** integration-qb7tk - qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter that integration-r4vzm builds on
**Iteration:** 1 of 3
**Reasons:** Acceptance criterion 2 (`testQbittorrentConnection` returns Ok with app version + torrent count / Error naming authentication) has no test coverage — `Qbittorrent.testConnection` and its Api wrapper are never exercised; everything else passed (npm test 702/0, npm run build clean, scope confined, README updated, ADR-0071 point 7 honored)
**Iteration hint:** likely-fixable
**Next:** re-dispatched worker

---

## 2026-09-10 17:33 -- Batch started: [integration-qb7tk]

**Type:** Work / Batch start
**Tasks:** integration-qb7tk - qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on
**Parallel:** no (1 worker — integration-qb7tk is the only ready task across every BC; nothing held back)

---

## 2026-09-10 17:25 -- Modeling / Promoted: integration-qb7tk - qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on

**Type:** Modeling / Promote
**BC:** integration
**From → To:** backlog → todo

---

## 2026-09-10 17:40 -- Modeling / Refined: integration-mqsd3 - "Remove local copy" (split three ways: qb7tk adapter → r4vzm server flow → mqsd3 UI)

**Type:** Modeling / Refine
**BC:** integration
**Status after:** backlog (each part promoted separately, see the Promoted entries above)
**Summary:** Orchestrator + architect pass confirmed all five open questions — projection-only removal under ADR-0043 (no domain event; the item's play state is imported first through the existing event paths), warn-never-refuse hit-and-run with named constants, season/episode targets deferred, every matched torrent listed and acknowledged (the tick is not a selection), qBittorrent credentials in SettingsStore with an in-memory per-operation session. Two hazards added: pack torrents whose content path is an ancestor of the target (pre-unticked, extra-file count shown) and a time-of-check race closed by re-matching a fresh torrent list at execute time. Delete order fixed as torrents → Jellyfin item → verify → clear ids so the plan is always re-derivable after a partial failure (no saga state). Mount roots come from env vars with defaults; an unmapped path refuses loudly. The original task was too large for one worker session and is now three sequential tasks.
**Split into:** integration-qb7tk (qBittorrent adapter + Settings card), integration-r4vzm (server-side plan/execute flow, no UI), integration-mqsd3 (detail-page action + confirmation dialog)
**ADRs written:** 0071

---

## 2026-09-10 16:36 -- Modeling / Captured: integration-mqsd3 - "Remove local copy" button (qBittorrent + Jellyfin)

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** backlog
**Summary:** One-click removal of a local copy from the movie/series detail page: import the item's Jellyfin play state, match torrents by path, delete them with files from qBittorrent, DELETE the Jellyfin item, verify both are gone, then clear the Jellyfin id. Feasibility verified live on harbour (paths line up, Jellyfin user may delete, uid 1000 everywhere); open questions on hit-and-run policy, season granularity and event-vs-projection keep it in backlog.

---

## 2026-09-06 14:36 -- Work session ended

**Type:** Work / Session end
**Duration:** 46m (first "Batch started" 13:49 → 14:35)
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-qh8mj: 1, design-system-fryq7: 1
**Commits:** 5 (2 batch starts, 2 task completions, this entry)
**Vision-conformance:** none — batch aligns with vision. design-system-fryq7 is the vision's own sentence made concrete — "not a catalog to browse, but an intent-driven view of what's next": the three flat list pages and their menu items go, the Dashboard's per-media tabs remain the only browsing surface ("Unified, not siloed"). intelligence-qh8mj grows the Games tab ("Expandable over time") by relocating the Upcoming view rather than inventing a new surface, so nothing named under "Out of Scope (v1)" is touched. vision.md still carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (2 tasks) — `formatBatchMixLine` over `type: feature` (qh8mj) + `type: refactor` with product-only files (fryq7).
**Carry-over:** left behind (user WIP, 1 file — `src/Client/DesignSystem.fs`, the builder's own filmstrip `scrollbarHidden` edit, present since session start and named as such in fryq7's task Notes); no registered worktrees remain, `.worktrees/` removed.

**Verifier evidence beyond the workers' claims:** qh8mj — confirmed `PlayFacetsDisplay.releaseDateBadge` never degrades to `Html.none` for rail items because `getUpcomingGames` only returns `coming_soon = 1 OR parsed > now` rows; the rail's container class string is byte-identical to the Recently Played / Recently Added rails; `npm run build` clean, `npm test` 686/686 (+1). fryq7 — ran the acceptance grep for `format|navigate "movies|series|games"` in the worktree (empty), confirmed the only surviving `getUpcomingGames` hits are `GameProjection.getUpcomingGames` and its Dashboard caller, confirmed the two symbols the task assumed dead (`DesignSystem.statusBadgeLabel`, `PlayFacetsDisplay.facetBadges`) really have live `GameDetail` consumers (kept, named in the Outcome per the criterion); `npm run test:client` 29/29 across 7 files (up from 20/5), `npm test` 686/686 after one sanctioned re-run of a `JobRuns` timing flake.

**Bookkeeping repairs made on `main` this session (conductor, not worker):** (1) `.agentheim/knowledge/protocol.md` had lost the blank line after the header `---`, so `prependProtocolEntry`'s `\n---\n\n` anchor was inserting new entries below the 12:35 session-end and 12:10 capture entries; re-ordered those two into timestamp order and restored the blank line — every prepend since lands at the top again. (2) `applyReadmeDelta` splits on `\n` while every BC README is CRLF, so qh8mj's `append` disposed `appended-fallback` at end-of-file (moved into `## Ubiquitous language` by hand) and fryq7's `replace` disposed `merged` as a duplicate bullet (collapsed into a true replace by hand); the conductor's delta helper now normalizes CRLF before applying. Worth a plugin-side fix in `lib/readme-delta.mjs`. (3) Cross-BC README edits a worker cannot report in `README_DELTA` (games README "Release date" + "Play facets" bullets, made stale by qh8mj/fryq7) were applied by hand on `main` in fryq7's integrating commit.

**Browser verification not performed this session:** both tasks carry a [human-eye] criterion (Upcoming rail rhythm in the Games tab; Dashboard highlighted on a detail page). Verifiers proved the predicates and classes from source; one glance on the running app is still due.

**Harness notes:** root `node_modules` junctioned into each worktree via `mklink /J` and removed via `rmdir` before `git worktree remove` (main copy verified intact, 206 entries, twice). `scoped-commit` refuses paths whose deletion is already staged by the squash-merge (`pathspec did not match`) — pass only the non-deleted paths; the staged deletions ride the commit anyway. Board is empty after this session — no todo, doing, or backlog work in any BC.

---

## 2026-09-06 14:33 -- Task verified and completed: design-system-fryq7 - Remove the Movies / TV Series / Games items from the main menu (sidebar rail + mobile BottomNav) and delete their three list pages, plus every piece of code only those pages referenced — the Dashboard's per-media tabs already cover what they showed.

**Type:** Work / Task completion
**Task:** design-system-fryq7 - Remove the Movies / TV Series / Games items from the main menu (sidebar rail + mobile BottomNav) and delete their three list pages, plus every piece of code only those pages referenced — the Dashboard's per-media tabs already cover what they showed.
**Summary:** Removed the Movies / TV Series / Games items from the sidebar rail and BottomNav and deleted their three list pages plus every symbol only they kept alive; bare /movies|/series|/games URLs resolve to the Dashboard with the matching tab, and the global search modal now fetches its library snapshot on open
**Duration:** 33m
**Verification:** PASS (iteration 1)
**Files changed:** 28
**Tests added:** 9
**ADRs written:** none

---

## 2026-09-06 14:07 -- Batch started: [design-system-fryq7]

**Type:** Work / Batch start
**Tasks:** design-system-fryq7 - Remove the Movies / TV Series / Games items from the main menu (sidebar rail + mobile BottomNav) and delete their three list pages, plus every piece of code only those pages referenced — the Dashboard's per-media tabs already cover what they showed.
**Parallel:** no (1 worker — design-system-fryq7 became the only ready task once intelligence-qh8mj landed in done/; no other todo task exists in any BC)

---

## 2026-09-06 14:04 -- Task verified and completed: intelligence-qh8mj - Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.

**Type:** Work / Task completion
**Task:** intelligence-qh8mj - Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.
**Summary:** Dashboard Games tab gains an Upcoming poster rail (soonest-first, TBA last) fed by the existing GameProjection.getUpcomingGames through a new DashboardGamesTab.Upcoming field, absent entirely when nothing is unreleased
**Duration:** 14m
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 1
**ADRs written:** none

---

## 2026-09-06 13:49 -- Batch started: [intelligence-qh8mj]

**Type:** Work / Batch start
**Tasks:** intelligence-qh8mj - Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.
**Parallel:** no (1 worker — intelligence-qh8mj is the only ready task; design-system-fryq7 is blocked on it via depends_on and joins the next wave once it lands)

---

## 2026-09-06 13:40 -- Modeling / Refined: design-system-fryq7 - Remove the Movies / TV Series / Games items from the main menu and delete their three list pages

**Type:** Modeling / Refine
**BC:** design-system
**Status after:** todo
**Summary:** Builder resolved the flagged assumption — the Games list page's Upcoming section survives. fryq7 now depends_on intelligence-qh8mj (which moves Upcoming onto the Dashboard Games tab), keeps GameProjection.getUpcomingGames and its Expecto cases, and deletes only the now-unreferenced IMediathecaApi.getUpcomingGames endpoint; PlayFacetsDisplay.releaseDateBadge is expected to survive as the Dashboard rail's badge.
**Split into:** none (companion task intelligence-qh8mj captured separately)
**ADRs written:** none

---

## 2026-09-06 13:40 -- Modeling / Captured: intelligence-qh8mj - Dashboard Games tab gains an "Upcoming" poster rail — the unreleased-games view (soonest-first, TBA last) currently living only on the Games list page moves here before that page is deleted.

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Builder chose to keep the Games list page's Upcoming section when design-system-fryq7 deletes that page: this task moves it onto the Dashboard Games tab first — a new Upcoming field on DashboardGamesTab fed by the existing, tested GameProjection.getUpcomingGames, rendered as a poster rail in the Games tab's section-card chrome, absent when empty. Filed to todo; it blocks design-system-fryq7. No orchestrator round, no ADR.

---

## 2026-09-06 13:35 -- Modeling / Captured: design-system-fryq7 - Remove the Movies / TV Series / Games items from the main menu (sidebar rail + mobile BottomNav) and delete their three list pages, plus every piece of code only those pages referenced — the Dashboard's per-media tabs already cover what they showed.

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** Builder asked to remove the Movies / TV Series / Games items from the main menu and their target list pages, plus dead code only those pages referenced. Routed to design-system (it owns the sidebar rail + BottomNav); filed straight to todo — the Dashboard's per-media tabs already replace the pages, and the only real entanglement is the global SearchModal being seeded from the three list models, which the task specifies as a fetch-on-open. Flagged that the Games page's Upcoming section (games-ev65k) and getUpcomingGames go with it. Styleguide gate met (design-system-001 done). No orchestrator round, no ADR.

---

## 2026-09-06 12:35 -- Work session ended

**Type:** Work / Session end
**Duration:** 30m (first "Batch started" 12:05 → 12:35)
**Completed:** 2 (first-try PASS: 2, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-c3vqm: 1, design-system-hs4vm: 1
**Commits:** 5 (2 batch starts, 2 task completions, this entry) — plus one concurrent `modeling` capture commit (f23d2a9, design-system-hs4vm) that landed on `main` mid-run and was picked up as a new ready task after intelligence-c3vqm completed
**Vision-conformance:** none — batch aligns with vision. Both tasks are visual polish inside the Unified Dashboard workstream: intelligence-c3vqm pins All-tab game posters to the movie filmstrip's size and drops a badge that carried no information (In Focus is already expressed by which section the game sits in — the "Intent-driven" principle survives intact); design-system-hs4vm replaces dead `tailwind-scrollbar` classes on six existing rails with a working design-system primitive and adds no dependency. Neither touches anything named under "Out of Scope (v1)". vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)"/"Design Principles", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (2 tasks, both `type: bug` — one intelligence, one design-system) — hand-classified.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Verifier evidence beyond the workers' claims:** c3vqm — confirmed Tailwind actually emits the arbitrary `grid-cols-[repeat(auto-fill,minmax(0,130px))]` rule into the built stylesheet (not a dead string), that `movieToWatchPosterCard`/`movieToWatchFilmstripItem` keep their conditional crosshair, and that `Icons.crosshairSmFilled` still has three references in Views.fs; `npm test` 685/685. hs4vm — confirmed `.scrollbar-hidden` and its `::-webkit-scrollbar` rule land in the built CSS, the acceptance grep for `scrollbar-thin|-thumb|-track` is empty across `src/Client/`, `package.json`/`package-lock.json` untouched, `index.css` still has exactly two `@plugin` lines, and all six rails keep `overflow-x-auto snap-x snap-mandatory scroll-px-2` byte-identical; `npm run build` clean, `npm test` 685/685.

**Verifier observation, not acted on (scope):** the first `npm test` run for c3vqm errored once on `JobRuns.reconciliation only runs at startup: a row that's genuinely running (guard held) is left alone by a later read` — a `NullReferenceException` in `SqliteConnection.Close()` inside `TempDb.Dispose` (`tests/Server.Tests/TestDb.fs:69`), a teardown race between the test's `Async.Start` background job and scope disposal. Re-run was fully green; hs4vm's run did not reproduce it. Server-side, unrelated to either client diff — a candidate for a small `modeling` capture to de-flake `TestDb.TempDb.Dispose`.

**Browser verification not performed this session:** both changes are viewport-dependent visuals (poster cap across sm/lg/xl; scrollbar paint under the rails). The verifiers proved them from the emitted CSS rather than a running browser, and the local dev database is still empty (populated rails would not render anyway). Worth one glance on the deployed build.

**Harness notes:** (1) Each worktree again needed the root `node_modules` junctioned from the main tree (`mklink /J`, removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries, twice). (2) The `checkpoint`/`complete` verbs' opts JSON was written to a scratch file and read back via `process.argv[1]` (not `[2]` — `node -e` shifts argv), as in prior sessions. (3) Board is empty after this session — no todo, doing, or backlog work in any BC.

---
## 2026-09-06 12:10 -- Modeling / Captured: design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail

**Type:** Modeling / Capture
**BC:** design-system
**Filed to:** todo
**Summary:** Builder asked for the Dashboard scrollbars under episodes and movies to be invisible. Investigation found the cause is dead code, not missing styling: all six horizontal poster rails in `Dashboard/Views.fs` carry `scrollbar-thin/-thumb/-track` utilities from the `tailwind-scrollbar` plugin, which is absent from both `package.json` and `index.css`'s `@plugin` directives — so Tailwind 4 emits nothing and the native scrollbar renders. Routed to design-system (scrollbar chrome is cross-cutting visual language) as a `.scrollbar-hidden` / `DesignSystem.scrollbarHidden` primitive covering Chromium and WebKitGTK, adopted at all six rails, with no new plugin dependency. Filed straight to todo — concrete and unambiguous. No orchestrator round, no ADR, no prior art (nothing in any BC has touched scrollbars before).

---

## 2026-09-06 12:34 -- Task verified and completed: design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail

**Type:** Work / Task completion
**Task:** design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail
**Summary:** Minted the .scrollbar-hidden / DesignSystem.scrollbarHidden hidden-scrollbar primitive (Chromium + WebKitGTK) and adopted it at all six Dashboard poster rails, retiring the dead tailwind-scrollbar plugin classes tree-wide.
**Duration:** 15m
**Verification:** PASS (iteration 1)
**Files changed:** 5
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 12:18 -- Batch started: [design-system-hs4vm]

**Type:** Work / Batch start
**Tasks:** design-system-hs4vm - Hidden-scrollbar primitive — the Dashboard's six horizontal poster rails carry `tailwind-scrollbar` classes for a plugin that was never installed, so the native scrollbar renders under every rail
**Parallel:** no (1 worker — design-system-hs4vm is the only ready task across every BC after the intelligence-c3vqm re-scan; it was captured to todo by a concurrent modeling session mid-run)

---

## 2026-09-06 12:17 -- Task verified and completed: intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair

**Type:** Work / Task completion
**Task:** intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair
**Summary:** Capped All-tab game poster tiles to a 130px auto-fill grid track (matching the movie filmstrip's fixed 196px height via the existing 2/3 aspect ratio) and dropped the redundant unconditional crosshair badge from game poster cards.
**Duration:** 12m
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 12:10 -- Modeling / Captured: intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Two builder complaints about the All tab's Games section, captured as one task after verifying both in code. (1) Game posters are a `grid-cols-2 sm:grid-cols-3` of `1fr` tracks over an `aspect-ratio: 2/3` container, so they scale with the section width and swing hard between `sm` and `xl` (the Games/Books split stays single-column until `xl`) — where movie posters are pinned to the filmstrip's fixed `h-[196px]`. Fix is a hard 130×196 cap. (2) `gameInFocusPosterCard` renders the crosshair badge unconditionally — for games `InFocus` *is* the status and the section lists only InFocus games, so it distinguishes nothing; same reasoning `intelligence-f6cfv` applied to the Next-episode card. The conditional badge on `movieToWatchPosterCard` stays. Filed straight to todo — one file, code facts verified, no orchestrator round needed.

---
## 2026-09-06 11:50 -- Work session ended

**Type:** Work / Session end
**Duration:** 13m (first "Batch started" 11:37 → 11:50)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-p4t7k: 1
**Commits:** 3 (1 batch start, 1 task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. intelligence-p4t7k is a pure server-side deletion of a payload the builder already retired (via intelligence-wecjh); it removes per-load work from the Unified Dashboard's All-tab endpoint and touches nothing named under "Out of Scope (v1)". vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task, `type: refactor` in intelligence) — hand-classified.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Verifier evidence beyond the worker's claim:** re-ran `npm test` (685 passed) and `npm run build` (clean Fable compile) from the worktree, confirmed `resolveFriendRefs` survives (GameProjection.fs:527, four call sites in the game-detail projection at 763–766), and confirmed the tree-wide grep for `NewGames|DashboardNewGame|getDashboardNewGames` is empty across `src/` and `tests/`.

**Harness notes:** (1) Worktree again needed the root `node_modules` junctioned from the main tree (`mklink /J`, removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries). (2) The `checkpoint` verb's opts JSON was written to a scratch file with forward-slash paths and read back, as in prior sessions. (3) The `work` SKILL.md references `skills/work/references/worker-return-format.md`; in plugin 0.9.2 the file actually lives at the plugin root `references/worker-return-format.md`. (4) Board is empty after this session — no todo or backlog work remains in any BC's `todo/`.

---
## 2026-09-06 11:05 -- Modeling / Refined: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Modeling / Refine
**BC:** intelligence
**Status after:** backlog
**Summary:** Code-fact refinement, no orchestrator round needed. Corrected the record name — the dead field lives on `DashboardAllTab` (the All-tab payload), not `DashboardGameStats` (the Games-tab stats block, untouched). Verified `DashboardNewGame` has only two references (definition + mapper) so it is deleted too; `resolveFriendRefs` stays (still used by the game-detail projection); no test references `DashboardAllTab`; no client code reads `NewGames`. Added the README "Retired" note correction to scope and a grep-clean criterion. No split, no ADR.
**Split into:** none
**ADRs written:** none

---

## 2026-09-06 12:05 -- Batch started: [intelligence-c3vqm]

**Type:** Work / Batch start
**Tasks:** intelligence-c3vqm - Dashboard All-tab Games — cap poster size to the movie-poster size and drop the redundant In Focus crosshair
**Parallel:** no (1 worker — intelligence-c3vqm is the only ready task across every BC, nothing held back)

---

## 2026-09-06 11:49 -- Task verified and completed: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Work / Task completion
**Task:** intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it
**Summary:** Pruned the dead New Games dashboard payload end to end — removed Shared.DashboardNewGame and DashboardAllTab.NewGames, deleted GameProjection.getDashboardNewGames, removed its call and assignment in Api.fs getDashboardAllTab, and corrected the intelligence README Retired note to name DashboardAllTab and state the prune is complete.
**Duration:** 10m
**Verification:** PASS (iteration 1)
**Files changed:** 4
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 11:38 -- Batch started: [intelligence-p4t7k]

**Type:** Work / Batch start
**Tasks:** intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it
**Parallel:** no (1 worker — intelligence-p4t7k is the only ready task across every BC, nothing held back)

---

## 2026-09-06 11:28 -- Modeling / Promoted: intelligence-p4t7k - Prune the New Games dashboard payload — server still computes and ships DashboardAllTab.NewGames but no client code reads it

**Type:** Modeling / Promote
**BC:** intelligence
**From → To:** backlog → todo

---

## 2026-09-06 10:37 -- Work session ended

**Type:** Work / Session end
**Duration:** 49m (first "Batch started" 09:48 → 10:37)
**Completed:** 3 (first-try PASS: 3, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** intelligence-encn4: 1, intelligence-wecjh: 1, intelligence-f6cfv: 1
**Commits:** 7 (3 batch starts, 3 task completions, this entry) — plus one concurrent `modeling` capture commit (a6ba85a, intelligence-f6cfv) that landed on `main` mid-run and was picked up as a new ready task after intelligence-wecjh completed
**Vision-conformance:** none — batch aligns with vision. All three tasks sit inside the Unified Dashboard workstream (All-tab section chrome, Dashboard/Views.fs code health, Next episode card). The All-tab semantics the vision names survive: the Games section still shows only In Focus games (heading shortened, content unchanged), the Next episode row still shows watch-with friends (as avatars instead of name pills), and the In focus badge drop removes a duplicate signal, not the In Focus sort. The Books placeholder chrome change is a visual stub already introduced by intelligence-dq8rk, not Books (v2) scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (3 tasks, all `type: refactor` in intelligence) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Builder decision recorded mid-run:** intelligence-wecjh's open question (re-wire `newGamesSection` on the Games tab vs. confirm the drop) was put to the builder before dispatch; answer: **confirm the drop**. Both helpers are deleted, the retirement is recorded in the intelligence README, and the worker filed `intelligence-p4t7k` (backlog) for the server-side `NewGames` payload prune.

**Browser smoke check (conductor, Chrome DevTools MCP, served from `main` via `dotnet src/Server/bin/Debug/net9.0/Server.dll` run from the repo root):** after intelligence-wecjh, all four dashboard tabs (All / Movies / TV Series / Games) rendered with their expected sections and empty states and **zero console errors or warnings**; after intelligence-f6cfv, the rebuilt bundle loaded with zero console errors and the All tab's Books column renders as `.section-open` with no `.velvet-card` in `main` (intelligence-encn4 confirmed live). **Caveat:** the local dev database (`~/app/mediatheca/mediatheca.db`) is empty — `getMovies`/`getSeries`/`getGames` all return `[]` — so populated render paths (poster grids, Next episode cards, the new avatar stack) were not exercised in the browser. wecjh's identical-render criterion rests on the verifier's pure-deletion proof (`--diff-algorithm=patience` → 0 added lines, all 54 surviving definitions byte-identical); f6cfv's avatar look is unverified in a browser and worth a glance on the deployed build.

**Verifier observation, not acted on (scope):** `friend.Name.Substring(0, 1)` in `DesignSystem.nextEpisodeHeroCard` throws on an empty friend name; `Friend_added` has no server-side non-empty-name guard. Not reachable via the normal add-friend flow — a candidate for a small `modeling` capture (guard the initial fallback, or validate the name at the aggregate).

**Harness notes:** (1) `dotnet run --project src/Server/Server.fsproj` sets CWD to `src/Server`, and `Composition.fs` resolves static files from `CWD/deploy/public` — the SPA 404s unless the server is launched from the repo root. (2) Each worktree again needed the root `node_modules` junctioned from the main tree (`mklink /J`, removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries, three times). (3) The `checkpoint` verb's opts JSON was written to a scratch file with forward-slash paths and read back, as last session. (4) Myers diff of a ~2000-line deletion showed 166 spurious `+` lines; `--diff-algorithm=patience` gave the true pure-deletion view and was handed to the verifier. (5) Board is empty after this session — `intelligence-p4t7k` sits in backlog, unrefined.

---


## 2026-09-06 10:34 -- Task verified and completed: intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped

**Type:** Work / Task completion
**Task:** intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped
**Summary:** Reworked the Dashboard Next episode hero card so watched-with friends render as an overlapping circular avatar stack pinned top-left (image or initial fallback, click-through to the friend page preserved), and dropped the In-focus badge from the card and its props; ring-white/30 recorded as a card-local adaptation in the intelligence README.
**Duration:** 17m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 10:21 -- Batch started: [intelligence-f6cfv]

**Type:** Work / Batch start
**Tasks:** intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped
**Parallel:** no (1 worker — intelligence-f6cfv was promoted mid-run by a concurrent modeling session and became ready when intelligence-wecjh landed; it is the only ready task across every BC, nothing held back)

---

## 2026-09-06 10:20 -- Task verified and completed: intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild

**Type:** Work / Task completion
**Task:** intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild
**Summary:** Deleted all 43 unreachable view helpers (including newGamesSection/newGameItem, confirmed retired by the builder) from Dashboard/Views.fs, shrinking it from 4418 to 2427 lines with a zero-unreferenced-definitions scan, and recorded the New Games retirement in the intelligence BC README; follow-up intelligence-p4t7k captures the server-side NewGames payload prune.
**Duration:** 21m
**Verification:** PASS (iteration 1)
**Files changed:** 3
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 10:12 -- Modeling / Captured: intelligence-f6cfv - Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** The hero card built by intelligence-h7v2q renders watched-with friends as name pills at the *bottom* of the scrim, last in a four-item stack — the people you watched with end up the least prominent thing on the card while the tall overlay eats the backdrop. They move to the card's top-left as avatar-only circles (~40px, matching the Jellyfin play button opposite them), overlapping stack with a ring separator per the styleguide `heroCard` pattern, image where available and the uppercased first letter otherwise; the name drops out of the visual and survives as `title`/`alt`, and the friend-link click semantics (preventDefault + stopPropagation, no card click-through) are preserved verbatim. Builder decided in the modeling session that the **In focus badge is no longer needed on this card** — In Focus already earns those series their position through sorting — so the badge block and the `InFocus` prop are deleted outright, which is what frees the corner; `statusBadge` itself stays for the styleguide `heroCard`. Concrete enough for todo directly, orchestrator skipped. Sequenced behind `intelligence-wecjh` (in flight, deletes ~2000 line-referenced lines of the same `Dashboard/Views.fs` this task edits); Series-tab list rows and their page-local `friendPill` are explicitly out of scope.

---

## 2026-09-06 09:57 -- Batch started: [intelligence-wecjh]

**Type:** Work / Batch start
**Tasks:** intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild
**Parallel:** no (1 worker — intelligence-wecjh became ready when intelligence-encn4 landed; it is the only remaining task across every BC, nothing held back)

---

## 2026-09-06 09:57 -- Task verified and completed: intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"

**Type:** Work / Task completion
**Task:** intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"
**Summary:** On the Dashboard All tab, the Games and Books columns now render as open sections (no card chrome) matching the Next episode / Movies to Watch rows above them, and the Games heading was shortened from "Games In Focus" to "Games"; the Games tab keeps its card chrome.
**Duration:** 8m20s
**Verification:** PASS (iteration 1)
**Files changed:** 1
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-06 09:48 -- Batch started: [intelligence-encn4]

**Type:** Work / Batch start
**Tasks:** intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"
**Parallel:** no (1 worker — intelligence-encn4 is the only ready task across every BC; intelligence-wecjh is blocked behind it via depends_on and is picked up next wave)

---

## 2026-09-06 09:31 -- Modeling / Captured: intelligence-wecjh - Dashboard Views.fs — delete the ~2000 lines of unreferenced view helpers left behind by the 3a rebuild

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** Follow-up to the dead `gamesInFocusSection` spotted while scoping intelligence-encn4 — a transitive-closure scan of `Dashboard/Views.fs` found the single dead helper is one of **43 unreferenced top-level definitions, ~2000 of the file's 4419 lines (45%)**, residue from intelligence-dq8rk's 3a rebuild changing call sites without deleting definitions (F# does not warn on unused module-private lets, so `npm run build` never flagged it). Task carries the full verified inventory with line ranges and a bottom-up deletion order. Two findings surfaced while scoping and recorded rather than acted on: (1) `newGamesSection` is a **latent regression** — dq8rk explicitly kept New Games on the Games tab but `gamesTabView` never calls it, so the worker must ask the builder whether to re-wire or confirm the drop; (2) the server still projects and ships the unrendered `NewGames` payload (`Api.fs:2162`), left out of scope as a follow-up since it touches the `IMediathecaApi` contract. Sequenced behind intelligence-encn4, whose edits are line-referenced into the same file.

---


## 2026-09-06 09:12 -- Modeling / Captured: intelligence-encn4 - Dashboard All-tab — Games and Books drop the card chrome and "Games In Focus" is renamed to "Games"

**Type:** Modeling / Capture
**BC:** intelligence
**Filed to:** todo
**Summary:** The All tab's Games and Books columns still carry velvet-card chrome from the original 3a build while the TV and Movies rows above them are open sections, so the landing page reads as two flat rows followed by two boxes; both columns swap `sectionCard` for the existing `sectionOpen` helper and the Games heading loses its now-redundant "In Focus" qualifier (the All tab shows nothing but In Focus games). Two-line composition change in `Dashboard/Views.fs`, no CSS and no new helper — concrete enough for todo directly, orchestrator skipped. Noted in the task: `gamesInFocusSection` (the list-row variant) is dead code with no callers, deliberately left out of scope.

---

## 2026-09-06 00:28 -- Work session ended

**Type:** Work / Session end
**Duration:** 33m (first "Batch started" 23:54 → 00:28)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-v0xmv: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is an integration refactor that removes the Steam login/token-mint path and keeps the Steam Family import alive on a user-pasted browser token — it protects the account the whole Steam Import workstream depends on, touches no Out-of-Scope (v1) item and adds no admin-console scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)", as in prior sessions.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: refactor` → product-facing.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed; the now-empty `spikes/` directory left by the squash-merge's file deletions is gone too.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-04 10:55 boundary — the single commit (85f103d, the integration-v0xmv capture) carries its task trailer. Nothing to re-align.

**Builder-side, outside the task (from integration-v0xmv Notes):** (1) at `store.steampowered.com/twofactor/manage` remove the "Mediatheca" / "… (SteamKit2)" authorized devices; (2) deploy this change **before** the next family import; (3) fetch a fresh `webapi_token` from your own logged-in browser (`store.steampowered.com/pointssummary/ajaxgetasyncconfig`) and paste it in Settings → Steam Family — never script that step; (4) do not press any Steam-related button on the old deployed build in the meantime. First startup of the new build deletes the stored `steam_family_refresh_token`.

**Harness notes:** (1) the `checkpoint` verb's opts JSON had to be written to a scratch file and read back — Windows backslash paths in an inline bash argument fail its JSON parse; forward-slash paths work. (2) The worktree again needed the root `node_modules` junctioned from the main tree for `npm run build` (removed via `rmdir` before `git worktree remove`; main copy verified intact, 206 entries). (3) Board is empty after this task — no ready work in any BC.

---

## 2026-09-06 00:27 -- Task verified and completed: integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again

**Type:** Work / Task completion
**Task:** integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again
**Summary:** Removed the Steam Connect QR login and the refresh-token mint path end to end (SteamConnect.fs, SteamKit2/QRCoder deps, the spike scripts, the mint-and-retry seam) so the Steam Family import runs only on a browser-obtained access token pasted by the user in Settings; wrote ADR-0070, superseded ADR-0061, and amended ADR-0019/ADR-0067 in place.
**Duration:** 32m13s
**Verification:** PASS (iteration 1)
**Files changed:** 25
**Tests added:** 11
**ADRs written:** 0070 (new); 0019, 0061, 0067 amended in place

---

## 2026-09-05 23:54 -- Batch started: [integration-v0xmv]

**Type:** Work / Batch start
**Tasks:** integration-v0xmv - Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again
**Parallel:** no (1 worker — integration-v0xmv is the only ready task across every BC; nothing held back)

---

## 2026-09-05 23:50 -- Modeling / Captured: integration-v0xmv - Remove the Steam Connect QR login and refresh-token mint path; Steam Family import runs only on a browser-obtained access token pasted in Settings

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** Valve escalated from three hijack alerts to a permanent-ban threat for further misuse of the login API (2026-09-05). ADR-0067's amendment had already pinned the third alert to the SteamKit2 QR ceremony, so the task deletes `SteamConnect.fs`, the `/api/stream/steam-connect` route, the SteamKit2/QRCoder packages, the refresh-token mint path (`mintFamilyAccessToken`, `withTokenRefresh`, `*WithRefresh`), the stored `steam_family_refresh_token` and the spike scripts, and re-promotes the paste-a-token flow (with `ajaxgetasyncconfig` → `webapi_token` instructions) as the only way in. Compliance check of the remaining import shape against the Steam Web API Terms of Use recorded in the task: 2–3 read-only `IFamilyGroupsService` GETs on the user's own browser token, key-authenticated `GetOwnedGames`, throttled public `appdetails`; no login API call of any kind. Worker writes an ADR superseding 0061 and amending 0019/0067 (ladder retired; browser-retrieval fallback closed as will-not-build). Concrete enough for todo directly; orchestrator skipped — the removal seam was fully inventoried from source during capture.

---

## 2026-09-04 10:55 -- Work session ended

**Type:** Work / Session end
**Duration:** 19m (first "Batch started" 10:36 → 10:55)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** integration-zwnh4: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is an integration bug fix (Steam Connect QR login sends a fixed device identity; ADR-0067 amended with the corrected home-IP hypothesis) — it serves the Steam Import workstream, touches no Out-of-Scope (v1) item and adds no admin-console scope. vision.md carries no "What success looks like"/"Non-goals" headings, so the pass was judgment against "Remaining v1 Work"/"Out of Scope (v1)".
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — `formatBatchMixLine` from the source-repo `lib/vacuum-guard.mjs` (installed plugin 0.9.2 carries no such module).
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 2026-09-03 20:14 boundary — the single commit (02ad2a9, the integration-zwnh4 capture) carries its task trailer. Nothing to re-align.

**Verifier observation, not acted on (scope):** ADR-0019 points 2 and 4 still carry the now-retracted "MobileApp-from-datacenter-IP" framing and call browser retrieval "escalation-ladder step 2" (it is step 3 after the ADR-0067 amendment). ADR-0061 Consequences has the same datacenter wording. Neither was in integration-zwnh4 sync list — a candidate for a small follow-up capture via `modeling`.

**Builder-side, outside the task (from integration-zwnh4 Notes):** after Steam recovery, check `store.steampowered.com/twofactor/manage` for the `(SteamKit2)` devices and `steamcommunity.com/dev/apikey` for an unrecognised key; deploy this change before the next Connect Steam; do not click Connect Steam unless an import reports "reconnect required". A fourth alert after this is live for one ordinary usage cycle is the trigger to climb the amended ladder.

**Harness notes:** (1) cached plugin 0.9.2 `checkpoint` again omitted the vacated `doing/` path from its manifest — staged explicitly, git recorded the move as a rename. (2) The worktree needed the root `node_modules` junctioned from the main tree for `npm run build` (removed via `rmdir` before `git worktree remove`; main copy verified intact). (3) Board is empty after this task — no ready work in any BC.

---

## 2026-09-04 10:53 -- Task verified and completed: integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert

**Type:** Work / Task completion
**Task:** integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert
**Summary:** The Steam Connect QR login now sends a fixed, honest device identity (a "Mediatheca" device name, STEAM_DEVICE_NAME-overridable, and a Mobile website id matching the MobileApp platform) instead of SteamKit2 per-deploy container-hostname defaults, and ADR-0067 is amended to retract the disproven datacenter-IP hypothesis in favor of the device-fingerprint one after the third Valve alert.
**Duration:** 18m
**Verification:** PASS (iteration 1)
**Files changed:** 6
**Tests added:** 8
**ADRs written:** none

---

## 2026-09-04 10:36 -- Batch started: [integration-zwnh4]

**Type:** Work / Batch start
**Tasks:** integration-zwnh4 - Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert
**Parallel:** no (1 worker — integration-zwnh4 is the only ready task across every BC; nothing held back)

---

## 2026-09-04 10:35 -- Modeling / Captured: integration-zwnh4 - Steam Connect QR login gets a stable, honest device identity; ADR-0067 amended after the third Valve alert

**Type:** Modeling / Capture
**BC:** integration
**Filed to:** todo
**Summary:** The third Valve account alert (2026-09-03 18:54 CEST, Muenster DE) was traced in production container logs to the Connect Steam QR ceremony at 16:54:30 UTC on a container redeployed at 16:39 UTC — not to the import. The alert location proves the production host is residential, disproving ADR-0067's datacenter-IP hypothesis; the enumeration fixes (ADR-0066/0069) were already live. What SteamKit2 sends by default is the real fingerprint: DeviceFriendlyName = "{MachineName} (SteamKit2)" (the random container id per deploy), WebsiteID "Client" on a MobileApp/Android9 session. The task fixes SteamConnect.fs (fixed name, "Mobile" website id, fixed OS type, pure + tested), amends ADR-0067 in place (retract the IP half, discharge ladder step 1, insert this as the cheapest rung before browser retrieval, add the reconnect-loop note), and syncs the README/concept prose. Concrete enough for todo directly.

---

## 2026-09-03 20:14 -- Work session ended

**Type:** Work / Session end
**Duration:** 36m (first "Batch started" 19:38 → 20:14)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** games-t69rb: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is a Games detail-page UX change (persistent card column, Journal-first default tab) — media-experience work; it touches no Out-of-Scope (v1) item (no trailer playback added, no Books/Trakt/intelligence scope) and adds no admin-console scope.
**Batch mix:** 100% product-facing / 0% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: feature` → product-facing.
**Carry-over:** none — working tree clean, no registered worktrees remain. A stray unregistered `.worktrees/infrastructure-r8kqt/src/Server/obj/` residue (three dotnet build files locked during that session's teardown, 17:36) was found and deleted; `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 0 human commits since the 18:00 boundary — all six commits carry task trailers (infrastructure-r8kqt runbook records, administration-b3xqf rotation, games-t69rb capture). Nothing to re-align.

**Human-eye pending (games-t69rb):** "switching tabs does not visibly reflow or flash the card column" is unticked in the task file — builder check on a real game page. The Playwright spec `tests/e2e/game-detail-persistent-cards.spec.ts` was written but not executed by the verifier (build + Expecto 685/685 + Vitest 16/16 were).

**Harness notes:** (1) plugin 0.9.2's `checkpoint` manifest again did not fold in the vacated `doing/` path — staged explicitly, git recorded the move as a rename. (2) Worker's `FILES_CHANGED: 20` undercounted its own 22-path FILE_LIST (13 of them one-line `GameJournal.initialize` additions to server test fixtures, mechanical from the new DTO field). (3) Board is empty after this task — no ready work in any BC; vision.md has no "Open questions" section for the vacuum guard to surface.

---

## 2026-09-03 20:11 -- Task verified and completed: games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise

**Type:** Work / Task completion
**Task:** games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
**Summary:** The game detail page two-column grid is now the unconditional page frame — the right-hand card column (Links, play facets, friends, catalogs) stays mounted at the same DOM node across the Overview/Journal tabs — and the page opens Journal-first when the game journal document already has non-blank content, computed server-side via a new shared JournalBlock.hasContent rule.
**Duration:** 32m
**Verification:** PASS (iteration 1)
**Files changed:** 20
**Tests added:** 9
**ADRs written:** none

---

## 2026-09-03 19:38 -- Batch started: [games-t69rb]

**Type:** Work / Batch start
**Tasks:** games-t69rb - Game detail page — keep the right-hand card column (Links, play facets, friends, …) mounted across the Overview/Journal tabs so switching only swaps the content column, and open on the Journal tab when the game's journal document already has content, Overview otherwise
**Parallel:** no (1 worker — games-t69rb is the only ready task across every BC; nothing held back)

---

## 2026-09-03 19:25 -- Modeling / Captured: games-t69rb - Game detail page — persistent right-hand card column across Overview/Journal tabs, Journal-first when the game has journal content

**Type:** Modeling / Capture
**BC:** games
**Filed to:** todo
**Summary:** The game detail page's right-hand cards (Links, play facets, friends, catalogs, …) currently live inside the Overview tab arm and vanish on the Journal tab; the task lifts them out so the tab only swaps the content column. Adds a once-per-load default-tab rule: land on Journal when the game's journal document has a non-blank block, Overview otherwise, recommended via a HasJournalContent flag on the GameDetail DTO. Concrete enough to file straight to todo (styleguide gate design-system-001 already done).

---

## 2026-09-03 18:00 -- Work session ended

**Type:** Work / Session end
**Duration:** 5m (first "Batch started" 17:55 → 18:00)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** administration-b3xqf: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task is a documentation-only chore (README entry reframed as history, ADR-0058 retirement note); it adds no admin-console scope, pulls toward no Out-of-Scope (v1) item, and moves nothing away from Remaining v1 Work.
**Batch mix:** 0% product-facing / 100% harness / 0% bookkeeping (1 task) — `type: chore` touching a BC README and an ADR, which the helper's heuristic classes as harness rather than bookkeeping.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** 0 recognized machine-shape commits, 1 human commit since the 17:40 boundary — `2f7e74a Merge branch 'fix-cutover'`, the builder's merge of the previous session's branch into main; it carries no content of its own beyond the already-trailed task commits it integrates. Touches no governed surface; nothing to re-align.

**Builder runbook still pending (from infrastructure-r8kqt):** Part B — delete the three server backups under `/app/data/backups` and the dev-machine stale inventory under `C:\Users\marco\app\mediatheca\backups\`; then `/deploy` and confirm the first boot logs zero `[StartupCutover]` lines with drift 0/7.

**Harness notes:** (1) the `checkpoint` verb rejected a fileList JSON with backslash-escaped `C:\...` paths when passed through Git Bash (`invalid-opts-json`); forward-slash drive-letter paths (`C:/...`) worked. (2) The source-repo `lib/task-lifecycle-cli.mjs` (0.9.2 tree) DID fold the vacated `doing/` path into the checkpoint manifest this time, unlike last session's cached plugin. (3) Board is empty after this task — no ready work remains in any BC.

---

## 2026-09-03 18:00 -- Task verified and completed: administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt

**Type:** Work / Task completion
**Task:** administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
**Summary:** Reframed the administration README's Offline demoted-event filter bullet as settled history (purge executed 2026-08-05, tooling retired by infrastructure-r8kqt on 2026-09-03, pointers to the runbook and ADR-0058) and appended a Retirement note to ADR-0058 recording both the CLI subcommand and StartupCutover.fs's playSessionPhase guard as deleted.
**Duration:** 3m30s
**Verification:** PASS (iteration 1)
**Files changed:** 2
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-03 17:55 -- Batch started: [administration-b3xqf]

**Type:** Work / Batch start
**Tasks:** administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt
**Parallel:** no (1 worker — administration-b3xqf is the only ready task across every BC; nothing held back)

---

## 2026-09-03 17:51 -- Modeling / Promoted: administration-b3xqf - Update the administration README's Offline demoted-event filter entry — EventLogFilter.fs and StartupCutover.fs it cross-references were both deleted by infrastructure-r8kqt

**Type:** Modeling / Promote
**BC:** administration
**From → To:** backlog → todo

---

## 2026-09-03 17:51 -- Modeling / Refined: administration-b3xqf - Update the administration README's Offline demoted-event filter entry

**Type:** Modeling / Refine
**BC:** administration
**Status after:** todo
**Summary:** Grounded the stale-README follow-up against the tree: README line 28 is the only line naming the deleted `EventLogFilter.fs`/`StartupCutover.fs`; ADR-0052 already carries a retirement note, ADR-0058 does not; the runbook has its executed/retired header. Settled the open question in What: the entry is reframed as settled history (README's existing "retired"/"formerly" convention), the `StartupCutover.fs` compile-dependency anecdote is dropped in favour of a one-clause pointer to ADR-0058 (pointer over restatement), and a matching "Retirement note (2026-09-03)" is added to ADR-0058. Scope pinned to exactly two files, six machine-checkable criteria. No orchestrator round — pure documentation chore, findings were factual.
**Split into:** none
**ADRs written:** none

---

## 2026-09-03 17:40 -- Work session ended

**Type:** Work / Session end
**Duration:** 18m (first "Batch started" 17:22 → 17:40)
**Completed:** 1 (first-try PASS: 1, re-dispatched: 0, skipped: 0)
**Bounced:** 0
**Failed:** 0
**Escalated after verification:** 0
**Dispatches:** infrastructure-r8kqt: 1
**Commits:** 3 (batch start, task completion, this entry)
**Vision-conformance:** none — batch aligns with vision. The one shipped task retires temporary cutover machinery, a dead sync gate, and a fired one-shot CLI (net −1116 lines); it adds no admin-console scope, pulls toward no Out-of-Scope (v1) item, and moves nothing away from Remaining v1 Work.
**Batch mix:** 0% product-facing / 100% harness / 0% bookkeeping (1 task) — hand-classified; installed plugin 0.9.2 carries no `vacuum-guard.mjs`. `type: chore` touching real source (`Composition.fs`, `PlaytimeTracker.fs`, `Program.fs`, both fsprojs, four deleted modules) rather than purely bookkeeping surfaces.
**Carry-over:** none — working tree clean, no registered worktrees remain, `.worktrees/` removed.

**Session-start churn note:** one untrailed human commit since the last session-end boundary — `ed07f87 research for dashboard` (three research reports plus three root-level dashboard notes). Touches no governed surface; nothing to re-align. 0 recognized machine-shape commits, 1 human commit.

**Builder runbook pending (from infrastructure-r8kqt):** Part B — delete the three server backups under `/app/data/backups` and the dev-machine stale inventory under `C:\Users\marco\app\mediatheca\backups\`; then `/deploy` and confirm the first boot logs zero `[StartupCutover]` lines with drift 0/7. The task file's checkboxes for those criteria are deliberately unticked. New backlog item `administration-b3xqf` (administration README's Offline demoted-event filter entry now cross-references deleted files) was filed by the worker and indexed.

**Harness notes, carried forward:** (1) the `checkpoint` verb refuses every path as `outside-worktree` when the fileList is built from Git Bash's `$PWD` (POSIX `/c/...` form) — pass drive-letter paths. (2) Plugin 0.9.2's `checkpoint` manifest did not fold in the vacated `doing/` path; it was staged explicitly, and git recorded the move as a rename. (3) The main-tree `node_modules` junction was required again for `npm run build` and was unlinked before `git worktree remove --force`; main-tree `node_modules` verified intact afterwards (206 entries).

---

## 2026-09-03 17:35 -- Task verified and completed: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes

**Type:** Work / Task completion
**Task:** infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes
**Summary:** Retired the one-shot startup-cutover machinery (StartupCutover.fs and its Composition.fs call sites), the dead PlaytimeTracker Steam-sync gate, and the fired EventLogFilter purge CLI, restoring the plain Projection.startAllProjections boot path; ADR-0052 and the purge runbook carry retirement notes. Builder-owned Parts A/B (live and dev backup deletion) and the post-deploy boot check remain as runbook steps.
**Duration:** 13m40s
**Verification:** PASS (iteration 1)
**Files changed:** 12
**Tests added:** 0
**ADRs written:** none

---

## 2026-09-03 17:22 -- Batch started: [infrastructure-r8kqt]

**Type:** Work / Batch start
**Tasks:** infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes
**Parallel:** no (1 worker — infrastructure-r8kqt is the only ready task across every BC; nothing held back)

---

## 2026-09-03 17:20 -- Modeling / Promoted: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes

**Type:** Modeling / Promote
**BC:** infrastructure
**From → To:** backlog → todo

---

## 2026-09-03 21:05 -- Modeling / Refined: infrastructure-r8kqt - Retire the one-shot cutover machinery and its backups

**Type:** Modeling / Refine
**BC:** infrastructure
**Status after:** todo
**Summary:** Window-closed grounding pass. Both rollback windows (2026-08-17, 2026-08-19) have elapsed, so the Part A precondition was run read-only from the modeling session: 0 `StartupCutover] Phase` log lines in 24h, container up 3 days healthy, drift check 0 discrepancies across all 7 projections, and `/app/data/backups` holds exactly the three inventoried files. Recorded in Notes that the backups are now expired rollback points, so the one-way-ordering hazard no longer constrains sequencing — Part C (worker) and Part B (builder) may run in either order. Re-grounded every named symbol against the working tree: all present, only fsproj/Composition line hints drifted (noted; task already says re-locate by name). Settled `plan.md`'s disposition — it is tracked (commit 648db9c, "kept for the record"), not untracked as the 2026-08-04 note claimed; it stays, out of scope, same reasoning as the purge runbook. No orchestrator round — third refinement, findings were factual corrections grounded directly in source and production state.
**Split into:** none
**ADRs written:** none

---

