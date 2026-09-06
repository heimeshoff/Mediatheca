---
id: intelligence-f6cfv
title: Dashboard "Next episode" card — watched-with friends become circular avatars pinned top-left; the In focus badge is dropped
status: todo
type: refactor
context: intelligence
created: 2026-09-06
completed:
depends_on: [design-system-001, intelligence-wecjh]
blocks: []
tags: [dashboard, frontend, series, next-up, hero-card, friends, avatars]
related_adrs: []
related_research: []
prior_art: [intelligence-h7v2q, intelligence-dq8rk]
---

## Why
`intelligence-h7v2q` built the "Next episode" hero card with the watched-with friends
rendered as name pills at the **bottom** of the scrim overlay — the last item in a stack
of four (series name → episode label → segmented progress → friend pills). That makes
the people you watched with the visually least important thing on the card, and it
pushes the bottom overlay tall enough that it eats into the backdrop image.

They should read as *who this is with*, glanceable before you read anything else — so
they belong in the card's top-left corner, opposite the Jellyfin play button, as plain
circular avatars. The name is redundant there: the face is the identifier.

The **In focus** badge currently sits in that top-left slot. The builder's call is that
the card doesn't need it any more — In Focus already earns those series their position
at the front of the row through sorting, so the badge is repeating information the
placement already carries. Removing it is what frees the corner.

## What
Two files. The card itself is `DesignSystem.nextEpisodeHeroCard` in
`src/Client/DesignSystem.fs` (§ "Next episode hero card", roughly lines 800–916); its
only call site is `seriesNextEpisodeCard` in `src/Client/Pages/Dashboard/Views.fs`
(~line 1115).

### 1. Drop the In focus badge from this card
- Delete the `if props.InFocus then …` block (the `absolute top-3 left-3 z-10`
  `statusBadge InFocus`) from `nextEpisodeHeroCard`.
- Delete the `InFocus: bool` field from `NextEpisodeHeroCardProps`.
- Delete the corresponding `InFocus = item.InFocus` line at the `Views.fs` call site.
- `statusBadge` / `LifecycleStatus` / the gold-sweep animation stay — they are still
  used by the styleguide's `heroCard` and elsewhere. This removes one *instance*, not
  the badge component.

### 2. Move the watched-with friends to the top-left, avatar-only
- Remove the friend-pill row (the `if not props.WatchedWith.IsEmpty then …` block, last
  child of the bottom scrim's `flex flex-col gap-1.5` stack).
- Render the same `props.WatchedWith : NextEpisodeHeroFriend list` in a new
  absolutely-positioned block in the vacated corner — `absolute top-3 left-3 z-10`, so it
  sits above the scrim and the `posterShine` overlay and aligns on the same top row as
  the Jellyfin play button opposite it.
- **Overlapping stack**, per the styleguide's `heroCard` avatar treatment: a
  `flex items-center` row with negative horizontal spacing (`-space-x-3`) and a `ring-2`
  on each avatar so adjacent circles separate. Pick the ring colour so the separation
  reads over a *photographic* backdrop — the styleguide specimen's `ring-base-100` sits
  on a flat gradient, this card does not; match whatever the rest of this card's overlay
  chrome does against the backdrop if `ring-base-100` reads muddy.
- **Size:** ~40px (`w-10 h-10`) — deliberately the same as the Jellyfin play button
  (`w-10 h-10 rounded-full`, top-right) so the two corners balance.
- **Content:** the friend's image (`ImageRef` → `/images/{ref}`, `rounded-full`,
  `object-cover`) when present; otherwise a filled circle showing the **first letter of
  `Name`, uppercased**, centred, in the sans voice.
- **The name is no longer rendered inline.** Keep it reachable: `alt` on the image and a
  `title` on the anchor, both the friend's name.
- **Keep the existing link semantics unchanged** — each avatar stays an `Html.a` with the
  friend's `Href`, calling `preventDefault()` + `stopPropagation()` then `OnClick()`, so
  clicking a face goes to the friend's page and does *not* trigger the card's
  navigate-to-series click-through.

### 3. Let the remaining content fill the freed space
The bottom scrim block is `absolute bottom-0 … flex flex-col gap-1.5`, so removing its
last child makes it shorter and the series name / episode label / segmented progress
settle lower on their own — no explicit repositioning needed. Check the result visually
and only adjust padding if the stack now reads cramped against the bottom edge.

Scope is the Dashboard **All tab** "Next episode" scroller (`seriesNextUpOpenScroller`).
The Series-tab next-up list rows use the separate page-local `friendPill` helper
(`Views.fs:202`) and are **out of scope** — do not change them.

## Acceptance criteria
- [ ] On the Dashboard All tab, each "Next episode" card shows its watched-with friends
      as circular avatars in the **top-left** corner, and nowhere else on the card.
- [ ] Each avatar is roughly the size of the Jellyfin play button (~40px) and the two
      sit on the same top row, one per corner.
- [ ] Avatars overlap in a stack with a ring separating adjacent circles.
- [ ] A friend with an image shows that image, circular and `object-cover`; a friend
      without one shows the uppercased first letter of their name.
- [ ] No friend **name** text is rendered on the card; the name is still available via
      the anchor `title` and the image `alt`.
- [ ] Clicking an avatar navigates to that friend's page and does **not** navigate to
      the series detail page; clicking anywhere else on the card still goes to the
      series.
- [ ] The "In focus" badge no longer renders on this card, and `InFocus` is gone from
      `NextEpisodeHeroCardProps` and from the `Views.fs` call site.
- [ ] `statusBadge` and its In-focus variant still exist and still render in the
      styleguide's `heroCard` specimen.
- [ ] The series name, episode label, and segmented progress sit lower in the card than
      before, in the space the pill row vacated.
- [ ] `npm run build` succeeds (Fable compile is the type-level gate for both files).

## Notes
- **Sequencing:** `depends_on` includes `intelligence-wecjh`, the in-flight dead-code
  sweep of `Dashboard/Views.fs` — it deletes ~2000 lines of that file by line range, so
  this task's small edit to `seriesNextEpisodeCard` should land after it, not race it.
  The card body itself lives in `DesignSystem.fs` and is untouched by that sweep.
- **Styleguide gate** (`design-system-001`): this reuses an existing styleguide pattern
  (the `heroCard` overlapping avatar stack) rather than inventing one, so no new
  specimen is required. If the ring colour has to deviate from the styleguide's
  `ring-base-100` to read over a backdrop, that is a card-local adaptation, not a
  design-system change — note it in the BC README rather than editing the StyleGuide page.
- **Overflow:** a card is 280–320px wide and the play button claims ~52px on the right.
  At `w-10` with `-space-x-3` the stack grows ~28px per extra friend, so six friends
  still clear the button. No cap is specified; if a real series ever collides, tighten
  the overlap rather than truncating the list.
- **No test expected.** The client's `*.test.fs` suite (ADR-0064) covers logic, not
  render output, and there is no existing test for this card. Verification is
  `npm run build` plus a look at the running dashboard.
- The animated In-focus gold sweep (`design-system-bky6v`) is unaffected — this task
  removes a *usage*, not the effect.
