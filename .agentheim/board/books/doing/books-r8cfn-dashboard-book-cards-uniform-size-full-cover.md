---
id: books-r8cfn
title: Dashboard book poster cards all share one size and show the whole cover — the Books tab rails (Currently Reading / Recently Finished / Recently Added) and the All tab's Reading card stop sizing each card from its image and stop cropping covers to the 2:3 frame
status: doing
type: bug
context: books
created: 2026-09-27
completed:
depends_on: [design-system-001]
blocks: []
tags: [dashboard, poster, cover, books]
related_adrs: []
related_research: []
prior_art: []
---

## Why
On the Books dashboard the rails look broken. Card sizes follow a pattern of small, larger, very large, then small again, and most covers have their sides cut off. The builder wants every book card to be the same size and to show the entire cover.

## What
Two causes, both in `bookReadingPosterCard` (`src/Client/Pages/Dashboard/Views.fs:834`):

1. **Uneven sizes.** The card's outer `Html.a` has only `cursor-pointer group`. It is missing the fixed-width rail sizing that every other dashboard rail card carries: `flex-shrink-0 w-[120px] sm:w-[130px] snap-start` (see `gameRecentlyAddedPosterCard`, `:2638`). Inside `posterScroller`'s flex row, each card's width therefore comes from its image.
2. **Cropped covers.** The shared `.poster-image` uses `object-fit: cover` in the 2:3 `.poster-image-container`. Audible covers are square, so their sides are cropped.

The fix:
- Give the book card the same fixed rail width as the games cards, so every book card in a rail is the same size.
- **Keep the 2:3 frame** (builder's decision, so the card matches movies/games/series). Show the whole cover inside it with `object-fit: contain`, centred. Letterbox space above/below a square cover (or at the sides of an unusually wide one) shows the container's existing soft background, not an empty hole.
- Scope the `contain` behaviour to book covers only, e.g. a book-specific modifier class or a `DesignSystem` composition. Movie/series/game posters must keep `cover`, because their art is authored at 2:3.
- The card is also used in the All tab's "Reading" card, which uses the `PosterGrid` layout (`:1064`), and in expanded `WrappingRow` / `PosterGrid` views. Check that the fixed width does not break those layouts. Follow whatever the games cards do in the same layouts.
- Keep the progress scrim / Finished badge overlay attached to the bottom of the 2:3 frame, as it is now.

## Acceptance criteria
- [ ] `bookReadingPosterCard`'s root element carries the same fixed rail width classes as the games rail cards (`flex-shrink-0 w-[120px] sm:w-[130px] snap-start` or an equivalent shared composition).
- [ ] Book cover images render with `object-fit: contain`. Movie, series and game poster images still render with `object-fit: cover` (only book covers change).
- [ ] In the Books tab's Recently Added rail with mixed square and 2:3 covers, every card has the same rendered width and height (checkable via DOM `getBoundingClientRect` on the cards).
- [ ] A square cover shows in full inside the 2:3 frame, centred, with no side cropping. The letterbox area shows the container's soft background. [human-eye]
- [ ] The All tab's Reading card and the expanded views still lay the book cards out cleanly, with no overflow or collapsed cards.
- [ ] `npm run build` passes.

## Notes
- The builder chose to keep the 2:3 frame over a book-specific square frame (2026-09-27), to stay consistent with the other tabs' poster rails.
- Unrelated but nearby: design-system-k4tw8 (Movies filmstrip hover jitter) also touches poster-card CSS. Avoid conflicting edits to `.poster-card` / `.poster-image-container` transforms.
