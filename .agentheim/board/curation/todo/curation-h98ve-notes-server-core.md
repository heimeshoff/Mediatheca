---
id: curation-h98ve
title: Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)
status: todo
type: feature
context: curation
created: 2026-09-16
completed:
depends_on: []
blocks: [curation-knqfj, curation-j4qqt]
tags: [notes, content-blocks, game-journal, event-sourcing, curation, movies, series, games, books]
related_adrs: [0080, 0043, 0044, 0079]
related_research: []
prior_art: []
---

## Why

Mediatheca has two free-form annotation systems and the builder wants exactly one (2026-09-16):

- **Content blocks** (movies, series, books): the event-sourced `ContentBlocks` aggregate — replayable,
  but a flat five-type model keyed by a bare slug with no owner kind, so a movie and a book sharing a
  title-year slug would share one stream.
- **Game Journal** (games): the better model — a Notion-style tree of sixteen block types with columns
  and toggles — but plain SQLite storage (`game_journal_blocks`, classified Imperative in ADR-0044).
  It is the only user-authored data in the app a rebuild from the event log cannot bring back, which
  is exactly what ADR-0043 says must not happen to the user's own writing.

ADR-0080 unifies them: the Game Journal's block model becomes **Notes**, owned by Curation, keyed by
`(MediaType, slug)` (the ADR-0079 pattern), event-sourced as **one document-snapshot event per
debounced save**. This closes Curation's standing open question ("should ContentBlocks become a
general annotations-on-any-aggregate mechanism?" — yes). This task is the server core only; the
client switch (`curation-knqfj`) and the migration + teardown (`curation-j4qqt`) follow so each lands
independently reviewable. Everything here is strictly additive: ContentBlocks and GameJournal keep
serving live traffic until j4qqt removes them.

## What

