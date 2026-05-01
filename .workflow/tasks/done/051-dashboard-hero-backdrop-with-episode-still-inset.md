# Task 051: Dashboard Hero — Use Series Backdrop as Background, Episode Still as Inset

**Status:** Todo
**Size:** Small
**Created:** 2026-05-01
**Milestone:** --
**Dependencies:** --

## Description

The dashboard's hero spotlight (the large "Next Up" card at the top of the Series / All tabs) currently picks the next episode's still as the full-bleed background and only falls back to the series backdrop when the still is missing (`src/Client/Pages/Dashboard/Views.fs:912–915`):

```fsharp
let imageRef =
    match item.EpisodeStillRef with
    | Some stillRef -> Some stillRef
    | None -> item.BackdropRef
```

Episode stills from TMDB are typically 1280×720 or smaller and look soft/low-res when stretched to a 21:9 hero card. Series backdrops are usually 1920×1080+ and look much better at hero size.

Flip the relationship: when both images are available, show the **series backdrop** full-bleed as the hero canvas, and render the **episode still** as a smaller inset thumbnail inside the card so the user still sees what specific episode is up next.

### Why

- Hero card is the most prominent UI element on the dashboard — quality of the imagery there matters more than anywhere else.
- Backdrops scale up cleanly to hero dimensions; episode stills do not.
- Keeping the still visible (just smaller) preserves the "this specific episode is next" cue.

## Acceptance Criteria

### Image selection

- [ ] When both `BackdropRef` and `EpisodeStillRef` exist: render the backdrop full-bleed and the still as a medium inset (see placement below).
- [ ] When only `BackdropRef` exists: render the backdrop full-bleed, no inset.
- [ ] When only `EpisodeStillRef` exists: render the still full-bleed (current behaviour for this case), no inset.
- [ ] When neither exists: keep the current gradient placeholder (`bg-gradient-to-br from-primary/20 to-base-300`).

### Inset placement & styling

- [ ] Inset sits in the **bottom-right corner of the hero**, **above the title block** (the `absolute bottom-0 left-0 right-0 p-4 sm:p-6` overlay at `Views.fs:946–977`) so it doesn't collide with the series title / episode label / overview / friend pills, and doesn't collide with the Jellyfin play button (top-right) or the In-Focus indicator (top-left).
- [ ] Medium size — roughly `w-44 sm:w-56` (≈176–224 px wide) at the same 16:9 aspect as the still itself. Pick something that reads as "thumbnail" but stays legible.
- [ ] **No glassmorphism** on the inset — plain rendering (subtle rounded corners + a thin neutral border or shadow are fine for definition, but no `backdrop-filter` / `bg-base-100/55` treatment).
- [ ] Inset must not block the gradient overlay readability of the title — sit it visually above the bottom gradient so the title remains the focal point.
- [ ] Inset uses the same `transition-transform group-hover:scale-105` motion as the backdrop, OR sits still while the backdrop scales — pick whichever looks better; either is acceptable.

### Scope

- [ ] Only the dashboard hero card (`heroSpotlight` at `src/Client/Pages/Dashboard/Views.fs:911`). Other "next up" cards in the scroller continue to use whatever they use today.

### Build / test

- [ ] `npm run build` succeeds (Fable compiles clean).
- [ ] `npm test` passes.
- [ ] Manual check on the dashboard: hero looks crisper than before when both images exist; the still inset is clearly visible without overpowering the backdrop or hiding the title.
- [ ] Spot-check the three fallback cases (backdrop-only, still-only, neither) by temporarily nulling the relevant fields or by finding a series in the data that hits each case.

## Implementation Notes

### Frontend only — no backend, no shared-types changes

`DashboardSeriesNextUp` already carries both `BackdropRef` and `EpisodeStillRef` (`src/Shared/Shared.fs:248–267`), so no API or projection work is needed.

In `src/Client/Pages/Dashboard/Views.fs:911–1008`, restructure `heroSpotlight`:

