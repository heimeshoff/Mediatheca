---
id: 0080
title: Notes — one document-snapshot event per debounced save, owned by (MediaType, slug), replacing ContentBlocks and GameJournal
scope: curation
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
amends: [0044]
related_tasks: [curation-h98ve, curation-knqfj, curation-j4qqt, curation-kezpv]
related_research: []
---

# ADR 0080: Notes — one document-snapshot event per debounced save, owned by (MediaType, slug)

## Context

Mediatheca has carried two free-form annotation systems side by side, and the curation README has
stood with an open question — "Should ContentBlocks become a more general 'annotations on any
aggregate' mechanism?" — since the BC was written:

1. **Content blocks** (movies, series, books) — event-sourced. `src/Server/ContentBlocks.fs`: 7
   fine-grained events (`Content_block_added/updated/removed/type_changed`,
   `Content_blocks_reordered/row_grouped`, `Content_block_row_ungrouped`) over stream
   `ContentBlocks-{slug}` — a **bare slug, no owner-kind discriminator**. Projection
   `content_blocks` (`src/Server/ContentBlockProjection.fs`, column literally `movie_slug`
   regardless of media type), registered in `Administration.fs`'s `boundedContextPrefixes`,
   `eventCodecs`, `handledEventTypesByBoundedContext`, `tableRegistry`/`projectionTables`, and
   `imageRefColumns`. Flat list, 5 block types (text/image/quote/callout/code), optional two-block
   row pairing (`RowGroup`/`RowPosition`). Client editor `ContentBlockEditor.fs` (837 lines),
   mounted on MovieDetail/SeriesDetail/BookDetail, two StyleGuide specimens. Two features are
   dead: `sessionId`-scoped blocks (no client ever passes one) and catalog-attached blocks (no
   catalog page mounts the editor). Live dev-copy data: 25 blocks / 17 owners / 78 events / 17
   streams.
2. **Game journal** (games only) — plain SQLite storage, **not event-sourced**.
   `src/Server/GameJournal.fs`: table `game_journal_blocks` keyed `game_slug`, whole document
   deleted-and-reinserted per save inside one transaction. Shared model `JournalBlockDto` (12
   fields) and `JournalBlockTypes` (16 types: text, h1–h4, bullet, numbered, todo, toggle, quote,
   callout, code, link, image, columnList, column) — a real tree via `ParentId`, side-by-side
   layout via `columnList`/`column`/`Width`. `JournalBlock.hasContent` re-derives
   `GameDetail.HasJournalContent` fresh on every `getGameDetail` call (ADR-0043's
   re-derivability test — no cached flag), driving the Journal-first tab default (games-t69rb).
   Client editor `JournalEditor.fs` (1318 lines), self-contained, 800ms-debounced autosave. Live
   data: 27 blocks / 7 games. Classified `Imperative "GameJournal"` in ADR-0044's `tableRegistry`.

The second is the better model — the one the user actually writes in — and the one that violates
ADR-0043 outright: the user's own writing is the purest case of "an observation of the user's own
engagement," yet it lives in a mutable table with no replay, no audit, no recovery from a bad
overwrite. This is the mirror-image of the `game_play_session` defect ADR-0043 already named and
fixed for play history. Meanwhile ContentBlocks is correctly event-sourced but untyped by owner —
exactly the defect ADR-0079 fixed for catalog entries (`Entry_added` now carries `MediaType`) —
and carries two dead features nobody uses.

