---
id: administration-k3vmt
title: Dissolve the /admin console into Settings — its six tabs become inline collapsible sections below Data Imports, and the sidebar's bottom group drops to a single Settings button
status: done
type: refactor
context: administration
created: 2026-08-01
completed: 2026-08-01
depends_on: [design-system-001]
blocks: []
tags: [admin-console, navigation, ui, settings]
related_adrs: [0017, 0023, 0034, 0027, 0041]
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

- `Pages/Admin/` **survives as a headless composite child**, not as a page: `Types.fs` and
  `State.fs` stay exactly as they are, Settings holds the whole `AdminModel` in one field
  with one `Admin_msg` wrapper, and only `Views.fs`'s shell chrome — the "Administration"
  `h1` and `tabBar` — is deleted. This preserves `Admin.State`'s `Surgery_msg` handler, which
  reloads `ProjectionsModel.Stats` immediately after every committed mutation so the dirty
  banner reacts (ADR-0034), and keeps Settings' already-72-field `Model` from absorbing six
  more child models plus six `Msg` wrappers.
- The six tab views render as **collapsible sections on the Settings page**, below the
  existing Data Imports section, in the tab bar's current order: Events, Projections,
  Health, Images, Jobs, Surgery.
- `Router.Page.Admin of AdminTab` is removed, and the `AdminTab` DU with it. `/admin`,
  `/admin/<tab>` and the legacy `/events` alias all resolve to `Settings`. **`/settings` is
  the only address the settings page has** — no section route, no fragment. Per-section
  deep-linkability is deliberately dropped (see Notes).
- `Sidebar.bottomNavItems` drops to a single entry (Settings). `BottomNav.dockItems` is
  already Settings-only and needs no change. All six sections render on mobile — nothing is
  viewport-hidden.
- **`Stream_detail` (`/admin/streams/<streamId>`) survives as its own page.** It is
  parameterized — one page per stream — and cannot be a section. It was already a
  top-level `Page` case rather than an `AdminTab` variant (administration-v4y9g), so it
  outlives the shell cleanly; only its "back to admin" affordance and its sidebar-highlight
  predicate need re-pointing at `Settings`.
- **Lazy sections, with one deliberate exception.** A collapsed section fetches nothing —
  Settings is where the builder goes to paste an API key, and that visit must not trigger a
  health scan, an image-orphan scan, a job-status poll and an event page. The single
  exception is `getProjectionStats`, which must fire on every `/settings` visit regardless of
  collapse state, because the ADR-0034 dirty banner is client-derived from it and would
  otherwise never appear for a store left dirty by an earlier session.

## Acceptance criteria

- [x] `Sidebar.bottomNavItems` contains exactly one item, `Settings`; no `Admin` entry
      remains anywhere in `src/Client/Components/Sidebar.fs`.
- [x] The sidebar still highlights Settings while on a stream drill-in — `Route.isAdminSection`
      (or its successor) resolves `Stream_detail _` to the Settings nav item, so the drill-in
      doesn't leave the whole rail unhighlighted once `Admin` is gone.
- [x] `/settings` renders the six administration sections below the Data Imports section,
      in order: Events, Projections, Health, Images, Jobs, Surgery.
- [x] `Router.Page` has no `Admin` case and the `AdminTab` DU is gone; `Route.parseUrl` maps
      `["admin"]`, all six `["admin"; <tab>]` segments, and the legacy `["events"]` to
      `Settings` — every former admin URL still resolves to something, none to `Not_found`.
- [x] `Stream_detail` still parses at `/admin/streams/<id>`, still renders its timeline +
      projection panel, the event browser's clickable stream ids still reach it, and its
      "← Back to Event Store" link (`Pages/StreamDetail/Views.fs`) points at `Settings`.
