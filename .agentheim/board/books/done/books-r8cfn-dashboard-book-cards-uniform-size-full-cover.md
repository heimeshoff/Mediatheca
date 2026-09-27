---
id: books-r8cfn
title: Dashboard book poster cards all share one size and show the whole cover — the Books tab rails (Currently Reading / Recently Finished / Recently Added) and the All tab's Reading card stop sizing each card from its image and stop cropping covers to the 2:3 frame
status: done
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

## Verifier note (iteration 1)

**REASONS:**
- Copy-paste defect at `src/Client/Pages/Dashboard/Views.fs:841-858`: the new 9-line "Fixed rail width (books-r8cfn) ..." comment above `prop.className` in `bookReadingPosterCard` appears twice, word for word, back to back.
- Criterion 3 ("in the Books tab's Recently Added rail with mixed square and 2:3 covers, every card has the same rendered width and height"): the one covering test, `tests/e2e/dashboard-book-poster-cards-uniform-size.spec.ts`, seeds all three books with `CoverUrl: null` (on purpose, per its comments). It only renders the icon placeholder and never exercises the named cause (card width coming from its image) or the new `.poster-image--contain` image path.
- The pre-resolved command (`npm run build && npm run test:client`) does not run the Playwright spec; the fails-before/passes-after claim is unconfirmed by any runner the verifier invoked. Build and test:client both exit 0.
- Fine and should stay: `.poster-image` still `cover`; `.poster-card`/`.poster-image-container` hover rules unchanged; `posterImageContain`/`.poster-image--contain` is reusable and media-agnostic; only `bookReadingPosterCard` switched to it; no `.agentheim/` paths.

**SUGGESTED_FIX:** Delete the duplicated comment block. Change the e2e spec so the Recently Added rail holds books with real covers, at least one square and one 2:3, seeded locally without network access (fixture images or data-URI covers, if the add-book path allows it), then assert equal card sizes. Run the spec against a server booted on 127.0.0.1:5100 with a temp DATA_DIR and report its exit status.

**ITERATION_HINT:** likely-fixable

## Outcome

Fixed both causes of the broken Books dashboard rails in `bookReadingPosterCard` (`src/Client/Pages/Dashboard/Views.fs`):

1. **Uneven sizes** — the card's root `Html.a` now carries `flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start`, the same fixed rail width every other dashboard rail card (`gameRecentlyAddedPosterCard` etc) carries. Inside `posterScroller`'s flex row, every card now sizes from this class instead of its own title length or image, and the fixed width is a no-op (harmless) in the `PosterGrid`/`WrappingRow` layouts the same card renders in on the All tab and expanded views.
2. **Cropped covers** — the book cover `<img>` now uses `DesignSystem.posterImageContain` (`posterImage + " poster-image--contain"`), a media-agnostic CSS modifier (`.poster-image--contain { object-fit: contain; object-position: center; }` in `index.css`) added in iteration 1 and intended for reuse by `games-q7vnd` (next wave) for off-ratio game box art. Only `bookReadingPosterCard` uses it — movie, series, and true-2:3 game covers keep `.poster-image`'s default `object-fit: cover` unchanged.

**Iteration 2 fixes** (verifier note on the task file):
- Removed the accidental duplicate 9-line comment block above `prop.className` in `bookReadingPosterCard` (was pasted twice, word for word).
- Rewrote `tests/e2e/dashboard-book-poster-cards-uniform-size.spec.ts` to actually exercise both named causes. It no longer seeds `CoverUrl: null` for every book. It now seeds three books via the real `addBook` API: one with a square cover, one with a 2:3 cover, and one with no cover — the two covers are real PNG images (built by hand from bytes in the new `tests/e2e/pngFixture.ts`, no external dependency) served from a Node `http` server the spec starts and tears down itself on `127.0.0.1`, so the app server's `CoverUrl` fetch (`Api.fs`'s `addBookToLibraryImpl`, the manual-entry `addBook` path) never leaves the machine. The spec asserts: the covers decode to their real pixel dimensions (`naturalWidth`/`naturalHeight`), both render with computed `object-fit: contain`, and all three cards' `getBoundingClientRect` boxes match within 1px — proving both the fixed-width fix and the contain-fit fix together.
- Ran the spec for real against a server the worker booted itself: `npm run build` to populate `deploy/public`, then `dotnet build src/Server/Server.fsproj` and `dotnet src/Server/bin/Debug/net9.0/Server.dll` from the worktree root (so the server's `deploy/public` relative path resolves — `dotnet run --project` changes cwd to the project directory and 404s) with `ASPNETCORE_URLS=http://127.0.0.1:5100` and a throwaway `DATA_DIR` (never touching ports 5000/5173 or the live DB). The spec was run with a worker-local Playwright config (created inside the worktree, deleted before returning — never committed, per the task's instruction not to touch the tracked `playwright.config.ts`) pointing `baseURL` at `127.0.0.1:5100`.
- Proved the spec's fails-before/passes-after claim directly: temporarily reverted the fix in `Views.fs` (bare `"cursor-pointer group"` class, `DesignSystem.posterImage` instead of `posterImageContain`), rebuilt, and reran — the spec failed exactly on `expect(squareImg).toHaveCSS("object-fit", "contain")` (received `"cover"`). Restored the real fix, rebuilt, reran — 1 passed. Server process killed and the temp `DATA_DIR` removed afterward.
- `npm run build` passes (confirmed twice, once per rebuild above). `npm run test:client` (Vitest) passes: 18 files, 132 tests, all green.

Criterion 4 ("a square cover shows in full inside the 2:3 frame, centred, with no side cropping") is tagged `[human-eye]` in the task and is covered here only by the automated proxy (`object-fit: contain` computed style + correct natural dimensions), not a pixel-level visual check.
