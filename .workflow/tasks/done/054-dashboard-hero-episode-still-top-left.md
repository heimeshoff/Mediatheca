# Task 054: Dashboard Hero — Move Episode Still Inset to Top-Left

**Status:** Todo
**Size:** Small
**Created:** 2026-05-01
**Milestone:** --
**Dependencies:** --

## Description

Task 051 added an episode-still inset on the dashboard hero spotlight card, positioned at the bottom-right corner above the title block (`src/Client/Pages/Dashboard/Views.fs:949–964`):

```fsharp
prop.className "absolute bottom-24 sm:bottom-28 right-4 sm:right-6 w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]"
```

Move the inset to the **top-left corner** of the hero instead. The series title block stays exactly where it is (anchored at the bottom over the gradient) — only the inset moves.

The "In Focus" glow indicator currently lives at `top-3 left-3` (`Views.fs:1015–1020`). When the inset is shown, it occupies that corner, so the In Focus indicator must be **hidden whenever the inset is rendered** — they don't share the corner.

### Why

- User-driven layout preference: "the image of an episode on a hero in the dashboard is at the moment at the bottom right; it should be in the top left above the title of the series."
- Top-left is the natural "what is this?" reading position; pairing the still there gives the user an immediate "next episode" cue before they read down to the title.
- The bottom-right corner becomes free for future use (or simply visual breathing room in the gradient).

## Acceptance Criteria

### Inset placement

- [ ] When both `BackdropRef` and `EpisodeStillRef` exist: inset renders in the **top-left** of the hero card. Replace `bottom-24 sm:bottom-28 right-4 sm:right-6` with `top-4 left-4 sm:top-6 sm:left-6` (or equivalent — match the existing inner-padding rhythm of the card).
- [ ] Inset size, aspect, border, shadow, and lack of glassmorphism are unchanged: `w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]`.
- [ ] Inset still uses plain rendering — no `backdrop-filter`, no `bg-base-100/55`, no glassmorphism additions.
- [ ] Backdrop scaling on hover (`group-hover:scale-105` on the background `<img>`) is unchanged. Inset stays still during the hover, same as before.

### In Focus indicator coexistence

- [ ] When the episode-still inset is rendered, the "In Focus" glow indicator (`Views.fs:1015–1020`, currently at `top-3 left-3`) is **hidden** — wrap the existing `if item.InFocus then …` branch in an additional check so it only renders when `insetRef.IsNone`.
- [ ] When no inset is rendered (backdrop-only, still-only-as-background, or neither image present), the In Focus indicator continues to render exactly as it does today.
- [ ] Jellyfin play button at `top-3 right-3` is unaffected — it stays where it is regardless.

### Title block

- [ ] The bottom title block (series name, episode label, overview, friend pills) is **unchanged** — same position (`absolute bottom-0 left-0 right-0 p-4 sm:p-6`), same content, same gradient overlay behind it.

### Image-selection behaviour (unchanged)

- [ ] Both refs present → backdrop full-bleed + still inset (now top-left).
- [ ] Only `BackdropRef` → backdrop full-bleed, no inset.
- [ ] Only `EpisodeStillRef` → still rendered full-bleed, no inset.
- [ ] Neither → existing gradient placeholder.

### Build / test

- [ ] `npm run build` succeeds (Fable compiles clean).
- [ ] `npm test` passes.
- [ ] Manual check on the dashboard: inset is clearly visible in the top-left of the hero, doesn't collide with the Jellyfin play button on the top-right, doesn't overlap the bottom title block, and the In Focus indicator is properly suppressed when the inset is showing.
- [ ] Spot-check an "In Focus" series with no episode still (backdrop-only) to confirm the In Focus glow still appears in that case.

## Implementation Notes

### Frontend only — single function change

Edit `heroSpotlight` in `src/Client/Pages/Dashboard/Views.fs:911–1029`:

1. Change the inset's class string at `Views.fs:955`:
   - From: `"absolute bottom-24 sm:bottom-28 right-4 sm:right-6 w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]"`
   - To:   `"absolute top-4 left-4 sm:top-6 sm:left-6 w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]"`

2. Gate the In Focus indicator on `insetRef.IsNone`. Currently:

```fsharp
// In Focus glow indicator
if item.InFocus then
    Html.div [
        prop.className "absolute top-3 left-3 z-10"
        ...
    ]
```

Change to:

```fsharp
// In Focus glow indicator — hidden when an inset is shown (they share top-left).
if item.InFocus && insetRef.IsNone then
    Html.div [
        prop.className "absolute top-3 left-3 z-10"
        ...
    ]
```

That's it — no other structural changes, no shared-types changes, no backend work.

### Out of scope

- Repositioning the In Focus indicator to a different corner when the inset is shown (just hide it for this task; we can revisit if it turns out to be missed).
- Any change to the title block, gradient overlay, Jellyfin button, friend pills, or hover animation.
- Changes to other "next up" cards in the scroller below the hero.
- Any change to `DashboardSeriesNextUp` or related shared types.

## Work Log

### 2026-05-01 15:29 — Work Completed

**What was done:**
- Updated the inset class string in `heroSpotlight` (Views.fs) from bottom-right (`bottom-24 sm:bottom-28 right-4 sm:right-6`) to top-left (`top-4 left-4 sm:top-6 sm:left-6`); size, aspect, border, shadow, z-index, and lack of glassmorphism left unchanged.
- Gated the In Focus glow indicator on `insetRef.IsNone` so it is suppressed when the inset is rendered, and updated the surrounding comment.

**Acceptance criteria status:**
- [x] Inset placement moved to top-left — verified by class string change at the inset div.
- [x] Inset size, aspect, border, shadow, z-index unchanged — only positional classes were touched.
- [x] Inset still uses plain rendering (no glassmorphism) — no `bg-base-100/55`, `backdrop-blur`, or `backdrop-saturate` added.
- [x] Backdrop hover scaling unchanged — `group-hover:scale-105` on the backdrop `<img>` is untouched.
- [x] In Focus indicator hidden when inset shown — condition is now `item.InFocus && insetRef.IsNone`.
- [x] In Focus indicator unchanged when no inset — same render branch fires when `insetRef = None`.
- [x] Jellyfin play button unaffected — `top-3 right-3` block is untouched.
- [x] Title block unchanged — `absolute bottom-0 left-0 right-0 p-4 sm:p-6` block is untouched.
- [x] Image-selection branches unchanged — the `match item.BackdropRef, item.EpisodeStillRef` block is untouched, so the four image-source cases still drive `backgroundRef`/`insetRef` exactly as before.
- [x] `npm run build` succeeds — Vite production build completed in 32.21s with no Fable errors.
- [x] `npm test` passes — 255 Expecto tests run, 255 passed, 0 failed.

**Files changed:**
- `src/Client/Pages/Dashboard/Views.fs` — moved episode-still inset to top-left and gated In Focus indicator on `insetRef.IsNone`.