Unrelated to the capture that started this workstream, `GameJournal.fs` already contains a live,
still-wired, marker-gated **prior migration**: `migrateFromContentBlocks` (lines 124–253), called
unconditionally every boot from `Composition.fs:355-356`, gated on `SettingsStore` key
`"game_journal_migrated"` (already `"1"` in production). It one-time-converted each game's OLD
content-block rows into the `JournalBlockDto` shape via `convertOldBlocks` (screenshot→image,
quote→quote, callout→callout, code→code, link→link, else→text; row-grouped pairs become
`columnList` + 2×`column` + 2×content), dumping a markdown backup to
`DATA_DIR/journal-export/<slug>.md` as insurance, and is purely additive — it never deleted the
`content_blocks` rows it read. This is real, precedented conversion logic for exactly the mapping
this ADR's migration needs to do again, more broadly. It is not evidence of "no startup-migration
precedent" — it is a second, smaller, still-live boot routine sitting alongside the one already
retired (`StartupCutover.fs`, per project memory). Its *shape* — automatic, unattended, no room for
a human decision — is exactly the shape this ADR's migration must not repeat, for reasons below.

## Decision

### 1. One surviving model, in Curation, named Notes

The Game Journal's tree/16-type/columns/toggle model generalizes to all four media types and lives
in Curation. It is named **Notes**, not "Journal" — that word is the activity-diary Journal BC's.
This closes the curation README's standing open question: yes, content blocks become a general
annotations mechanism, typed by owner.

### 2. Notes is a document stream, not an aggregate

Plainly: this is not an aggregate. An aggregate exists to protect an invariant across a consistency
boundary, and Notes has none — no cross-block rule, no forbidden lifecycle transition, nothing of
the form "X cannot exceed Y." The write side is "the user typed; store what they typed." It is
built with the codebase's aggregate machinery (`decide`/`evolve`/`reconstitute`/`streamId`/
`Serialization`, matching `ContentBlocks.fs`'s file shape, wired through `Api.executeCommandCore`
exactly like every other command) because that machinery is the cheapest path — free
expected-position concurrency, free `Ok []`-means-no-op handling, free projection catch-up — not
because the concept deserves aggregate ceremony. The curation README documents it under a
**"Document streams (not aggregates)"** heading, not under "Aggregates" with a fabricated
"protects:" clause.

The event-worthiness question (ADR-0043) is unambiguous, separately from the aggregate question:
the user's own writing, observed at a moment, never re-derivable from any third party, passes the
re-derivability test as decisively as a play session does.

### 3. One event per save: `Notes_saved`, blocks only, owner in the stream id

`Notes_saved of blocks: JournalBlockDto list` — past tense, in the domain's word (the editor's own
UI already says "Saved"), not `*_updated`/`*_replaced` CRUD-speak. The editor's existing
800ms-debounced whole-document save is unchanged; each save appends exactly one snapshot carrying
the **full current block list** — not a diff, not per-block events. Per-block events are not
reintroduced: the editor has no per-block save path, and synthesizing a diff server-side would
invent precision the write side doesn't have.

The owner is **not** duplicated into the payload. It lives only in the stream id:
`Notes-{token}-{slug}`, `token ∈ {movie, series, game, book}`, from a **frozen** function
`Notes.storageToken: MediaType -> string` — deliberately **not** ADR-0079's `MediaType.routePrefix`
(a UI concern free to change when the router changes) and not a DU `.ToString()` (free to change on
a rename). Two copies of the owner would be two things ADR-0034's event surgery (`editEventData`,
`renameEventTypeRows`) could desync independently, for no read the stream id doesn't already serve.
`MediaType` never changes for a slug once created, and `Api.generateUniqueSlug` never reuses a slug
within a type (a removed-then-re-added item gets a fresh slug), so the stream id is stable for the
life of the item. `Notes-` stays the prefix's leading token (not `Movie-Notes-…`) so all four media
types' notes remain one filterable unit in the event browser — right, since they're one concept.

Not carried in the payload: `savedAt` (`events.timestamp` already has it), `blockCount`/
`hasContent` (derivable — caching either is exactly what ADR-0043 and games-t69rb's
`HasJournalContent` note already rule out).

### 4. `decide` refuses only what breaks replay or the log — nothing about document shape

Two refusals, both log/projection-integrity guards, not domain invariants:

