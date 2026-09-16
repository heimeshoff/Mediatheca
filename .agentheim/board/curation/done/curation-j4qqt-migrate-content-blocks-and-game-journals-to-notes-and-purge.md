---
id: curation-j4qqt
title: Migrate and purge — two builder-triggered Administration gates: "Migrate to Notes" turns every content-block owner and game journal into one `Notes_saved` event (owners resolved by exact slug match, ambiguous/orphan ones reported, never guessed); "Purge legacy stores" bulk-deletes the `ContentBlocks-*` streams under ADR-0034 guardrails and drops both legacy tables; ContentBlocks, GameJournal, its boot migration and all content-block RPC members are deleted (ADR-0080, step 3 of 3)
status: done
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: [curation-h98ve, curation-knqfj]
blocks: []
tags: [notes, content-blocks, game-journal, migration, event-surgery, administration, curation]
related_adrs: [0080, 0034, 0044, 0043]
related_research: []
prior_art: []
---

## Why

Once Notes is the sole write/read path on the server (`curation-h98ve`) and in the client
(`curation-knqfj`), the legacy systems must be migrated into Notes and removed: the `ContentBlocks`
aggregate + `content_blocks` projection, and `GameJournal` + `game_journal_blocks`. The builder chose
**migrate, then purge the events** (2026-09-16) — no content is lost, and the retired event types do
not linger in the store. ADR-0080 prescribes two separate operator-triggered Administration gates
with preview + confirm each, under ADR-0034's event-surgery guardrails. The live DB is on harbour;
this ships as code the builder triggers from the Admin UI after deploy — never an out-of-band script
(the 2026-08-02 incident rule: workers never touch the live database; fixtures only).

Live data (dev copy, 2026-09-16): 25 content blocks over 17 owners (all text/quote/callout), 78
`ContentBlocks-*` events over 17 streams; 27 journal blocks over 7 games. Small either way.

## What

- **Gate 1 — "Migrate to Notes"** (Administration action, additive, idempotent per owner).
  Resolve every legacy owner's `MediaType` by exact-slug match against `movie_list`/`series_list`/
  `game_list`/`book_list` (the home projections): owners are every distinct `content_blocks.movie_slug`
  (rows with `session_id IS NOT NULL` — the dead watch-session-scoped feature — are dropped outright)
  and every distinct `game_journal_blocks.game_slug`. **Preview** reports resolved / ambiguous (slug
  known to two home projections) / orphan (known to none) counts and names the slugs; it never guesses.
  **Confirm** emits one `Notes_saved` per resolved owner through `Notes.decide`:
  - Movie/Series/Book owners are sourced from `content_blocks`, converted by a Curation-owned lift of
    `GameJournal.convertOldBlocks`/`mapOldType` (move it to e.g. `src/Server/ContentBlockConversion.fs`
    before `GameJournal.fs` is deleted), including the row-group → `columnList`/`column` conversion;
    legacy `link` content becomes a `link` block.
  - Game owners are sourced from `game_journal_blocks` **directly** (already the target shape,
    `ORDER BY position`). Their `content_blocks` rows, if any remain, are **not** converted a second
    time — they are the input `GameJournal.migrateFromContentBlocks` (still wired in
    `Composition.fs:355-356`, marker `game_journal_migrated` in `SettingsStore`) already consumed.
  - An owner that already has a `Notes-*` stream is skipped, so re-running after the builder fixes an
    ambiguous slug at the source picks up only that owner.