- **`src/Server/Notes.fs`**, mirroring `ContentBlocks.fs`'s file shape. `NotesEvent = Notes_saved of
  blocks: JournalBlockDto list`; `NotesState = { Blocks: JournalBlockDto list }`; `NotesCommand =
  Save_notes of blocks: JournalBlockDto list`. `evolve` is last-snapshot-wins. `decide` refuses (a) a
  block list with duplicate `Id`s and (b) more than 5000 blocks, returns `Ok []` (no event) when the
  incoming list is structurally and order-sensitively equal to the current state, else
  `Ok [ Notes_saved blocks ]`. `streamId (mediaType: MediaType) (slug: string) = sprintf "Notes-%s-%s"
  (storageToken mediaType) slug` where `storageToken` is a **frozen** `MediaType -> string`
  (`movie`/`series`/`game`/`book`), documented as never to be derived from `MediaType.routePrefix` —
  a route rename must never orphan every Notes stream. `parseStreamId` is the inverse. `Serialization`
  module with `serialize`/`deserialize`/`handledEventTypes = ["Notes_saved"]`/`toEventData`/
  `fromStoredEvent`.
- **`src/Server/NotesProjection.fs`**, mirroring `ContentBlockProjection.fs`: table `notes_blocks`
  (`id` PK, `media_type`, `slug`, `parent_id`, `block_type`, `content`, `checked`, `collapsed`,
  `language`, `url`, `image_ref`, `caption`, `position`, `width` — the `game_journal_blocks` columns
  plus the two owner columns) with `idx_notes_blocks_owner (media_type, slug)`. `handleEvent` on
  `Notes_saved` deletes every row for the owner then reinserts. `handler` registered in
  `Composition.fs`'s `projectionHandlers`.
- **`IMediathecaApi`** gains `getNotes: MediaType -> string -> Async<JournalBlockDto list>` and
  `saveNotes: MediaType -> string -> JournalBlockDto list -> Async<Result<unit, string>>`, wired
  through the existing `executeCommand` path (no bespoke write path).
- **`HasNotesContent: bool`** on `MovieDetail`, `SeriesDetail`, `GameDetail`, `BookDetail`, replacing
  `GameDetail.HasJournalContent`. `JournalBlock.hasContent` in `Shared.fs` stays the rule (non-whitespace
  `Content`, or `ImageRef`/`Url` set; structural wrappers don't count), re-derived from `notes_blocks`
  on every detail read, never cached (ADR-0043). Until knqfj switches the client, `GameDetail`'s
  Journal-first rule reads `HasNotesContent` — which is `false` for every game until j4qqt migrates —
  so this task also keeps the games rule correct in the interim by computing `HasNotesContent` for
  games as `notes_blocks` content OR `game_journal_blocks` content (one line, deleted by j4qqt; say
  so in a comment).
- **Administration registries** (`src/Server/Administration.fs`): `boundedContextPrefixes` gains
  `"Notes", "Notes-"`; the round-trip codec list gains a `"Notes-"` entry over `Notes.Serialization`;
  `handledEventTypesByBoundedContext` gains `"Notes", Notes.Serialization.handledEventTypes`; the
  table classification gains `"notes_blocks", Projected "NotesProjection"`; the image-cache
  orphan-detection column list gains `"notes_blocks", "image_ref"`.
- **Media-item removal:** if the existing remove-movie/series/game/book paths cascade into content
  blocks today, mirror that with `Save_notes []`; if they don't (orphan streams are rebuild-safe),
  leave removal alone and say so in the RESULT block. ADR-0080 recommends `Save_notes []` over a
  distinct `Notes_discarded` event.
- **README delta (reported, applied by the conductor):** Curation README gains the Notes entries from
  ADR-0080's language — `Notes`, `Note block`, `Save`, `Notes content` under Ubiquitous language; a new
  "Document streams (not aggregates)" heading holding Notes (it protects no domain invariant, only
  replay integrity); `Notes_saved` in Key events, `Save_notes` in Key commands. The content-block
  entries stay until j4qqt deletes the system.

## Acceptance criteria

- [ ] `dotnet build` and `npm run build` succeed.
- [ ] Expecto: two identical `saveNotes` calls leave the stream's position unchanged (no second event).
- [ ] Expecto: a reorder-only save (same ids and content, different `Position`/order) appends an event.
- [ ] Expecto: `saveNotes` with two blocks sharing an `Id` returns `Error`; with more than 5000 blocks
      returns `Error`.
- [ ] Expecto: rebuilding `NotesProjection` from a stream of several `Notes_saved` snapshots yields
      exactly the latest snapshot's rows in `notes_blocks`.
- [ ] Expecto: the existing registry-completeness pattern (ADR-0044, `TableClassificationTests.fs`
      and the `handledEventTypesByBoundedContext` guard from books-y9kxy) covers `notes_blocks`,
      `"Notes"`/`"Notes-"` and `Notes_saved`; the Health tab would list no unhandled Notes event and
      no unclassified `notes_blocks` table.
- [ ] Expecto: `HasNotesContent` is computed for a movie, series, game and book each with and without
      notes content; a game with only legacy `game_journal_blocks` content still reports `true`.
- [ ] `GameDetail.HasJournalContent` no longer exists in `src/`; `HasNotesContent` exists on all four
      detail DTOs (grep).
- [ ] `ContentBlocks.fs`, `ContentBlockProjection.fs`, `GameJournal.fs`, `ContentBlockEditor.fs` and
      `JournalEditor.fs` have no diff.
- [ ] `npm test` and `npm run test:client` are green (the Vitest `DefaultTab.test.fs` rename to
      `HasNotesContent` is the only client edit allowed here).

## Notes

- Read ADR-0080 in full first, especially the snapshot-event design, the `storageToken` freeze, the
  no-op-save rule, and the event-size expectations (one whole document per debounced save is accepted
  for a single-user app; if the log ever needs bounding, the projection shape flips to a JSON blob —
  not this task's concern).
- The duplicate-id and size-cap refusals are log-integrity guards, not business rules. Do not
  validate tree shape (orphan `ParentId`, `column` outside `columnList`); the client's `Doc.normalize`
  already handles that permissively and the server must store what the client sent.
- Do not document Notes as an aggregate with a "protects:" clause — see the README delta.
- Specialist round (orchestrator, 2026-09-16): tactical-modeler designed the stream and `decide`;
  architect confirmed the separate `Notes-` namespace from `ContentBlocks-` and the ADR-0079 owner key.