- [x] Navigating away from `/settings` tears down the Events live-tail poll: ADR-0023's
      Follow-epoch bump is re-keyed from "leaving `Admin _`" to "leaving `Settings`", proven
      by a test that asserts no `getEventsAfter` traffic after the navigation — not by
      static inspection. (`event-tail-follow.spec.ts`'s "No orphan polling (c)" spec.)
- [x] Collapsing the Events section without navigating also stops the live-tail poll, via
      the **same** `EventBrowser.State.stopFollowing` call the navigation path uses — one
      idempotent function, two triggers, not two epoch-bumping code paths. (New
      `settings-admin-sections.spec.ts` spec.)
- [x] Visiting `/settings` issues exactly **one** admin query — `getProjectionStats` — and no
      other admin query for any collapsed section. (New `settings-admin-sections.spec.ts` spec.)
- [x] That one query fires on a `/settings` **visit**, not at app cold start: it is wired
      into root `State.Url_changed`'s `Settings` branch, never into `Settings.State.init`
      (whose `Cmd` root `init` batches unconditionally on every page, which is why today's
      six admin loads correctly never fire at cold start). Proven by a test asserting no
      `getProjectionStats` traffic on a cold start at a non-Settings URL. (New
      `settings-admin-sections.spec.ts` spec.)
- [x] Expanding a section for the first time issues that section's load; re-expanding an
      already-loaded section issues none. (New `settings-admin-sections.spec.ts` spec.)
- [x] The projections-dirty banner (ADR-0034) appears on a `/settings` visit whenever any
      projection's `Lag > 0` — **including while the Projections section is collapsed** —
      still appears after a committed surgery mutation, and still clears once every
      projection's `Lag` returns to 0. (`admin-surgery.spec.ts`'s "Cross-tab dirty banner"
      test, run with the Projections section left collapsed while Surgery is expanded.)
- [x] The banner's "Go to Projections" affordance is an in-page action that expands and
      scrolls to the Projections section; no navigation, no URL change. (Same test, DOM
      assertion on the Projections checkbox instead of the old URL assertion.)
- [x] `npm run build` succeeds (Fable compiles) and `npm test` is green.
- [x] All three e2e specs pass against the new location: `tests/e2e/event-tail-follow.spec.ts`,
      `tests/e2e/event-tail-follow.smoke.spec.ts` (which also navigates `/#/admin/events`),
      and `tests/e2e/admin-surgery.spec.ts` — the last with its `toHaveURL(/#\/admin\/projections$/)`
      assertion replaced by a DOM assertion on the expanded Projections section, and its
      `test.skip(!process.env.CI, ...)` gate preserved verbatim.
- [x] All six sections render on mobile; no section is hidden below a breakpoint, and
      `BottomNav.dockItems` is unchanged.
