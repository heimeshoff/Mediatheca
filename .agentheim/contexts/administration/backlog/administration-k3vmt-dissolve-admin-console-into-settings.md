---
id: administration-k3vmt
title: Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button
status: backlog
type: refactor
context: administration
created: 2026-08-01
completed:
depends_on: [design-system-001]
blocks: []
tags: [admin-console, navigation, ui, settings]
related_adrs: [0017, 0023, 0034, 0027]
related_research: []
prior_art: [administration-p0jka, administration-v4y9g, administration-wwc36, administration-a4d9b, administration-svq3t]
---

## Why

The sidebar's bottom group carries two buttons — **Admin** and **Settings** — for what the
builder experiences as one destination. Both are the Administration BC's operator surface:
Settings holds Integrations + Data Imports, Admin holds Events / Projections / Health /
Images / Jobs / Surgery. Splitting them across two nav entries is information architecture
the builder has rejected outright: *"I don't want to have two different buttons in the menu
on the bottom left for these two different sections."*

Secondary pressure the current split creates: `Components/BottomNav.fs`'s mobile dock has
**no** Admin item at all, so the entire admin console is unreachable on mobile today. One
Settings destination fixes that as a side effect.

## What

**Fully inline, one page** — the builder's explicit choice over the two lighter alternatives
offered at capture (a section of links into an unchanged `/admin/*`, or re-parenting the
routes to `/settings/admin/*`). Both were declined; admin stops being its own page.

- `src/Client/Pages/Admin/` shell dissolves. Its page header ("Administration") and
  `tabBar` disappear; the six tab views render as **collapsible sections on the Settings
  page**, placed below the existing Data Imports section, in the tab bar's current order:
  Events, Projections, Health, Images, Jobs, Surgery.
- `Router.Page.Admin of AdminTab` is removed. `/admin`, `/admin/<tab>` and the legacy
  `/events` alias all resolve to `Settings` — no dead route, no orphaned `AdminTab` DU.
- `Sidebar.bottomNavItems` drops to a single entry (Settings). `BottomNav.dockItems` is
  already Settings-only and needs no change.
- **`Stream_detail` (`/admin/streams/<streamId>`) survives as its own page.** It is
  parameterized — one page per stream — and cannot be a section. It was already a
  top-level `Page` case rather than an `AdminTab` variant (administration-v4y9g), so it
  outlives the shell cleanly; only its "back to admin" affordances need re-pointing.
- Collapsed sections must not fetch. Settings is where the builder goes to paste an API
  key; that visit must not trigger a health scan, a projection-stats query, a job-status
  poll and an event page.

## Acceptance criteria

- [ ] `Sidebar.bottomNavItems` contains exactly one item, `Settings`; no `Admin` entry
      remains anywhere in `src/Client/Components/Sidebar.fs`.
- [ ] `/settings` renders the six administration sections below the Data Imports section,
      in order: Events, Projections, Health, Images, Jobs, Surgery.
- [ ] `Router.Page` has no `Admin` case, and `Route.parseUrl` maps `["admin"]`,
      `["admin"; <tab>]` and the legacy `["events"]` to `Settings` — every former admin URL
      still resolves to something, none to `Not_found`.
- [ ] `Stream_detail` still parses at `/admin/streams/<id>`, still renders its timeline +
      projection panel, and the event browser's clickable stream ids still reach it.
- [ ] Navigating away from `/settings` tears down the Events live-tail poll: ADR-0023's
      Follow-epoch bump is re-keyed from "leaving `Admin _`" to "leaving `Settings`", proven
      by a test that asserts no `getEventsAfter` traffic after the navigation — not by
      static inspection.