- **Duplicate block ids within one save.** The projection's `notes_blocks.id` is a PRIMARY KEY; a
  document with two blocks sharing an id would throw mid-replay and poison every future rebuild.
  This is ADR-0079 §3's "an event the projection cannot insert is a replay-breaking divergence,"
  applied to a new shape.
- **An absurd document** — more than 5000 blocks. Cheap, one-directional protection for an
  append-only log against a runaway client; not a domain rule.

Everything else is **accepted as given**, matching the Game Journal's permissive plain-storage
behavior for its entire life, during which no malformed-tree failure ever materialized: an orphan
`ParentId`, a `column` outside a `columnList`, duplicate `Position` values, an unrecognized
`BlockType` string. The client's `Doc.normalize` (`JournalEditor.fs:131-141`) already repairs
these — its own comment says "so nothing is ever lost" — and refusing a save inside a
debounced autosave editor is close to the worst possible failure: the user keeps typing while their
work silently stops persisting. The server does **not** normalize either, for a sharper reason than
symmetry: storing a document different from the one the client sent would desynchronize the
client's optimistic in-memory state and make the very next no-op comparison (decision 5) spuriously
unequal, turning every subsequent save into a real event forever. `BlockType` stays an open
`string`, not a closed Shared DU — closing it would make a 17th block type an event-schema
migration instead of a client-only change.

### 5. A save that changes nothing appends nothing

`decide` returns `Ok []` (which `Api.executeCommandCore` already treats as append-nothing,
return-success — the exact mechanism Books' per-source `Observe_reading_progress` no-op already
exercises, ADR-0076 §2) when `blocks = state.Blocks`: **F# structural, order-sensitive equality**
over the full `JournalBlockDto list`. Not a looser comparison — `Position` and list order ARE the
document's layout, so a reorder-only save with identical content must still produce an event.
`state` comes from `reconstitute` over *decoded* events, so the comparison is inherently against
the JSON-round-tripped form, never a pre-encode in-memory value — the property decision 4's
no-normalization rule depends on.

### 6. Projection: flat block rows in `notes_blocks`

```sql
CREATE TABLE IF NOT EXISTS notes_blocks (
    id         TEXT PRIMARY KEY,
    media_type TEXT    NOT NULL,          -- 'movie' | 'series' | 'game' | 'book'
    slug       TEXT    NOT NULL,
    parent_id  TEXT,
    block_type TEXT    NOT NULL,
    content    TEXT    NOT NULL DEFAULT '',
    checked    INTEGER NOT NULL DEFAULT 0,
    collapsed  INTEGER NOT NULL DEFAULT 0,
    language   TEXT,
    url        TEXT,
    image_ref  TEXT,
    caption    TEXT,
    position   INTEGER NOT NULL DEFAULT 0,
    width      REAL    NOT NULL DEFAULT 1.0
);
CREATE INDEX IF NOT EXISTS idx_notes_blocks_owner ON notes_blocks(media_type, slug);
```

`handleEvent` on `Notes_saved`: delete every row for `(media_type, slug)`, then insert the new
block list — idempotent and order-independent across replay, last-snapshot-wins automatically
because snapshots replay in ascending `stream_position`. `getNotes` is
`SELECT … WHERE media_type = @t AND slug = @s ORDER BY position` — shape-identical to today's
`GameJournal.get`, so the surviving editor's read path carries zero migration risk.

Chosen over a single JSON-blob document column because two concrete, already-existing consumers
decide it: `Administration.imageRefColumns` is a flat `(table, column)` list the orphan-image GC
walks (a blob would need a special-cased `json_each` path grafted onto a mechanism with no notion
of one), and ADR-0044's drift/table-classification discipline does column-level work that degrades
to one opaque string compare against a blob. `notes_blocks` becomes a real `Projected`,
checkpoint-tracked table — one fewer member of the `tableExists`-must-specially-exempt club
`game_journal_blocks` used to belong to.