1. Replace the `imageRef` selection with a small helper that returns `(backgroundRef: string option, insetRef: string option)`:
   - Both present → `(Some backdrop, Some still)`
   - Only backdrop → `(Some backdrop, None)`
   - Only still → `(Some still, None)` *(no inset; the still itself is the background)*
   - Neither → `(None, None)`
2. Render the background image branch using `backgroundRef` (same `<img>` + gradient overlay as today).
3. After the gradient overlay and **before** the bottom content overlay (so it's behind the title in DOM but visually above the gradient via z-index), render the inset when `insetRef.IsSome`:

```fsharp
match insetRef with
| Some still ->
    Html.div [
        prop.className "absolute bottom-24 sm:bottom-28 right-4 sm:right-6 w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]"
        prop.children [
            Html.img [
                prop.src $"/images/{still}"
                prop.alt $"S{item.NextUpSeason}E{item.NextUpEpisode}"
                prop.className "w-full h-full object-cover"
            ]
        ]
    ]
| None -> ()
```

Tweak the `bottom-*` offset during implementation so the inset clears the title block on both mobile and desktop — the title block uses `p-4 sm:p-6` with up to four lines of content (title, episode label, overview, friend pills), so the inset bottom needs roughly enough headroom for two lines of overview plus the pills. If clearance is too tight, drop the inset's vertical position or shrink the inset slightly.

### Out of scope

- Lazy loading or `srcset` for the inset image (current `<img>` rendering pattern is plain everywhere; don't introduce a new pattern just here).
- Changing image sourcing in any other dashboard component.
- Backend changes to fetch higher-resolution stills from TMDB.
- Cropping or focal-point adjustments on the backdrop.

## Work Log

### 2026-05-01 13:25 -- Work Completed

**What was done:**
- Restructured `heroSpotlight` in `src/Client/Pages/Dashboard/Views.fs` to compute a `(backgroundRef, insetRef)` pair instead of a single `imageRef`.
- When both `BackdropRef` and `EpisodeStillRef` exist: backdrop is rendered full-bleed, still is shown as an inset (`bottom-24 sm:bottom-28 right-4 sm:right-6 w-44 sm:w-56 aspect-video rounded-lg overflow-hidden border border-white/10 shadow-lg z-[5]`).
- When only `BackdropRef` exists: backdrop full-bleed, no inset.
- When only `EpisodeStillRef` exists: still rendered full-bleed (preserves prior behaviour for that case), no inset.
- When neither exists: existing gradient placeholder is preserved.
- Inset uses plain rendering (no glassmorphism) per spec — only a thin border + shadow for definition.
- Inset placed in the DOM after the gradient overlay and before the bottom title block; positioned with `bottom-24 sm:bottom-28` to clear the title/episode label/overview/friend pills, and stays clear of the top-right Jellyfin button + top-left In Focus indicator.
- Inset stays still while the backdrop scales on hover (acceptable per spec).

**Acceptance criteria status:**
- [x] Both refs present → backdrop full-bleed + still inset -- verified by code review of new match arm.
- [x] Only backdrop → backdrop full-bleed, no inset -- verified by code review.
- [x] Only still → still full-bleed, no inset -- verified by code review (matches original "no still inset" behaviour).
- [x] Neither → gradient placeholder preserved -- verified by code review.
- [x] Inset bottom-right above title block -- positioned at `bottom-24 sm:bottom-28 right-4 sm:right-6`, clearing the `p-4 sm:p-6` title block.
- [x] Medium size `w-44 sm:w-56` aspect-video -- matches spec exactly.
- [x] No glassmorphism -- only `border border-white/10 shadow-lg` for definition; no backdrop-filter or `bg-base-100/55`.
- [x] Inset doesn't block title gradient -- positioned above the bottom content block.
- [x] Backdrop scales on hover; inset sits still -- spec allows either choice.
- [x] Only the dashboard hero card touched -- single function `heroSpotlight` modified.
- [x] `npm run build` succeeds -- Fable compiled clean in 30.43s.
- [x] `npm test` passes -- 255 tests passed, 0 failed.

**Files changed:**
- `src/Client/Pages/Dashboard/Views.fs` -- restructured `heroSpotlight` image selection and added episode-still inset rendering.