- [ ] The Settings page reads as one coherent page rather than two apps stapled together —
      the administration sections are visibly subordinate to Settings' own chrome, not
      competing with it. [human-eye] — left unchecked: this worker has no visual/browser
      tool available (Playwright's DOM snapshots were reviewed and look coherent — an
      "Administration" `h2` matching the "Integrations"/"Data Imports" headings above it,
      followed by six evenly-styled collapse cards — but that's not a substitute for an
      actual eyeball pass. Flagging for the verifier/human.

## Notes

Refinement settled all six mechanics the capture left open, against the real code.

1. **MVU shape — headless composite child** (builder's call, on the evidence below).
   `Pages/Admin/State.fs`'s `Surgery_msg` handler is the only thing that makes ADR-0034's
   banner react without a tab revisit; absorbing the six children into Settings would mean
   re-implementing that cross-child reload inside an already-72-field `Model`. Keeping the
   composite intact makes the diff mostly deletion (shell chrome) plus one field.

2. **Deep-linkability dropped** (builder's call). `/admin/projections` is bookmarkable today
   and `Admin.Views.dirtyBanner` links to it via `Route.toUrl (Admin AdminProjections)` — an
   assertion `admin-surgery.spec.ts:292` makes by URL. Both go: the banner becomes an in-page
   expand+scroll, and that spec assertion becomes a DOM assertion. The alternative
   (`Settings of section option` → `/settings/projections`) was offered and declined.

3. **Live-tail teardown re-keying (ADR-0023).** Root `State.fs` currently matches
   `| Admin _, Admin _ -> model | Admin _, _ -> stopFollowing`. The tab-to-tab arm becomes
   unnecessary; the trigger is simply "leaving `Settings`". The section-collapse trigger
   (criterion 7) calls the same exported `EventBrowser.State.stopFollowing`, which is already
   documented idempotent — so the epoch guard genuinely is driven from one place.

4. **Section-load trigger — load on first expand, no refetch on re-expand.** All six children
   already return exactly one load `Cmd` from their `init` (`Cmd.ofMsg Load` for Health /
   Projections / Images / Jobs, `Cmd.ofMsg Load_backup_stats` for Surgery, a two-message
   `Cmd.batch` for EventBrowser), so deferral is mechanical: keep the model `init` returns,
   drop its `Cmd`, re-issue it on first expand. Each section keeps whatever manual refresh
   affordance it already has for staleness.

5. **The existing collapse idiom cannot be reused as-is.** Settings' integration cards use
   DaisyUI's *uncontrolled* `collapse collapse-arrow` with a bare
   `Html.input [ prop.type' "checkbox" ]` and no MVU state. Lazy loading and
   collapse-stops-the-poll both need the open/closed state **in the model** — controlled
   `prop.isChecked` + `prop.onChange` dispatching a toggle. Reuse the classes, not the
   mechanism. (Those cards' content is also already in `Model` at page load, which is exactly
   what must not happen here.)

6. **ADR: yes.** It retracts the `/admin` tabbed-shell shape administration-p0jka established
   alongside ADR-0017 (the `IAdminApi` contract itself is untouched — only its client shell),
   amends ADR-0023's teardown trigger and adds a second one, moves ADR-0034's cross-tab
   banner and converts its navigation affordance to an in-page action, drops per-tab
   deep-linkability, and establishes a lazy-section-load convention with one named exception.

7. **Mobile** (builder's call): all six sections render. `BottomNav`'s Settings item reaches
   the whole admin console for the first time; the Events browser and Surgery forms will be
   cramped on a phone, and that is accepted over hiding them.

**Design-system touch.** `Sidebar.fs`'s bottom group is design-system-governed chrome
(styleguide § 4 Sidebar nav — design-system-t4b9k, reverted by design-system-grtw7,
viewport-pinned by design-system-vk7rd). `DesignSystem.navGroupBottom`'s `mt-auto` layout
should be eyeballed with a single item in the group. Frontend gate satisfied via
`depends_on: [design-system-001]` (done), the same dependency administration-p0jka carried.

## Outcome

Implemented largely as scoped in Notes, with one production-code deviation discovered while
fixing the e2e specs (documented in ADR-0041's Consequences): the outer Settings section
wrapper (`adminSectionCard` in `Pages/Settings/Views.fs`) deliberately does **not** reuse
`DesignSystem.velvetCard`/`.velvet-card` — the Surgery section nests three more `.velvet-card`
panels (`AdminSurgery/Views.fs`'s `sectionCard`) inside it, and stacking the same class two
levels deep made `admin-surgery.spec.ts`'s existing `panelCard` locator ambiguous (it matched
both the outer wrapper and each inner panel). The wrapper reproduces the same visual look via
the underlying design tokens (`bg-base-100 rounded-[var(--radius-card)]
shadow-[var(--shadow-card)]`) instead of the class name.

Key files: `src/Client/Router.fs` (Page/parseUrl/toUrl/navigateTo, `isAdminSection` →
`isSettingsSection`), `src/Client/Components/Sidebar.fs` (single Settings item),
`src/Client/Pages/Admin/{Types,State,Views}.fs` (headless composite, per-section render
functions, `dirtyBanner` takes a callback instead of a `Page`), `src/Client/Pages/Settings/
{Types,State,Views}.fs` (the `AdminModel` field, twelve Open/Loaded section flags, lazy-load
Cmds, `Toggle_*_section`/`Go_to_projections_section` messages, `adminSectionCard` view
wrapper), `src/Client/{Types,State,Views}.fs` (root wiring: `AdminModel` field removed,
Follow-epoch teardown re-keyed to `Settings`, `Settings.State.loadProjectionStatsCmd` fired
from `Url_changed`'s `Settings` branch), `src/Client/Pages/StreamDetail/Views.fs` (back-link).
ADR: `.agentheim/knowledge/decisions/0041-admin-console-dissolved-into-settings.md`.

Tests: `npm run build` and `npm test` (441 Expecto tests) both green. E2e (all run with
`CI=1` against a cold-started server): `event-tail-follow.spec.ts` (6/6), `event-tail-
follow.smoke.spec.ts` (1/1), `admin-surgery.spec.ts` (4/4, CI-gated, `test.skip` preserved
verbatim), `sidebar-rail-viewport-pinned.spec.ts` (3/3, a design-system BC spec fixed to drop
its now-nonexistent "Admin" link assertions — collateral of this task's own Sidebar change,
not scope creep), and a new `settings-admin-sections.spec.ts` (4/4) covering the mechanics
that had no prior test coverage: zero admin queries at cold start away from Settings, exactly
one (`getProjectionStats`) on a `/settings` visit, load-once-on-first-expand with no refetch
on re-expand, and the Events-section-collapse Follow-teardown trigger.