**The accepted cost:** a rebuild replays *every* snapshot for a stream, doing a delete + N inserts
each, even though only the last one matters. See "event size" below.

### 7. Concurrency: last-write-wins, accepted, stream integrity unchanged

`Api.executeCommandCore` already passes the read `currentPosition` as `expectedPosition` to
`appendToStream`, returning `ConcurrencyConflict` on a genuine race — that stream-level optimistic
check is kept, unchanged. What is **not** introduced is a client-supplied document version: two
tabs open on the same item, saving within the ~1s debounce window of each other, is last-write-wins
with no merge offered. This is a deliberate accepted call, not an oversight: single-user app, no
auth; for a whole-document snapshot the only honest conflict resolutions a UI could offer are
"discard yours" or "overwrite theirs," which is exactly what LWW already does without a modal; and
today's `GameJournal.save` has **zero** protection at all (delete-then-reinsert, last committer
silently wins). The new design is strictly no worse, and materially better on the failure's own
terms: every superseded snapshot survives in the event log, so a clobbered document is one
event-browser lookup away from recovery — today it is gone forever the instant it's overwritten.

### 8. `HasNotesContent` replaces `GameDetail.HasJournalContent`, on all four detail DTOs

`JournalBlock.hasContent` (unchanged rule: non-whitespace `Content`, or `ImageRef`/`Url` set;
structural wrappers with nothing typed don't count) generalizes and is computed fresh from the
latest `notes_blocks` snapshot on every `getMovieDetail`/`getSeriesDetail`/`getGameDetail`/
`getBookDetail` call — never cached, per ADR-0043. `GameDetail.HasJournalContent` and its
Journal-first tab-default rule (games-t69rb) are renamed/rebased onto this; the four-DTO
generalization is new — Movies/Series/Books have no tabs today for an analogous default to gate,
so whether they get a "Notes-first" default is left to `curation-knqfj`'s refinement rather than
decided here.

### 9. Dropped: session-scoped blocks and catalog-attached blocks

Neither migrates. Content-block rows with `session_id IS NOT NULL` are excluded outright. Rows
whose owner slug matches no `movie_list`/`series_list`/`game_list`/`book_list` entry (the
catalog-attached shape, if any exist) fall out of the migration's owner-resolution step as orphans
(decision 10) and are reported, not silently dropped without a trace.

### 10. Migration: two operator-triggered Administration gates, not one action and not a boot routine

Per ADR-0056's criterion (automate at boot only when crash-unsafe-mid-sequence and
replay-recoverable; keep operator-executed when the operation mutates the event log and a human
must catch a wrong-but-well-formed outcome), the two halves of this migration have genuinely
different risk shapes and ship as **two separate gates**, sharing one report between them:

**Gate 1 — Migrate to Notes** (additive, idempotent per owner). For every distinct legacy owner —
`content_blocks.movie_slug` (excluding `session_id IS NOT NULL` rows) and every
`game_journal_blocks.game_slug` — resolve its `MediaType` by an exact-match join against
`movie_list`/`series_list`/`game_list`/`book_list`: exactly one match is **resolved**, zero is
**orphan**, two-or-more is **ambiguous**. Unlike ADR-0079's live catalog-entry read path (which
keeps join-order inference as a legacy display fallback because a wrong guess there is cosmetic and
self-heals), this migration commits a specific new event to a specific new stream — a wrong guess
is a *permanent misattribution*, not a correctable render. **Report ambiguous/orphan owners by
name; never guess.** Confirm converts only resolved owners, one `Notes_saved` per owner:

- Owners resolved to `Movie`/`Series`/`Book` are sourced from `content_blocks`, converted via a
  Curation-owned generalization of `GameJournal.convertOldBlocks`/`mapOldType` (lifted out of
  `GameJournal.fs` before that file is deleted — the row-group→`columnList` conversion and the
  type mapping are proven, reusable as-is; the 16-type vocabulary's new members — h1–h4, bullet,
  numbered, todo, toggle — need no mapping, since old content-blocks never produced them). Legacy
  `link` content converts to a dedicated `link` block, not an inline link: old link blocks carry
  structured `Url`/`Content`(title)/`Caption` fields that would need synthesized markdown to
  demote, and every sibling type stays block-level too — no reason to special-case link.
- Owners resolved to `Game` are sourced from `game_journal_blocks` **directly** (already in the
  target `JournalBlockDto` shape — no conversion), reading `ORDER BY position`. `content_blocks`
  rows are **not** also converted for a `Game`-resolved owner: those rows are exactly the
  already-superseded input the old `migrateFromContentBlocks` boot routine already consumed once
  to produce the game's current `game_journal_blocks` — converting them a second time would emit a
  redundant `Notes_saved` for the same stream, immediately overwritten by (or overwriting) the
  authoritative `game_journal_blocks`-sourced one.

Confirm skips any owner that already has a `Notes-*` stream, so rerunning after the operator fixes
an ambiguous slug at the source only picks up that one owner. This step is deliberately **not** a
boot routine despite qualifying structurally (multi-step, replay-recoverable): it must be able to
pause for a human to review and potentially fix ambiguous/orphan owners on no fixed timeline, and
ADR-0056's own reasoning — "only a human comparing ... catches that ... a boot routine has no one
looking" — applies here to a *classification* failure mode, extending the ADR beyond its original
wrong-filter failure mode.

