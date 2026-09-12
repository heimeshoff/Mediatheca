---
id: intelligence-m09d4
title: Dashboard card expand/collapse grows in place — the card surface and its already-rendered items travel to their expanded positions in ~0.5s via the design-system FLIP primitive, later-fetched items join below without disturbing them, and the 50ms scroll guess in State.fs is retired (ADR-0073)
status: done
type: feature
context: intelligence
created: 2026-09-12
completed:
depends_on: [design-system-m2v88, design-system-001]
blocks: []
tags: [dashboard, expand-card, motion, animation, flip]
related_adrs: [0073, 0015, 0064]
related_research: []
prior_art: []
---

## Why

Commit 74e1ab8 shipped expandable dashboard cards: an expand button grows a query-backed
card over the whole tab area and re-runs its query without the row limit. Today the grow
is a subtree swap — the collapsed card becomes `invisible` and the expanded surface appears
at full size in the same frame, then the whole list re-renders when the unlimited fetch
lands. The builder's ask, verbatim: clicking expand should *not* jump to full size; the card
should get there within roughly half a second, and the elements that were already rendered
in the small version should move to where they sit in the expanded version, so it reads as
one dynamic process. Refined after the builder checked the running app (2026-09-12): today
the expanded list *fades in* whole (the `animate-fade-in-up` entrance on the grown
surface); what they want instead is the already-rendered movies moving *upwards* so they
sit at the top of the screen, with the rest of the screen then filling up with the new
entries.

## What

Wire the design-system FLIP primitive (`design-system-m2v88`, ADR-0073) into
`src/Client/Pages/Dashboard/Views.fs`'s expand/collapse:

- Wrap `tabArea`'s grown-surface branch in a `[<ReactComponent>]` (e.g. `GrowingTabArea`)
  that holds a `React.useRef` before-snapshot and the live `Animation` handles, driven by
  one `useLayoutEffect` keyed on `Expanded` — the same effect serves expand and collapse
  ("`Expanded` changed and a snapshot exists → play the plan").
- Take the snapshot (`Motion.Flip.snapshot` over the tab area) in the expand and collapse
  buttons' `onClick`, *before* `dispatch`. Cancel in-flight animations before re-measuring
  on re-entry, so rapid toggling never leaves a stray transform.
- Give every expandable item renderer a `Motion.flipKey` — movie/series/game slug, person
  name, achievement composite id — scoped per card so the All tab and a media tab reading
  the same query never clash. The card surface itself gets a key too, for the height grow
  (`growSurface`), independent of the item transforms.
- `ExpandedItemsLoaded` does not animate: the unlimited list extends the limited one in
  order, so the fetched items append below the ones that travelled, and the travelled ones
  do not move again.
- Move the scroll-into-view into the same layout effect, **instant** (`behavior:
  "instant"`) and *before* the after-snapshot, so the posters' measured travel includes
  the scroll shift: on screen they slide upward to the top of the viewport and the new
  entries fill the rest of the screen below them. Delete `State.scrollToExpandedCardCmd`,
  its 50ms `setTimeout`, and the `Cmd.batch` in `ExpandCard`. No shape change to `Model`,
  `ExpandedItems`, or `Msg`.
- Drop `animate-fade-in-up` from the expanded surface. Today `chromeClass` gives it the
  same fade-in-up entrance as a freshly rendered card, which is the "fully expanded list
  fades in" the builder saw on the running app (2026-09-12) and rejected. The grown surface
  enters through the FLIP travel and the height grow only.
- Reduced motion is handled by the primitive (no-op `play`), nothing extra here.

## Acceptance criteria

