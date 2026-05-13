# Curation

## Purpose
User-created **collections** that group media across types — ordered lists of movies / series / games — plus **content blocks** (free-form annotations attached to catalogs and detail pages). The "I made a list" half of the app.

## Classification
**supporting** — Custom-built but orthogonal to the core "watch / play" loop.

## Actors
Single user.

## Ubiquitous language

- **Catalog** — a named, ordered collection of media items. E.g. "Cinemarco favorites", "Coop games for Marco + Alice".
- **Catalog entry** — one item in a catalog. References a media item by `(MediaType, mediaId)`. Has a position.
- **Reorder** — drag-and-drop position change; emitted as a single `Entries_reordered` event with the full new order.
- **Content block** — a free-form chunk of content (text, image, link) attached to a context. Used on catalogs and detail pages.
- **Block type** — the discriminator of what kind of content a block holds (`ContentBlockType` in Shared).

## Aggregates

- **Catalog** — protects: entry positions stay contiguous; entries reference existing media; reordering preserves the entry set.
- **ContentBlock** — protects: blocks are typed; updates respect the type.

## Key events

`Catalog_created`, `Catalog_updated`, `Entry_added`, `Entry_updated`, `Entry_removed`, `Entries_reordered`, plus the ContentBlock event family (see `ContentBlocks.fs`).

## Key commands

`Create_catalog`, `Update_catalog`, `Add_entry`, `Update_entry`, `Remove_entry`, `Reorder_entries`, plus ContentBlock commands.

## Relationships with other contexts

- **Conformist to:** Movies, Series, Games. Catalog entries reference media items by id; Curation accepts whatever those BCs publish.
- **Indirect coupling:** Cinemarco import (in [[integration]]) creates Catalogs as part of its flow.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- Should ContentBlocks become a more general "annotations on any aggregate" mechanism, or stay scoped to Curation? Currently sits inside this BC.