- [ ] Collapsing the Events section without navigating also stops the live-tail poll (a
      teardown trigger with no equivalent in today's code).
- [ ] The projections-dirty banner (ADR-0034) still appears after a committed surgery
      mutation and still clears once every projection's `Lag` returns to 0, from its new
      home inside the Settings page.
- [ ] Visiting `/settings` issues **no** admin data query for a section that is collapsed —
      the page's initial network cost is unchanged from today's Settings page.
- [ ] `npm run build` succeeds (Fable compiles) and `npm test` is green.
- [ ] `tests/e2e/event-tail-follow.spec.ts` and `tests/e2e/admin-surgery.spec.ts` pass
      against the new location, with `admin-surgery.spec.ts`'s
      `test.skip(!process.env.CI, ...)` gate preserved verbatim.
- [ ] The Settings page reads as one coherent page rather than two apps stapled together —
      the administration sections are visibly subordinate to Settings' own chrome, not
      competing with it. [human-eye]

## Notes

Under-refined on purpose — the shape is chosen but six mechanics are open. Refinement
should settle these before promotion:

1. **MVU shape — the biggest open question.** `Pages/Settings/Types.fs`'s `Model` is
   already large (four integrations + two importers, with per-field input/saving/result
   triples). Does Settings absorb the six admin child models and six `Msg` wrappers
   directly, or does `Pages/Admin/State.fs` survive as a *headless composite child* — an
   `AdministrationModel` Settings holds in one field — with only `Views.fs`'s shell chrome
   (header + `tabBar`) deleted? The second keeps the diff far smaller and preserves
   `Admin.State`'s existing `Surgery_msg` handler, which reloads `ProjectionsModel.Stats`
   immediately after every committed mutation so the dirty banner reacts (ADR-0034).

2. **Deep-linkability, and what the dirty banner links to.** `/admin/projections` is
   bookmarkable today, and `Admin.Views.dirtyBanner` renders a "Go to Projections" link
   built from `Route.toUrl (Admin AdminProjections)`. Inline sections lose both unless
   Settings takes a section address — `/settings#projections`, or a `Settings of section`
   route. The banner needs *some* target; decide whether the general deep-link capability
   comes with it or is dropped.

3. **Live-tail teardown re-keying (ADR-0023).** The Follow epoch is bumped in root
   `State.Url_changed` when leaving `Admin _` for any other page. Two changes: the trigger
   becomes "leaving `Settings`", and a second, genuinely new trigger is needed for
   section-collapse-without-navigation (criterion 6). Worth checking whether the epoch
   guard can be driven from one place for both.

4. **Section-load trigger.** Load-on-first-expand, or refetch on every expand? Affects
   criterion 8 and whether re-expanding Health shows stale stats. Note that Settings
   already uses the `collapse collapse-arrow` idiom for its integration cards, so the
   collapsible mechanism itself is established — but those cards' content is already in
   `Model` at page load, which is exactly what must **not** happen here.

5. **E2E spec rework scope.** Both specs navigate `/admin/*` URLs and click the tab bar to
   switch tabs. `admin-surgery.spec.ts` is destructive and gated by
   `test.skip(!process.env.CI, ...)` — the deliberate inverse of `playwright.config.ts`'s
   `reuseExistingServer: !process.env.CI` — a precedent administration-svq3t established
   for every future destructive spec, to be preserved exactly.

6. **Does this warrant an ADR?** It retracts the `/admin` tabbed-shell shape
   administration-p0jka established alongside ADR-0017 (the `IAdminApi` contract itself is
   untouched — only its client shell), amends ADR-0023's teardown trigger, and moves
   ADR-0034's cross-tab banner. Three governing decisions nudged by one change; likely yes.

7. **Mobile.** `BottomNav`'s Settings item now reaches the whole admin console for the
   first time. Wanted, or should the heavier sections (Events browser, Surgery) be
   desktop-only?

**Design-system touch.** `Sidebar.fs`'s bottom group is design-system-governed chrome
(styleguide § 4 Sidebar nav — design-system-t4b9k, reverted by design-system-grtw7,
viewport-pinned by design-system-vk7rd). `DesignSystem.navGroupBottom`'s `mt-auto` layout
should be eyeballed with a single item in the group. Frontend gate satisfied via
`depends_on: [design-system-001]` (done), the same dependency administration-p0jka carried.