- [ ] The existing `ExpandCard.test.fs` cases pass unmodified, except any assertion on `ExpandCard`'s scroll command, which now asserts the fetch is the only command (confirms no model-shape change).
- [ ] `State.fs` no longer contains `scrollToExpandedCardCmd` or a `setTimeout`; `ExpandCard` returns only `fetchExpandedItems`.
- [ ] Every item renderer used by an `expandable` spec applies `Motion.flipKey`; `npm run build` and a dev-server smoke run show no duplicate-key React warnings in the console.
- [ ] The expand-time scroll still brings the expanded card's top into view, now triggered from the layout effect — verified by code read (DOM scroll assertions are outside Vitest/jsdom's reach, same boundary as today's tests).
- [ ] `npm run build`, `npm test`, `npm run test:client` pass.
- [ ] Expanding a poster-rail card (e.g. Movies to Watch): the already-visible posters slide upward on screen to the top of the viewport, into their expanded grid positions, in roughly half a second, with no fade-in of a whole new list and no flash or jump; the posters that arrive from the unlimited fetch then fill the rest of the screen below them without disturbing the ones that moved. [human-eye]
- [ ] The expanded surface no longer carries `animate-fade-in-up` (code read; the class stays on collapsed cards).
- [ ] Expanding a list card that re-flows into tiles (e.g. Series Next Up): no text stretching or distortion during the transition. [human-eye]
- [ ] Collapse mirrors expand — items travel back and the card shrinks within the same ~0.5s budget. [human-eye]
- [ ] With `prefers-reduced-motion: reduce` emulated, expand and collapse show no travel; card and items are at their final positions immediately. [human-eye]
- [ ] Rapidly toggling expand/collapse before the animation settles leaves no item visibly stuck off its resting position. [human-eye]

## Notes

- ADR-0073 carries the technique decision and the finding that `Program.withReactSynchronous`
  commits on a concurrent `createRoot` (so nothing post-commit may hang off `dispatch`).
- Prior art is commit 74e1ab8 itself (no task file — it was built outside the board) and
  `intelligence-dq8rk`'s 3a layout that `tabArea` sits in. `LocalCopyRemovalDialog` is the
  precedent for keeping transient DOM/animation state out of the Elmish model.
- Recent Achievements cannot widen on expand (Steam keeps ten unlocks); its expand still
  animates the surface, with nothing new to append.
- `clip-path: inset()` is the documented escape hatch if animating the surface's `height`
  costs frames.

## Verifier note (iteration 1)

**VERDICT: FAIL** — all three runners are green (`npm run build` exit 0, 41.17s, only the known-benign FS0020 in `Pages/AdminProjections/Views.fs`; `npm test` 769 passed / 0 failed; `npm run test:client` 61 passed / 10 files, up from the 60 baseline). This is check 6b: the diff silently ignores a constraint ADR-0073 itself records.

### REASONS

**1. The expand direction animates the wrong elements — the visible item travel never plays.**
In `Views.fs:380-395` the FLIP root (`containerRef`, on the `grid grid-cols-1` div) has the `invisible` collapsed content as its **first** child and the grown `surface` as its **second**. The expanding card's items therefore carry the same `data-flip-key` **twice** in the after-state DOM (e.g. `AllMoviesToWatch`: `movieToWatchFilmstripItem`'s `FilmstripItem.Key = item.Slug` at `Views.fs:546` via `DesignSystem.filmstripRow`, and `movieToWatchPosterCard`'s `Motion.flipKey item.Slug`; likewise `seriesTabPosterCard` on both faces of `SeriesNextUp`).

`Motion.Flip.snapshot`'s `Map.ofArray` (`Motion.fs:100-108`) is last-wins, so the *after* box is correctly the expanded one — but `Motion.Flip.play` resolves the element with `$0.querySelector('[data-flip-key="…"]')` (`Motion.fs:131`), which returns the **first** match in document order: the `visibility: hidden` collapsed clone. Called from `Views.fs:343`, every planned move is applied to a hidden element.

Net effect on expand: only the card-box `growSurface` (targeted by `id`, so correct) animates; no poster or row visibly travels — exactly the behaviour the task exists to replace. Collapse is unaffected (no duplicates once the surface unmounts), so the two directions are asymmetric.

ADR-0073 records this very hazard as rejection reason (a) for View Transitions ("`tabArea` deliberately keeps the collapsed subtree mounted under `invisible` … every item's name would appear twice"); its §1/§4 decision is that each surviving key's *element* is animated. The diff inherits the duplication without addressing it.

**2. The task's `## What` requirement that flip keys be card-scoped is unimplemented.**
`## What` states: "Give every expandable item renderer a `Motion.flipKey` — … **scoped per card so the All tab and a media tab reading the same query never clash**." Every key in the diff is a bare domain identifier (`item.Slug` throughout, `person.Name` at `Views.fs:1100`, `friend.Slug`, `string achievement.GameAppId + "-" + achievement.AchievementName`).

The worker's own `intelligence-fk3p9` backlog item concedes the resulting collision and its premise checks out — `gamesTabView` mounts `GamesRecentlyAdded` (`data.RecentlyAdded : GameListItem`) and `GamesUpcoming` (`data.Upcoming : GameListItem`) collapsed simultaneously (`Views.fs:2669-2679`), both keyed on `Slug`, so a newly-added unreleased game collides. Deferring an explicitly specified requirement to a follow-up bug is not the worker's call; the follow-up is a good catch but not a substitute for the spec.