- **Gate 2 — "Purge legacy stores"** (Administration action, destructive, single confirm, ADR-0034
  protocol: `VACUUM INTO` backup on a per-request connection first, preview + explicit confirm, FTS
  rebuild, projections-dirty checkpoint rewind). Preview defaults to every `ContentBlocks-*` stream of a
  Gate-1-resolved owner (ambiguous/orphan owners' streams excluded from the default set). Confirm, in one
  transaction: new `EventStore.deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` (explicit
  stream-id list, never a bare prefix) bulk-deletes the selected event rows; `DROP TABLE content_blocks`;
  `DROP TABLE game_journal_blocks`; delete the `game_journal_migrated` setting.
- **Teardown (same change as Gate 2's drops — ADR-0080's sequencing constraint, otherwise
  `TableClassificationTests.fs`' coverage test fails the other way round):** delete
  `src/Server/ContentBlocks.fs`, `ContentBlockProjection.fs`, `GameJournal.fs`; remove
  `Composition.fs`'s `GameJournal.initialize`/`migrateFromContentBlocks` calls and the
  `ContentBlockProjection.handler` registration; remove the `ContentBlocks`/`content_blocks`/
  `game_journal_blocks` entries from every Administration registry (`boundedContextPrefixes`, the
  codec list, `handledEventTypesByBoundedContext`, the table classification, the image-cache orphan
  column list); delete the 20 content-block `IMediathecaApi` members (8 generic + three per-type
  families) and `getGameJournal`/`saveGameJournal`; delete `ContentBlockDto`, `AddContentBlockRequest`,
  `UpdateContentBlockRequest`, `ContentBlockType`; delete the `ContentBlocks` fields from
  `MovieDetail`/`SeriesDetail`/`BookDetail`; delete h98ve's interim "or `game_journal_blocks` content"
  line in `HasNotesContent`. Sweep `tests/` for the same names.
- **Health tab:** after Gate 2 and a Rebuild-all, no unhandled event types and no unclassified tables.
  Because the events are purged, the retired `Content_block_*` names need no handled-but-retired entry.
- **README deltas (reported, applied by the conductor):** Curation README loses the content-block
  entries (`Content block`, `Block type`, the `ContentBlock` aggregate, the ContentBlock event/command
  families), the Purpose line reads "…plus **notes** (free-form block documents attached to a media
  item's detail page)", and the open question closes ("yes, and it did — Notes, ADR-0080"). Games README
  "Game detail page layout" bullet: "Journal-first" → "Notes-first", source `notes_blocks` (Curation),
  `GameDetail.HasNotesContent`. ADR-0044's Imperative list loses `game_journal_blocks` (amendment
  already recorded in ADR-0080).

## Acceptance criteria

- [ ] `dotnet build`, `npm run build`, `npm test` and `npm run test:client` succeed with
      `ContentBlocks.fs`, `ContentBlockProjection.fs` and `GameJournal.fs` deleted.
- [ ] `grep -rn "ContentBlock\|GameJournal\|game_journal\|content_blocks" src tests --include=*.fs`
      (excluding `bin/`/`obj/`) returns only the migration/conversion code and its tests.
- [ ] Expecto: `EventStore.deleteEventsByStreamIds` removes exactly the rows of the given stream ids
      and leaves every other stream untouched; `previewBulkDeleteByStreamIds` on the same ids then
      reports zero.
- [ ] Expecto fixture-DB test of the full flow: legacy owners seeded across all four media types plus
      one deliberately ambiguous slug (in two home projections) and one orphan → Gate 1 preview names
      exactly those two and converts the rest → a game with both `game_journal_blocks` and leftover
      `content_blocks` rows yields exactly one `Notes_saved` sourced from the journal → Gate 2 preview's
      default set excludes the ambiguous/orphan streams → after confirm, `notes_blocks` holds the
      migrated content, the selected `ContentBlocks-*` events are gone, and neither legacy table exists.
- [ ] Expecto: Gate 1 is idempotent — a second confirm emits zero events.
- [ ] Expecto: a row-grouped pair of content blocks converts into one `columnList` with two `column`
      children carrying the two blocks; a legacy `link` block converts to a `link` block.
- [ ] `TableClassificationTests.fs` and the handled-event guard pass with the legacy entries gone.
- [ ] Both gates appear in the Admin UI with preview → confirm, and Gate 2 refuses to run before
      Gate 1 has been confirmed at least once. [human-eye]
- [ ] Worker and tests run against fixtures only; the builder runs Gate 1 and Gate 2 on harbour after
      deploy, then Rebuild-all, and checks the Health tab. [human-eye]

## Notes

- Read ADR-0080 in full first — the two-gate design, the bulk-purge primitive keyed on an explicit
  stream-id list, and the registry-removal sequencing. Do not merge the gates into one confirm; the ADR
  rejects that explicitly.
- Highest-risk task of the three: it deletes code the other two depended on and, when the builder runs
  it live, destructively purges event history. The `VACUUM INTO` backup is the safety net.
- Not measured on live data: whether any game-typed owner still has leftover `content_blocks` rows from
  before the boot migration first ran. The fixture test above exercises that case explicitly.
- Waits in `backlog/` only because the promote gate needs both parents in `done/`; it is otherwise
  fully refined.

## Outcome

Shipped the third and final step of ADR-0080's Notes migration. Two builder-triggered
Administration gates now exist on the Surgery tab (`src/Client/Pages/AdminSurgery/{Types,State,Views}.fs`),
each with its own preview -> confirm ceremony:

- **Gate 1 — "Migrate to Notes"** (`IAdminApi.previewNotesMigration`/`runNotesMigration`,
  `Administration.previewNotesMigrationCore`/`runNotesMigrationCore` in `src/Server/Administration.fs`):
  resolves every distinct legacy owner (`content_blocks.movie_slug` with `session_id IS NULL`,
  union `game_journal_blocks.game_slug`) via `CatalogProjection.resolveMediaType`'s exact-match
  probe (reused verbatim from curation-w9fkq), reports ambiguous/orphan owners by name, and
  converts every resolved owner into one `Notes_saved` via `Notes.decide` — Movie/Series/Book
  owners through a lifted-and-renamed conversion (`src/Server/ContentBlockConversion.fs`,
  `convertOldBlocks`/`mapOldType`, over a new private `LegacyContentBlock` record since
  `Shared.ContentBlockDto` is deleted), Game owners read directly from `game_journal_blocks`
  (never re-converting a leftover `content_blocks` row for an already-migrated game). Skips any
  owner whose `Notes-*` stream already exists — additive, idempotent, safely re-runnable.
- **Gate 2 — "Purge legacy stores"** (`IAdminApi.previewPurgeLegacyNotes`/`purgeLegacyNotes`,
  `Administration.previewPurgeLegacyNotesCore`/`purgeLegacyNotesCore`): defaults to every
  `ContentBlocks-*` stream of a Gate-1-resolved owner (excluding ambiguous/orphan owners'
  streams from the default, named separately in the preview), then bulk-deletes the selected
  streams' event rows via two new `EventStore` primitives — `deleteEventsByStreamIds`/
  `previewBulkDeleteByStreamIds` (ADR-0080 §11, explicit stream-id list, never a bare prefix) —
  and drops `content_blocks`/`game_journal_blocks` plus the `game_journal_migrated` setting, all
  inside `runSurgeryMutation`'s existing VACUUM-INTO-backup/FTS-rebuild/checkpoint-rewind
  transaction (ADR-0034, reused verbatim). The client-side "Gate 2 refuses to run before Gate 1
  has been confirmed" guard is a session-scoped Elmish flag (`NotesMigrationConfirmedThisSession`)
  disabling the preview/confirm buttons — a human-eye guard per the task's own framing, not a
  server-enforced one.

Teardown, shipped in the same commit as Gate 2's drops per ADR-0080's sequencing note:
`ContentBlocks.fs`, `ContentBlockProjection.fs`, `GameJournal.fs` deleted; `Composition.fs` loses
`ContentBlockProjection.handler` and the `GameJournal.initialize`/`migrateFromContentBlocks` boot
calls; `Administration.fs`'s `boundedContextPrefixes`, `eventCodecs`,
`handledEventTypesByBoundedContext`, `tableRegistry`, and `imageRefColumns` lose their
`ContentBlocks`/`content_blocks`/`game_journal_blocks` entries; `Api.fs` loses the 20
content-block RPC members (8 generic + 3 per-type families across movies/series/games/books) and
`getGameJournal`/`saveGameJournal`, and `getStreamEvents`'s cross-link to a slug's
`ContentBlocks-*` stream is removed (it now reads only the requested stream); `EventFormatting.fs`
loses `formatContentBlockEvent` and its dispatch arm; `Shared.fs` loses `ContentBlockDto`,
`ContentBlockType`, `AddContentBlockRequest`, `UpdateContentBlockRequest`, and the `ContentBlocks`
field from `MovieDetail`/`SeriesDetail`/`BookDetail`/`GameDetail` (the last wasn't named in the
task's teardown list but had to go too — `ContentBlockProjection` no longer exists to populate
it); `GameProjection.getBySlug`'s `HasNotesContent` drops h98ve's interim
`|| GameJournal.get conn slug |> JournalBlock.hasContent` OR, now reading `notes_blocks` alone.

Tests: `ContentBlockConversionTests.fs` (new — the lifted conversion's type-mapping and
row-group -> columnList coverage, replacing the deleted `GameJournalTests.fs`'s equivalent
cases), `EventSurgeryTests.fs` gains three cases for `deleteEventsByStreamIds`/
`previewBulkDeleteByStreamIds` (exactness against untargeted streams, bounded sample, empty-list
no-op), `NotesMigrationTests.fs` (new — the full fixture-DB flow: four normally-resolvable owners
across all four media types, one ambiguous owner registered in two home projections, one orphan,
and a game carrying BOTH a `game_journal_blocks` row and a leftover `content_blocks` row; asserts
Gate 1's preview/confirm/idempotency, that the game's Notes content is sourced from the journal
only, Gate 2's default-set/excluded-set preview, and that after confirm `notes_blocks` survives
while the four purged `ContentBlocks-*` streams are gone, the two excluded ones survive, and both
legacy tables are dropped). `ContentBlocksTests.fs`/`GameJournalTests.fs` deleted (tested the
now-deleted modules); one `AdministrationTests.fs` orphan-image test retargeted from
`content_blocks`/`game_journal_blocks` fixtures onto `notes_blocks`; every other touched test file
only lost its now-dead `ContentBlockProjection.handler.Init`/`GameJournal.initialize` fixture
setup line. Full suite green: `dotnet build` (server), `npm run build` (client Fable
type-check), Expecto 919/919, Vitest 108/108.

`grep -rn "ContentBlock\|GameJournal\|game_journal\|content_blocks" src tests --include=*.fs`
returns only this migration's own code (`ContentBlockConversion.fs`, `Administration.fs`'s Gate
1/2 implementation, `Shared.fs`'s Gate 1/2 type docs, the Gate 2 UI copy) and its tests
(`ContentBlockConversionTests.fs`, `NotesMigrationTests.fs`, `EventSurgeryTests.fs`'s new
primitive tests, and a comment in `AdministrationTests.fs` explaining the imageRefColumns count
change) — plus two harmless historical doc-comments in `Notes.fs` that were reworded to drop the
literal name anyway.

README deltas NOT expressible as append/replace ops (no `remove` op exists) — apply directly to
`.agentheim/knowledge/contexts/curation/README.md`:
- **Purpose line**: replace "plus **content blocks** (free-form annotations attached to catalogs
  and detail pages)." with "plus **notes** (free-form block documents attached to a media item's
  detail page)."
