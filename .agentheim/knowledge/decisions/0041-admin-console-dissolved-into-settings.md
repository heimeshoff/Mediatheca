---
id: 0041
title: The /admin console dissolves into Settings — six lazy inline sections, one Follow-teardown trigger becomes two, per-tab deep-linking dropped
scope: administration
status: accepted
date: 2026-08-01
supersedes: []
superseded_by: []
amends: [0017, 0023, 0034]
related_tasks: [administration-k3vmt]
related_research: []
---

# ADR 0041: The /admin console dissolves into Settings — six lazy inline sections, one Follow-teardown trigger becomes two, per-tab deep-linking dropped

## Context

The sidebar's bottom nav group carried two buttons, **Admin** and
**Settings**, for what the builder experiences as one destination: both are
the Administration BC's operator surface (Settings held Integrations + Data
Imports; Admin held Events / Projections / Health / Images / Jobs /
Surgery). The builder rejected the split outright — *"I don't want to have
two different buttons in the menu on the bottom left for these two different
sections."* `Components/BottomNav.fs`'s mobile dock never carried an Admin
item at all, so the entire admin console was unreachable on mobile before
this change — a side effect this ADR also fixes, not a goal in itself.

administration-p0jka (alongside ADR-0017) established the `/admin` tabbed
shell this ADR retracts. Three later tasks built real functionality on top
of that shell that this ADR has to carry forward without regressing:
ADR-0023's Follow-epoch teardown on navigating away from the Admin page,
ADR-0034's cross-tab "projections out of sync" dirty banner (client-derived
from `getProjectionStats`'s `Lag` field, reloaded immediately after a
committed surgery mutation), and administration-v4y9g's stream drill-in
(`/admin/streams/<streamId>`, a parameterized top-level `Page` case, not an
`AdminTab` variant).

## Decision

### Fully inline, one page — not a lighter alternative

Two lighter alternatives were on the table at capture: a Settings section of
links into an unchanged `/admin/*`, or re-parenting the routes to
`/settings/admin/*`. The builder declined both — admin stops being its own
page. The six former tabs (Events, Projections, Health, Images, Jobs,
Surgery, in that order) become collapsible sections on the Settings page,
below the existing Data Imports section.

### `Pages/Admin/` survives as a headless composite child

`Pages/Admin/Types.fs` and `State.fs` keep their shape — one `Model` field
per former tab's child model, one `Msg` wrapper per child — losing only the
now-meaningless `ActiveTab: AdminTab` field (`AdminTab` itself is retired,
see Router below). `Pages/Admin/Views.fs`'s shell chrome (the "Administration"
`h1` and the underline tab bar) is deleted; what remains is one render
function per section (`eventsSection`, `healthSection`, ...) plus the dirty
banner, both consumed by `Pages/Settings/Views.fs`. `Settings.Types.Model`
holds the whole composite in one field (`AdminModel`) with one `Admin_msg`
wrapper, rather than absorbing six more child models and six `Msg` wrappers
into an already-72-field `Model`. This is what lets `Admin.State`'s
`Surgery_msg` handler — the only thing that makes the ADR-0034 banner react
without a manual reload — carry over as a pure diff-by-deletion instead of a
reimplementation inside Settings.

### Router: `Admin of AdminTab` and the `AdminTab` DU are gone

Every former admin URL still resolves to something — `/admin`, all six
`/admin/<tab>` segments, and the legacy `/events` alias all parse to
`Settings`. **`/settings` is the only address the Settings page has** — no
section route, no fragment. `Route.isAdminSection` (which used to resolve
both `Admin _` and `Stream_detail _`) becomes `Route.isSettingsSection`,
resolving `Settings` and `Stream_detail _` — the sidebar's single Settings
item stays highlighted on a stream drill-in. `Stream_detail` itself is
unaffected beyond its "← Back to Event Store" link, which now points at
`Settings`: it was already a top-level `Page` case rather than an `AdminTab`
variant (administration-v4y9g), so it outlives the shell cleanly.

### Per-section deep-linkability is deliberately dropped

`/admin/projections` was bookmarkable before this ADR, and the dirty
banner's "Go to Projections" link navigated there by URL. Both are gone: the
banner's link is now an in-page expand+scroll (dispatches
`Go_to_projections_section`, which sets `ProjectionsSectionOpen = true` and
scrolls `#settings-admin-projections` into view after a short delay for the
DOM to reflect the state change). The alternative offered at capture,
`Settings of section option` → `/settings/projections`, was declined by the
builder.

### Lazy sections, with one named exception