**Gate 2 — Purge legacy stores** (destructive, single confirm, ADR-0034's protocol verbatim).
Preview shows exactly which `ContentBlocks-*` streams will be deleted: by default, every stream
belonging to a Gate-1-**resolved** owner; ambiguous/orphan owners' streams are excluded from the
default set (a named override, never a default, if the operator wants to force it). Confirm runs
inside one `VACUUM INTO`-backed transaction (ADR-0034 unchanged): bulk-delete the selected
`ContentBlocks-*` event rows via a new `EventStore` primitive (below), `DROP TABLE content_blocks`,
`DROP TABLE game_journal_blocks`, FTS rebuild, checkpoint rewind (every projection dirty, operator
reruns Rebuild-all as usual). Gate 2 is operator-executed per ADR-0056's second clause directly: it
mutates the event log, and the dangerous failure — purging a stream Gate 1 never actually converted
— is caught only by a human comparing Gate 1's resolved-owner list against Gate 2's default
selection, not by anything a boot routine could validate unattended.

Two gates, not one combined confirm, because the two operations have incompatible recovery
stories (idempotent-rerun vs. backup-restore-only) and the ambiguous-owner decision needs room to
happen — possibly days later, after the operator fixes source data — before the irreversible step
is even offered.

### 11. New bulk-purge primitive: explicit stream-id list, not a bare prefix

`EventStore` gains:

```fsharp
/// Deletes every event row across the given set of exact stream ids, in one
/// DELETE statement. Same gap-tolerant, no-renumbering contract as
/// `deleteEventRow` (ADR-0034). Returns total rows affected.
let deleteEventsByStreamIds (conn: SqliteConnection) (streamIds: string list) : int

/// Preview: exact total row count across the given streams, plus a bounded
/// sample — same Count+Sample shape as `previewEventTypeRename`.
let previewBulkDeleteByStreamIds (conn: SqliteConnection) (streamIds: string list)
    : int * StoredEvent list
```

An explicit id list, not a bare `ContentBlocks-` prefix, because Gate 2 must be able to **exclude**
specific streams (unresolved owners) from the default target set by construction, not by a UI
checkbox layered on top of a blunter primitive. `deleteEventsByStreamIds` returns `int` (rows
affected), the same contract `deleteEventRow` already has, so a bulk delete composes into
`Administration.runSurgeryMutation` as "a bigger WHERE clause" — zero changes to that function's
transaction shape. The two `DROP TABLE` statements run inside the same `mutate` closure (order
between them doesn't matter — disjoint tables), still followed by the existing
FTS-rebuild-then-checkpoint-rewind tail. New `IAdminApi` members: `previewNotesMigration`/
`runNotesMigration` (Gate 1), `previewPurgeLegacyNotes`/`purgeLegacyNotes` (Gate 2, backed by
`deleteEventsByStreamIds` + the two `DROP TABLE`s, reusing the existing `SurgeryResult` type —
`Applied(backupPath, affected) | BackupFailed reason` — no new result case needed).

### 12. Registry updates — added and removed together, in the same deploy as the purge

**Added** (Notes, ships with `curation-h98ve`): `boundedContextPrefixes`'s `"Notes", "Notes-"`;
`eventCodecs`'s `"Notes-", fun eventType data -> Notes.Serialization.deserialize ... `;
`handledEventTypesByBoundedContext`'s `"Notes", Notes.Serialization.handledEventTypes`;
`tableRegistry`'s `"notes_blocks", Projected "NotesProjection"`; `imageRefColumns`'s
`"notes_blocks", "image_ref"`; `Composition.fs`'s `projectionHandlers` gains
`NotesProjection.handler`.

**Removed** (ContentBlocks/GameJournal retirement, ships with `curation-j4qqt`, atomically with
Gate 2's `DROP TABLE`s — not before): the matching six entries above for `ContentBlocks`/
`content_blocks`/`game_journal_blocks`, plus `Composition.fs`'s `GameJournal.initialize`/
`GameJournal.migrateFromContentBlocks` boot calls (dead the moment `content_blocks` no longer
exists for the latter to read) and the `ContentBlockProjection.handler` registration.

**Sequencing is load-bearing.** Removing a `tableRegistry` entry while its table still physically
exists fails `TableClassificationTests.fs`'s schema-coverage test (ADR-0044: a table present with
no registry entry is the exact anomaly the registry exists to catch) in the *other* direction from
usual — and, in production, would flip the Health tab from clean to "reporting unclassified
tables" until Gate 2 actually runs. This is safe as one shipped change specifically because Gate
2's own transaction is what performs the `DROP TABLE`s: the registry and the tables' physical
removal land atomically from the operator's point of view, at the moment they run Gate 2, not at
deploy time.

### Health-tab-clean, confirmed and why it differs from the Games legacy-event precedent

After a successful Gate 2 and Rebuild-all, the Health tab reports zero unhandled event types and
zero unclassified tables — by a different mechanism than Games' `Game_categorized`-style legacy
events. Those stay registered as handled (evolve/projection no-op) because ADR-0002 keeps the rows
in the log forever, and deregistering them would make currently-present rows read as unhandled.
ContentBlocks is the opposite: Gate 2 **physically deletes** every `Content_block_*` row, so after
purge there are zero rows of those types left for the unhandled-event scan to ever encounter again
— removing the registry entries then introduces no gap, because nothing remains to classify.
Table-side, `content_blocks`/`game_journal_blocks` must be genuinely dropped, not left empty, for
the schema-coverage test to pass — the builder's "drop tables" decision, restated here as the exact
reason it is load-bearing for Health-tab-clean and not just tidiness.

## Event size and log growth

A `Notes_saved` event carries the whole document — for a heavy page, potentially some kilobytes of
text and block metadata, once per 800ms-debounced save. This is acceptable for a single-user app
with no concurrent-writer contention and a store measured in the low tens of thousands of events:
the alternative (per-block events reconstructing a diff server-side) would invent precision the
write side doesn't have, for a storage saving that doesn't matter at this scale. What is **not**
free is rebuild cost: replaying a stream with many superseded snapshots does a delete+N-insert for
every one of them even though only the last matters. This is accepted as a tradeoff, not solved,
here — bounded, if it ever needs to be, by either coarsening the debounce or a future
snapshot-compaction pass (keep only the latest `Notes_saved` per stream, itself an ADR-0034 event
surgery operation, not something to bolt on casually). If log growth is ever judged unacceptable
and left permanently unbounded, decision 6 should be revisited toward a JSON-blob projection column
instead of flat rows, trading `imageRefColumns`/drift-check granularity for O(1) rebuild cost per
event.

## Consequences

### Positive
- The user's own writing becomes replayable, auditable, and recoverable domain history — the exact
  gap ADR-0043 named and this ADR closes for the one place in the codebase it was still open.
- One editor, one model, one storage path for all four media types, replacing two systems and the
  now-dead one-way migration function between them.
- `game_journal_blocks` stops needing to be an `Imperative`-classified table that `tableExists`
  and `isAnyProjectionDirty` specially exempt (ADR-0044's registry loses a member, gains a real
  `Projected` one).
- Slug collisions across media types become representable, exactly as ADR-0079 made them for
  catalog entries, and by the same `(MediaType, slug)` pattern.
- The migration's report-don't-guess owner resolution and two-gate split give the builder a real
  point to catch a misclassification before it becomes a permanent event-log fact — something
  neither prior system offered.

### Negative / accepted tradeoff
- Unbounded log growth per document over time (see above) — deliberately deferred, not solved.
- Two block-type vocabularies exist in the codebase until `curation-j4qqt` lands: `notes_blocks`'
  16 types and the legacy `ContentBlockType`'s 5, coexisting through `curation-h98ve`/`knqfj`.
- Last-write-wins with no client-supplied version means a same-item two-tab race silently drops one
  side's edits — mitigated, not eliminated, by every superseded snapshot staying recoverable in the
  log.
- The migration's owner-resolution step can strand real content (ambiguous/orphan owners) behind a
  manual fix-and-rerun loop with no purpose-built repair UI — accepted as manual DB inspection for
  now; a compensating-event-based repair tool is out of scope here.

### Neutral
- `EventFormatting.crossLinkFields` needs no Notes entry — the owner is reachable straight from the
  stream id, which the event browser already renders using `boundedContextPrefixes`.

## Alternatives considered

- **Per-block events over the new tree model.** Rejected: the editor has no per-block save path;
  synthesizing per-block events server-side would describe a diff algorithm, not a user action.
- **Keep the Game Journal as plain storage, generalized to four types.** Rejected: ADR-0043's test
  is unambiguous, and it is the one place in the codebase the user's own writing was unrecoverable.
- **Owner in both the stream id and the payload.** Rejected: two sources of truth ADR-0034 event
  surgery can desync, for no read the stream id can't already serve alone.
- **Server-side `Doc.normalize` before append.** Rejected: would store a document different from
  the one the client sent, desynchronizing optimistic client state and permanently defeating the
  no-op-save rule.
- **Validating `BlockType` against the 16 known names.** Rejected: freezes the block-type
  vocabulary into the event schema; a 17th type would become a migration instead of a client
  change.
- **A single combined "Migrate & Purge" operator action.** Rejected: collapses two operations with
  incompatible recovery stories into one commit and forces the ambiguous-owner decision inline
  instead of letting the operator fix source data and come back.
- **Migration as a guarded boot routine** (the `GameJournal`/`StartupCutover` shape). Rejected:
  cannot pause for the ambiguous/orphan human decision — ADR-0056's own reasoning against
  unattended automation for a judgment-dependent outcome.
- **A prefix-only bulk-delete primitive.** Rejected: can't exclude unresolved owners' streams by
  construction; would push that safety property into UI convention instead of the primitive's own
  contract.
- **Keep join-order inference as a migration fallback** (ADR-0079's live-read pattern). Rejected
  for migration specifically: a wrong guess here commits a permanent event-log misattribution, not
  a self-healing display bug.
- **A single JSON-blob projection column instead of flat rows.** Rejected for now, on the strength
  of `imageRefColumns` and drift-check granularity; flagged as the fallback if log growth is judged
  unacceptable and never bounded.

## References

- ADR-0043 — event-worthiness / re-derivability; the doctrine this ADR closes the last open gap
  against (the user's own writing, previously unrecoverable in `game_journal_blocks`).
- ADR-0044 — table classification registry; amended here (`game_journal_blocks` leaves the
  `Imperative` list, `content_blocks` leaves the `Projected` list, `notes_blocks` joins it).
- ADR-0034 — event surgery guardrails; this ADR's Gate 2 reuses the VACUUM-INTO/preview-confirm/
  FTS-rebuild/checkpoint-rewind protocol verbatim and adds one new primitive to it.
- ADR-0056 — boot-routine vs. operator-executed criterion; applied to Gate 1/Gate 2 and extended
  from a wrong-filter failure mode to a classification failure mode.
- ADR-0079 — the `(MediaType, slug)` owner-typing pattern this ADR reuses for Notes' stream id, and
  the "report, don't guess" / "an event the projection can't insert is replay-breaking" reasoning
  this ADR's migration design applies a second time.
- `src/Server/ContentBlocks.fs`, `src/Server/ContentBlockProjection.fs`, `src/Server/GameJournal.fs`
  (`convertOldBlocks`, `migrateFromContentBlocks`), `src/Server/Api.fs` (`executeCommandCore`'s
  `Ok []` path), `src/Server/Administration.fs` (`boundedContextPrefixes`, `eventCodecs`,
  `handledEventTypesByBoundedContext`, `tableRegistry`, `imageRefColumns`, `runSurgeryMutation`),
  `src/Server/EventStore.fs` (`deleteEventRow`, `renameEventTypeRows`, the new
  `deleteEventsByStreamIds`/`previewBulkDeleteByStreamIds`), `src/Server/Composition.fs` (boot
  wiring), `src/Client/Components/JournalEditor.fs` (`Doc.normalize`, the debounce),
  `src/Shared/Shared.fs` (`JournalBlockDto`, `JournalBlockTypes`, `JournalBlock.hasContent`).
- `curation-h98ve`, `curation-knqfj`, `curation-j4qqt` — the tasks this ADR was written against.

## Amendment 2026-09-16 (curation-kezpv, retirement)

Gate 1 ("Migrate to Notes") and Gate 2 ("Purge legacy stores"), §10-§11's two operator-triggered
Administration actions, ran on harbour's live store on 2026-09-16: Gate 1 at 20:36:18Z (14
`Notes_saved` events on 14 `Notes-*` streams, zero `ContentBlocks-*` streams left unconverted),
Gate 2 between 22:38 and 22:57 CEST (`content_blocks` and `game_journal_blocks` dropped, six
ADR-0034 pre-purge backups taken). With both gates run, this task (curation-kezpv) deleted both
end to end: `IAdminApi.previewNotesMigration` / `runNotesMigration` / `previewPurgeLegacyNotes` /
`purgeLegacyNotes` and their DTOs (`NotesMigrationOwnerRef`, `NotesMigrationResolvedOwner`,
`NotesMigrationPreview`, `NotesMigrationReport`, `PurgeLegacyNotesPreview`), their
`Administration.fs` implementations (owner resolution, legacy-table reads,
`ContentBlockConversion.fs` in full), and the `AdminSurgery` page's Gate 1 / Gate 2 cards, `Msg`
cases and `PendingPurgeLegacyNotes` pending-action case. §11's general-purpose
`EventStore.deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` primitives are NOT retired —
they're reusable surgery infrastructure, not migration-specific — and §12's `Notes`/`notes_blocks`
registry entries stay permanently; nothing about the registries changes here.