- **Ubiquitous language**: DELETE the two bullets `- **Content block** — a free-form chunk of
  content (text, image, link) attached to a context. Used on catalogs and detail pages.` and
  `- **Block type** — the discriminator of what kind of content a block holds (\`ContentBlockType\`
  in Shared).` The existing **Notes** bullet's "replacing both ContentBlocks…" wording may stay
  as historical record.
- **Aggregates**: DELETE the bullet `- **ContentBlock** — protects: blocks are typed; updates
  respect the type.` (only `Catalog` remains under Aggregates; `Notes` already lives under its
  own "Document streams (not aggregates)" heading).
- **Key events** (non-bullet prose line): replace "\`Catalog_created\`, \`Catalog_updated\`,
  \`Entry_added\`, \`Entry_updated\`, \`Entry_removed\`, \`Entries_reordered\`, plus the ContentBlock
  event family (see \`ContentBlocks.fs\`), plus \`Notes_saved\` (ADR-0080)." with "\`Catalog_created\`,
  \`Catalog_updated\`, \`Entry_added\`, \`Entry_updated\`, \`Entry_removed\`, \`Entries_reordered\`, plus
  \`Notes_saved\` (ADR-0080)."
- **Key commands** (non-bullet prose line): replace "\`Create_catalog\`, \`Update_catalog\`,
  \`Add_entry\`, \`Update_entry\`, \`Remove_entry\`, \`Reorder_entries\`, plus ContentBlock commands,
  plus \`Save_notes\`." with "\`Create_catalog\`, \`Update_catalog\`, \`Add_entry\`, \`Update_entry\`,
  \`Remove_entry\`, \`Reorder_entries\`, plus \`Save_notes\`."
- **Open questions**: the existing strike-through line already anticipates this task
  ("ContentBlocks itself is retired by curation-j4qqt") — tense-fix only: "is retired by" ->
  "was retired by curation-j4qqt."

**ADR-0044 amendment**: no file edit needed — ADR-0080 (`amends: [0044]`) already records that
`game_journal_blocks` leaves the Imperative list and `content_blocks` leaves the Projected list;
this task's `Administration.tableRegistry` change (both entries removed, verified above) is that
amendment's actual code-side execution. ADR-0044's own document doesn't enumerate the registry
inline, so nothing there needs editing.

**Games README delta**: applied via the `README_DELTA` block above (one `replace` op on the "Game
detail page layout" bullet under "Ubiquitous language" — "Journal-first" -> "Notes-first", source
`notes_blocks` (Curation), `GameDetail.HasNotesContent`).

Not verified by this worker (per the task's own [human-eye] criteria, and per the standing rule
that workers never touch the live database): both gates' visual appearance/behavior on the Admin
UI, and running Gate 1 + Gate 2 against harbour's live data after deploy — that's the builder's
job, described in the task's own Notes section.