**3. Secondary — not independently disqualifying, but fix in the same pass.**
- The layout effect's dependency is `[| box expanded |]` (`Views.fs:361`) and `ExpandedItemsLoaded` allocates a fresh `ExpandedCard` record (`State.fs:128`), so the effect re-fires when the unlimited fetch lands and re-issues the instant `scrollIntoView`. ADR-0073 §5 holds in substance (both refs are cleared, so nothing animates), but the re-scroll is an unintended side effect.
- `Motion.cancel` of in-flight handles runs *after* the scroll (`Views.fs:335` then `:340`) rather than before it — harmless for the measurement (cancel still precedes the after-snapshot), but the scroll target is computed against a possibly mid-grow surface height.

### Discharged by code read and recorded as MET (do not redo)
- `State.fs` contains no `scrollToExpandedCardCmd` and no `setTimeout`; `ExpandCard` returns `fetchExpandedItems` alone (`State.fs:114-116`).
- The expanded surface uses `expandedChromeClass` with no `animate-fade-in-up`; `chromeClass` keeps it for collapsed cards (`Views.fs:121-140`, `:271`).
- All 14 distinct `RenderItem` functions across the 15 `expandable` specs, plus `DesignSystem.filmstripRow`, do apply `Motion.flipKey`.
- `filmstripRow` has only two call sites (Dashboard + the StyleGuide specimen at `Pages/StyleGuide/Views.fs:1748`) and the edit is additive — the cross-BC touch is acceptable.
- `Types.fs` is untouched and no model shape change was smuggled into `State.fs` (ADR-0073 §3 honoured).
- One `useLayoutEffect` serves both directions (§4); the scroll is `behavior: "instant"` and precedes the after-snapshot (§6).
- Every `ExpandCard`/`CollapseCard` dispatch routes through `snapshotAndDispatch` — `build` shadows `dispatch` inside all four tab views.

### SUGGESTED_FIX
Make the FLIP play target the *visible* element — e.g. suppress `data-flip-key` inside the `aria-hidden`/`invisible` collapsed subtree while a card is grown, or scope `Flip.play`'s lookup to the grown surface — so the inverted transform lands on the expanded item rather than its hidden clone; then implement the `## What`'s per-card key scoping (prefix each key with the owning `DashboardCard`) so `intelligence-fk3p9`'s collision cannot arise, and re-check that the collapsed and expanded faces of the same card still share an identical key. While there, narrow the layout effect's dependency so `ExpandedItemsLoaded` does not re-trigger the scroll.

### ITERATION_HINT
likely-fixable

## Outcome

Wired the design-system FLIP primitive (`Motion.fs`, ADR-0073, shipped by `design-system-m2v88` earlier the same session) into the dashboard's expand/collapse in `src/Client/Pages/Dashboard/Views.fs`, then fixed two defects and a secondary rough edge a verifier pass caught (iteration 1 -> 2).

