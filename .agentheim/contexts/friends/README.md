# Friends

## Purpose
Lightweight registry of **people you experience media with**. Friends are referenced by slug from every other BC; their existence is the foundation of the watched-with / played-with / recommended-by language.

## Classification
**supporting** — Necessary, custom-built, but not the heart of the product. Could plausibly be a tiny shared kernel.

## Actors
Single user.

## Ubiquitous language

- **Friend** — a person. Has a name, a stable slug (`FriendRef.slug`), an optional image, and crop settings for the image.
- **Slug** — the identifier referenced by every other BC. Stable; never reused.
- **Crop settings** — image positioning data (`cropOffsetX`, `cropOffsetY`, `cropZoom`) used when rendering the friend's avatar.

## Aggregates

- **Friend** — protects: slug uniqueness; image ref / crop settings stay consistent.

## Key events

`Friend_added`, `Friend_updated`.

## Key commands

`Add_friend`, `Update_friend`, `Update_crop_settings`.

## Relationships with other contexts

- **Upstream of (published language):** Movies, Series, Games, Curation, Journal. All of them reference friends by `slug` and copy `name` / `imageRef` into their projections.
- **No downstream consumption.** Friends doesn't read from any other BC.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- Whether to model "groups" (e.g. "the Sunday crew") as a first-class aggregate, or keep them implicit through ad-hoc multi-friend sessions.
- Friend-level intelligence (v2) — what reads should Friends own vs. delegate to [[intelligence]].