A collapsed section fetches nothing — Settings is where the builder goes to
paste an API key, and that visit must not trigger a health scan, an
image-orphan scan, a job-status poll, and an event page all at once. Each
section's model is constructed once (via `Pages/Admin/State.init`, whose own
eager `Cmd` is discarded), and that section's one load message
(`Load`/`Load_backup_stats`/`Load_filter_options`+`Load_page`, exactly
mirroring what each child's own `init` already returned) is re-issued on
first expand only — re-collapsing and re-expanding fires nothing further.
This is tracked as twelve booleans on `Settings.Types.Model` (an
Open/Loaded pair per section) rather than a `Set<AdminSection>`, consistent
with the file's existing many-explicit-fields style.

The **one exception**: `getProjectionStats` fires on every `/settings`
*visit*, regardless of collapse state, wired into root `State.Url_changed`'s
`Settings` branch — not into `Settings.State.init`, whose `Cmd` root `init`
batches unconditionally on every page load, and which must stay silent at
cold start. The ADR-0034 dirty banner is client-derived from this call and
must react even if the operator never opens the Projections section — a
store left dirty by an earlier session would otherwise never surface it.

Settings' pre-existing Integrations/Data Imports cards use DaisyUI's
*uncontrolled* `collapse collapse-arrow` (`prop.type' "checkbox"`, no MVU
state). That idiom can't drive lazy loading or poll teardown, both of which
need open/closed state in the model — the six administration sections use a
*controlled* checkbox (`prop.isChecked` + `prop.onChange`) instead, reusing
the same `.collapse.collapse-arrow` classes but not the uncontrolled
mechanism.

### ADR-0023's Follow-epoch teardown gets a second trigger

Root `State.fs` used to match `Admin _, Admin _ -> model | Admin _, _ ->
stopFollowing`. The tab-to-tab arm is gone (there are no more tabs); the
remaining trigger re-keys from "leaving `Admin _`" to "leaving `Settings`".
A second trigger is added alongside it: collapsing the Events section
without navigating (`Settings.State`'s `Toggle_events_section`) calls the
exact same exported, already-idempotent `EventBrowser.State.stopFollowing` —
one function, two call sites, not two epoch-bumping code paths.

### ADR-0034's dirty banner moves and its link becomes in-page

`Pages/Admin/Views.dirtyBanner` is unchanged in its derivation (still reads
`AdminProjectionsModel.Stats`'s `Lag` field, still no new API method) but
now takes an `onGoToProjections: unit -> unit` callback instead of
navigating to a `Page`. It renders above all six sections on Settings
(previously above the tab bar, visible on every tab) — the same "visible
regardless of which section you're looking at" guarantee, just relocated.
`Admin.State`'s `Surgery_msg` handler (the cross-child reload after a
committed mutation) is untouched.

### Mobile: all six sections render

`BottomNav.dockItems` was already Settings-only and needed no change — it
now reaches the whole console for the first time. The Events browser and
Surgery forms will be cramped on a phone; the builder accepted that over
hiding them.

## Consequences

- `Sidebar.bottomNavItems` drops from two entries to one.
- `Router.Page` loses the `Admin` case and the `AdminTab` DU entirely; every
  `Page` match in the codebase that used to handle `Admin _` had that arm
  deleted (root `Views.fs`'s page-content switch, `Route.isAdminSection`
  → `isSettingsSection`).
- `Settings.State.update` gains an `IAdminApi` parameter (previously only
  `IMediathecaApi`), since `Admin_msg` routes through it.
- Three Playwright specs needed updating for the new location:
  `admin-surgery.spec.ts` (its `toHaveURL(/#\/admin\/projections$/)`
  assertion replaced by a DOM assertion — the Projections section's own
  checkbox becoming checked — since the URL no longer changes; every test in
  the file gained an `expandAdminSection` call before interacting with
  section content), `event-tail-follow.spec.ts` and
  `event-tail-follow.smoke.spec.ts` (an `expandEventsSection` call after
  navigating, since the Events section starts collapsed). A fourth,
  unrelated design-system BC spec (`sidebar-rail-viewport-pinned.spec.ts`)
  needed its "Admin" link assertions removed, since that link no longer
  exists. A new spec, `settings-admin-sections.spec.ts`, covers what's new
  here specifically: exactly one admin query on a `/settings` visit, none at
  cold start elsewhere, load-once-on-first-expand, and the Events-section-
  collapse Follow-teardown trigger.
- The `IAdminApi` contract itself (ADR-0017) is untouched — only its client
  shell dissolved. No server-side change in this ADR.

## Alternatives considered

- **Settings section of links into an unchanged `/admin/*`** — rejected by
  the builder; still two navigational homes in practice, just one fewer
  sidebar button.
- **Re-parent routes to `/settings/admin/*`** — rejected by the builder;
  keeps per-tab deep-linking but doesn't address "one page, not two apps
  stapled together."
- **`Settings of section option` → `/settings/projections` deep-linking** —
  rejected by the builder in favor of the simpler "one address, in-page
  expand+scroll" shape.
- **Gate the `Admin_msg` branch in root `update` to drop child messages
  whenever `CurrentPage` isn't `Settings`** (considered, alongside ADR-0023,
  for the teardown trigger) — too coarse; would swallow any Settings-page
  admin message that legitimately outlives a hypothetical stale dispatch,
  not just the Follow poll. The existing epoch-guard mechanism is precise
  where a coarse message-drop isn't.
