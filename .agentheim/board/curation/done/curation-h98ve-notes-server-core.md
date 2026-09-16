---
id: curation-h98ve
title: Notes server core — an event-sourced block document per (MediaType, slug) — `Notes_saved` snapshot stream, `notes_blocks` projection, `getNotes`/`saveNotes` on IMediathecaApi, `HasNotesContent` on all four detail DTOs replacing `GameDetail.HasJournalContent`, registered in every Administration registry; ContentBlocks and GameJournal left untouched (ADR-0080, step 1 of 3)
status: done
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

## Outcome

Implemented Notes' server core per ADR-0080 step 1: `src/Server/Notes.fs` (the
`Notes_saved` snapshot stream — `decide`'s no-op-save/duplicate-id/5000-block-cap
rules, the frozen `storageToken`/`streamId`/`parseStreamId`) and
`src/Server/NotesProjection.fs` (`notes_blocks`, delete-then-reinsert per
snapshot). `IMediathecaApi` gained `getNotes`/`saveNotes` (`src/Shared/Shared.fs`,
`src/Server/Api.fs`), wired through the existing `executeCommand`/
`executeCommandCore` path — no bespoke write path. All four detail DTOs
(`MovieDetail`, `SeriesDetail`, `GameDetail`, `BookDetail`) gained
`HasNotesContent: bool`, computed fresh from `notes_blocks` on every read
(never cached, ADR-0043) in `MovieProjection.fs`/`SeriesProjection.fs`/
`BookProjection.fs`/`GameProjection.fs`. `GameDetail.HasJournalContent` is
deleted; `GameProjection.getBySlug`'s `HasNotesContent` is `notes_blocks`
content OR legacy `game_journal_blocks` content (one `||`, commented as
deleted by curation-j4qqt) so the games-t69rb Journal-first tab default stays
correct until the migration lands. The one required client edit beyond the
Vitest rename is `src/Client/Pages/GameDetail/State.fs`'s
`g.HasJournalContent` -> `g.HasNotesContent` (the field it reads no longer
exists otherwise) — `ContentBlockEditor.fs`/`JournalEditor.fs` are untouched.
`Administration.fs` gained the six registry entries ADR-0080 §12 names:
`boundedContextPrefixes`'s `"Notes", "Notes-"`, the `eventCodecs` compensating
codec, `handledEventTypesByBoundedContext`'s `"Notes"` entry,
`tableRegistry`'s `"notes_blocks", Projected "NotesProjection"`, and
`imageRefColumns`'s `"notes_blocks", "image_ref"`; `Composition.fs`'s
`projectionHandlers` gained `NotesProjection.handler`.

**Removal cascade:** verified (grep across `Api.fs`) that none of
`removeMovie`/`removeSeries`/`removeGame`/`removeBook` today cascade into
`ContentBlocks` streams on removal — those streams are left orphaned
(rebuild-safe, per ADR-0080's own framing). Notes mirrors that: media removal
is untouched here, Notes streams orphan the same way ContentBlocks streams
already do. `GameJournal.deleteForGame`'s physical row+image deletion is a
different, legacy-only behavior this task doesn't touch or extend to Notes.

**Test-fixture ripple (unavoidable, mechanical):** every `MovieDetail`/
`SeriesDetail`/`GameDetail`/`BookDetail`-shaped `getBySlug` call now also
reads `notes_blocks` (mirroring how it already reads `content_blocks`), so
every existing test fixture that already called
`ContentBlockProjection.handler.Init` alongside one of the four media
projections needed the matching `NotesProjection.handler.Init` line too (33
files) — otherwise those fixtures would fail at runtime with "no such table:
notes_blocks". Confirmed via grep that every fixture calling one of the four
`getBySlug` functions already had a `ContentBlockProjection.handler.Init`
neighbor, and that the sed-based bulk edit touched exactly (and only) those
lines — a stray full-directory `sed -i` initially rewrote every test file's
line endings (LF, no content change); this was caught via `git diff` (empty
for the unintended files) and reverted with `git checkout --` before
building, so only the 33 legitimately-affected files plus the 3 new test
files remain in the diff.

**New tests** (19 Expecto, all green — `dotnet run --project
tests/Server.Tests/Server.Tests.fsproj`, 927/927 passing):
`tests/Server.Tests/NotesTests.fs` (pure `decide`/`evolve`/`streamId`
coverage — no-op save, reorder-only save still appends, duplicate-id and
5000-block-cap refusals, `storageToken`/`parseStreamId` round-trip),
`tests/Server.Tests/NotesProjectionTests.fs` (projection replay — several
`Notes_saved` snapshots yield only the latest's rows, per-owner scoping,
field round-trip), `tests/Server.Tests/HasNotesContentTests.fs` (the
DTO-level field for each of the four media types, plus the game
legacy-journal-OR case). `TableClassificationTests.fs` and
`AdministrationTests.fs` were updated for the new registry entries (the
generic registry-completeness guards — `handledEventTypesByBoundedContext`
has an entry for every `boundedContextPrefixes` name, and the
schema-vs-registry set-equality check — cover Notes automatically once
`NotesProjection.handler.Init`/`tableRegistry`/`imageRefColumns` were added,
no bespoke per-BC test needed beyond updating the two hardcoded expectation
lists — Projected-table set and the ref-column count 17 -> 18).

`npm run build` (Fable/Vite), `npm test` (Expecto, 927/927), and `npm run
test:client` (Vitest, 103/103) are all green.

**README delta not carried in the structured block:** the "Document streams
(not aggregates)" heading ADR-0080 calls for cannot be expressed by the
delta grammar (no section-creation op). Please add, as its own `##` heading
placed after "## Aggregates" and before "## Key events":

```
## Document streams (not aggregates)

- **Notes** — an event-sourced document stream, not an aggregate: there is
  no cross-block invariant to protect, only "the user typed; store what
  they typed." Built with the same decide/evolve/reconstitute/streamId/
  Serialization machinery as an aggregate purely because that machinery is
  the cheapest path to free expected-position concurrency, no-op handling,
  and projection catch-up — not because the concept deserves aggregate
  ceremony (ADR-0080).
```

Also by hand: "Key events" and "Key commands" are prose paragraphs, not
bullet lists, so the delta grammar's append/replace ops don't apply — please
append ", plus `Notes_saved` (ADR-0080)." to the Key events paragraph and
", plus `Save_notes`." to the Key commands paragraph. And the "Open
questions" bullet "Should ContentBlocks become a more general 'annotations
on any aggregate' mechanism..." is now answered (yes, ADR-0080) — please
replace it with something like "~~Should ContentBlocks become a more general
'annotations on any aggregate' mechanism?~~ Answered by ADR-0080: yes,
generalized as Notes. ContentBlocks itself is retired by curation-j4qqt."
(not expressed as a delta op since it has no bold lead-in for the anchor
rule).

**Follow-ups (curation-knqfj, curation-j4qqt) are unaffected:** ContentBlocks
and GameJournal keep serving live traffic unchanged, confirmed diff-free
(`git diff --stat` empty for `src/Server/ContentBlocks.fs`,
`src/Server/ContentBlockProjection.fs`, `src/Server/GameJournal.fs`,
`src/Client/Components/ContentBlockEditor.fs`,
`src/Client/Components/JournalEditor.fs`).