- **`growingTabArea`** (`Views.fs`) is a `[<ReactComponent>]` that replaces the old `tabArea`. It owns a container ref (root of every `[data-flip-key]` item in the tab), a before-snapshot ref, live-animation handles, and a second ref/height pair for the card-box grow. It exposes a `snapshotAndDispatch` function to its `build` callback (which constructs the tab's cards via `expandable`), so every `ExpandCard`/`CollapseCard` dispatch is preceded by `Motion.Flip.snapshot` on the tab area and a height read off the card's own surface (`collapsedCardElementId`/`State.expandedCardElementId`). All four tab-view functions build their `CardHandle list * ReactElement list` inside that callback, so `expandable`'s buttons only ever see the wrapped dispatch.
- **Fix 1 — the visible-element defect (iteration 1 FAIL).** `tabArea` deliberately keeps the collapsed subtree mounted `invisible` while a card is grown, so an expanding item's `data-flip-key` exists twice in the DOM (hidden clone first in document order, grown surface second). `Motion.Flip.play`'s `querySelector` returns the first match, so playing against the whole container landed every move on the hidden clone — only the card-box grow was visible and no item travelled, which is exactly the behaviour this task exists to replace. ADR-0073 names this hazard as its rejection reason (a) against the View Transitions API; the first implementation inherited the constraint without handling it. Fixed by scoping the *play* lookup — **not** the snapshot, which still spans the whole container so a collapsed box can connect to its expanded element across the swap — to the grown surface when expanding (`document.getElementById State.expandedCardElementId`, a sibling of the hidden clone rather than an ancestor), and to the container when collapsing (React unmounts the surface during commit, before layout effects, so there is one instance per key then). No `Motion.fs` change.
- **Fix 2 — per-card key scoping (iteration 1 FAIL).** The task's `## What` required keys "scoped per card so the All tab and a media tab reading the same query never clash"; the first implementation used bare domain identifiers and deferred the collision to a follow-up bug. Every `expandable` item renderer's flip key is now `cardItemKey card itemKey` (`$"{card}-{itemKey}"`), threaded through each renderer's signature (a new required leading `card: DashboardCard` parameter) and through the intermediate section builders that call them (`personStatsSection`, `watchedWithSection`, `seriesWatchedWithSection`, `achievementsBody`, `posterRail`'s `render` partial application) rather than hand-added per call site — a card that forgets to pass its own `Card` value fails to compile. This closes the confirmed `GamesRecentlyAdded`/`GamesUpcoming` collision in-task; the iteration-1 `intelligence-fk3p9` backlog item was dropped rather than carried forward. `DesignSystem.filmstripRow`'s tile gets its card-scoped key via `movieToWatchFilmstripItem`'s `FilmstripItem.Key`, so design-system's generic component stays unaware of `DashboardCard` — the scoping happens before the record crosses the BC boundary. Both faces of the two split-path cards were re-checked to derive an identical string.
- **Fix 3 — secondary.** The layout effect's dependency is now `expandedCardKey` (a plain string compared by value; all 19 `DashboardCard` cases are fieldless and distinctly named) instead of `box expanded`, so `ExpandedItemsLoaded`'s fresh `ExpandedCard` record no longer re-fires the effect and re-triggers the instant scroll (ADR-0073 §5), while genuine `None <-> Some` and `Some a -> Some b` transitions still do. `Motion.cancel` of both in-flight handle sets now runs first in the effect, ahead of the scroll, so the scroll target is measured against a settled surface.
- **Effect order** (ADR-0073 §6), single `useLayoutEffect` serving both directions (§4): cancel in-flight handles -> instant `scrollIntoView` (`behavior: "instant"`, `block: "start"`, expand direction only) -> after-snapshot -> `Motion.Flip.plan`/`play` -> `Motion.growSurface`.
- **`collapsedCardElementId`** (new) gives each card's collapsed surface a stable DOM id, mirroring `State.expandedCardElementId`, so the wrapped dispatch can measure "before" height for the specific card toggling regardless of direction. `section` gained an optional element-id parameter to carry the new ids.
- **`chromeClass`/`expandedChromeClass`** split (ADR-0073 §6a): the expanded surface no longer carries `animate-fade-in-up` — only collapsed cards keep the mount entrance.
- **`State.fs`**: `scrollToExpandedCardCmd` and its 50ms `setTimeout` are deleted; `ExpandCard` returns `fetchExpandedItems api card` alone (no `Cmd.batch`). A TDD-driven test in `ExpandCard.test.fs` asserts the command list has length 1 — red at length 2 before the change, green after. `Types.fs` is untouched: no `Model`/`ExpandedItems`/`Msg` shape change (ADR-0073 §3).

Verification (runner exit status, both iterations independently re-run by the verifier from the worktree): `npm run build` exit 0, 0 errors, only the pre-existing unrelated FS0020 in `Pages/AdminProjections/Views.fs`; `npm test` 769 Expecto tests passed / 0 failed; `npm run test:client` 61 Vitest tests passed / 10 files.

Key files: `src/Client/Pages/Dashboard/Views.fs`, `src/Client/Pages/Dashboard/State.fs`, `src/Client/Pages/Dashboard/ExpandCard.test.fs`, `src/Client/DesignSystem.fs`.

**Builder eye-checks outstanding** (the six `[human-eye]` criteria — never a worker or verifier action). The verifier flagged two tiers as worth a close look: (1) a list card that re-flows into tiles (`Recently Finished`, `Most Watched With`) for text stretching, since those take `Flip.plan`'s `FadeIn` size-changed path; (2) the collapse direction — the effect deliberately scrolls only on expand, per ADR-0073 §6, so on collapse the document shortens and the browser may clamp `scrollTop`, and because boxes are viewport coordinates that clamp is carried into the travel. Within doctrine, but the one place collapse is not a strict mirror of expand.
