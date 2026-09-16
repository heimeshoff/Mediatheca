# Curation

## Purpose
User-created **collections** that group media across types — ordered lists of movies / series / games / books — plus **notes** (free-form block documents attached to a media item's detail page). The "I made a list" half of the app.

## Classification
**supporting** — Custom-built but orthogonal to the core "watch / play" loop.

## Actors
Single user.

## Ubiquitous language

- **Catalog** — a named, ordered collection of media items. E.g. "Cinemarco favorites", "Coop games for Marco + Alice".
- **Catalog entry** — one item in a catalog. Identity is `(MediaType, slug)`: a catalog may hold two entries of different media types sharing a slug (ADR-0079, restored to its original pair-identity wording by curation-w9fkq's `media_type` backfill and widened uniqueness constraint). Entries recorded before entries were typed carry no MediaType; an *inferred media type* was recorded for each by a one-off Administration-triggered backfill where resolvable, leaving genuinely ambiguous or orphaned slugs untyped and reported by name — the backfill ran on the live store 2026-09-16 and was retired by curation-kezpv. Has a position.
- **Reorder** — drag-and-drop position change; emitted as a single `Entries_reordered` event with the full new order.
- **Notes** — a single event-sourced block document per `(MediaType, slug)` (ADR-0080), replacing both ContentBlocks (movies/series/books) and the plain-storage GameJournal (games). Generalizes ContentBlocks into a typed annotations mechanism, closing this BC's former open question about whether it should.
- **Note block** — one node in a Notes document's tree (16 types: text, h1–h4, bullet, numbered, todo, toggle, quote, callout, code, link, image, columnList, column — `JournalBlockDto`/`JournalBlockTypes` in Shared).
- **Save** — the debounced whole-document write: the editor sends the full current block list, and the server appends a `Notes_saved` snapshot only when it differs (order-sensitively) from the current state — no diff, no per-block events.
- **Notes content** — the "is there anything to show" rule (`JournalBlock.hasContent` in Shared): non-whitespace text, or an image/link block with `ImageRef`/`Url` set; structural wrapper blocks with nothing typed into them don't count. Drives `HasNotesContent` on each detail DTO.
- **Inferred media type** — a `MediaType` recorded for a legacy (untyped) catalog entry by an exact-match resolution against exactly one of `movie_list` / `series_list` / `game_list` / `book_list`; appended as the corrective event `Entry_media_types_inferred`, never guessed — slugs matching zero or more than one list were reported, not converted (curation-w9fkq). The one-off Administration backfill action itself ran on the live store 2026-09-16 and was retired by curation-kezpv; the event type and its projection replay stay permanent.

## Aggregates

- **Catalog** — protects: entry positions stay contiguous; entries reference existing media; reordering preserves the entry set.

## Document streams (not aggregates)

- **Notes** — an event-sourced document stream, not an aggregate: there is no cross-block invariant to protect, only "the user typed; store what they typed." Built with the same decide/evolve/reconstitute/streamId/Serialization machinery as an aggregate purely because that machinery is the cheapest path to free expected-position concurrency, no-op handling, and projection catch-up — not because the concept deserves aggregate ceremony (ADR-0080).

## Key events

`Catalog_created`, `Catalog_updated`, `Entry_added`, `Entry_updated`, `Entry_removed`, `Entries_reordered`, plus `Notes_saved` (ADR-0080).
- **Entry_media_types_inferred** — no corresponding command; appended directly by an Administration action (ADR-0032), not through `decide` (curation-w9fkq).

## Key commands

`Create_catalog`, `Update_catalog`, `Add_entry`, `Update_entry`, `Remove_entry`, `Reorder_entries`, plus `Save_notes`.

## Relationships with other contexts

- **Conformist to:** Movies, Series, Games, Books. Catalog entries reference media items by `(MediaType, slug)`; Curation accepts whatever those BCs publish.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- ~~Should ContentBlocks become a more general "annotations on any aggregate" mechanism, or stay scoped to Curation?~~ Answered by ADR-0080 (2026-09-16): yes — generalized as Notes, keyed by `(MediaType, slug)`, owned by Curation. ContentBlocks itself was retired by curation-j4qqt.
