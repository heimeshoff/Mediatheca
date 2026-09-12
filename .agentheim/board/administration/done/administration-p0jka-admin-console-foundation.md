---
id: administration-p0jka
title: Admin console foundation — IAdminApi contract, Administration.fs, /admin section with tabs
status: done
type: feature
context: administration
created: 2026-07-20
completed: 2026-07-20
depends_on: [design-system-001]
blocks: []
tags: [admin-console, event-store, api]
related_adrs: [0002, 0004, 0017]
related_research: []
prior_art: []
---

## Why
The event-sourcing observability tooling is outgrowing the single `/events` page. A proper Administration console needs a structural home on both sides of the wire: a dedicated Remoting contract so admin plumbing doesn't bloat `IMediathecaApi`, a server module for admin queries/commands, and a tabbed client section that the follow-up tasks (explorer upgrades, projection dashboard, health, jobs, surgery) can each fill in.

## What
- New `IAdminApi` Remoting contract in `src/Shared/` (Fable.Remoting supports multiple APIs; route via a distinct route builder, e.g. `/api/admin/{Method}`).
- New `src/Server/Administration.fs` implementing it. Move the existing event-browser methods (`getEvents`, `getEventStreams`, `getEventTypes`) from `IMediathecaApi` into `IAdminApi`.
- Client: promote `/events` into an `/admin` section — `Router.Page` gains `Admin` sub-routes; a tabbed shell page (`Pages/Admin/`) with tabs: Events, Projections, Health, Jobs, Surgery. The existing EventBrowser page becomes the Events tab content; the other tabs render placeholder panels until their tasks land.
- `/events` URL redirects (or re-parses) to `/admin/events` so old bookmarks keep working.

## Acceptance criteria
- [x] `IAdminApi` exists in Shared, served alongside `IMediathecaApi`, and the client proxies both.
- [x] Event browser functionality is unchanged for the user but now served through `IAdminApi`.
- [x] `/admin` renders a tabbed shell (Events, Projections, Health, Jobs, Surgery) styled per the design system; `/admin/events` shows the current event browser.
- [x] `/events` still resolves (redirect or alias) to the Events tab.
- [x] `npm run build` and `npm test` pass.

## Notes
Foundation task for the Administration console — administration-g5dfy, -v4y9g, -mtf1f, -qjcp4, -hw74a and the backlog surgery/ops tasks all depend on it. Tab-level routing should be URL-addressable (`/admin/projections` etc.) so later tasks slot in without reworking the shell. Frontend gate design-system-001 is done.

Decision record: [[0017-second-remoting-api-for-admin-console]] — why the admin console got its own Remoting contract instead of extending `IMediathecaApi`.

## Outcome
Added `IAdminApi` (Shared.fs) + `AdminRoute.builder` (`/api/admin/{Method}`), implemented server-side by `src/Server/Administration.fs` (`Administration.create`), mounted as a second Remoting handler alongside the existing one in `Program.fs`. Moved `getEvents`/`getEventStreams`/`getEventTypes` off `IMediathecaApi`/`Api.fs` onto the new contract — event browser behavior is unchanged, just re-plumbed.

Client: `Router.fs` gained `AdminTab` (AdminEvents/Projections/Health/Jobs/Surgery) and `Page.Admin of AdminTab`, URL-addressable at `/admin/{tab}`; `/events` parses as a legacy alias to `Admin AdminEvents`. New `Pages/Admin/{Types,State,Views}.fs` is the tab shell: an underline-tab bar (`DesignSystem.underlineTabClass`, same pattern as the Dashboard tab bar) with anchor-based navigation so tabs are directly linkable; the Events tab renders the existing `EventBrowser` page (unmodified apart from its `update` signature now taking `IAdminApi`); Projections/Health/Jobs/Surgery render placeholder panels for the sibling tasks to fill in. Sidebar's "Events" nav item became "Admin", active on any `/admin/*` route. Root MVU (`Types.fs`/`State.fs`/`Views.fs`/`App.fs`) now threads both `api: IMediathecaApi` and `adminApi: IAdminApi`.

Added `tests/Server.Tests/AdministrationTests.fs` (3 tests, TDD red→green against `Administration.create`) exercising `getEvents`/`getEventStreams`/`getEventTypes` through the new contract against an in-memory SQLite event store. Full suite: 291/291 passing. `npm run build` passes (Fable compiles cleanly). Manual smoke test: started the server against a scratch `DATA_DIR`, confirmed `/api/admin/getEventStreams` and `/api/admin/getEventTypes` respond over the new route.

Key files: `src/Shared/Shared.fs`, `src/Server/Administration.fs`, `src/Server/Api.fs`, `src/Server/Program.fs`, `src/Server/Server.fsproj`, `src/Client/Router.fs`, `src/Client/Pages/Admin/{Types,State,Views}.fs`, `src/Client/Pages/EventBrowser/State.fs`, `src/Client/Components/Sidebar.fs`, `src/Client/Types.fs`, `src/Client/State.fs`, `src/Client/Views.fs`, `src/Client/App.fs`, `src/Client/Client.fsproj`, `tests/Server.Tests/AdministrationTests.fs`, `tests/Server.Tests/Server.Tests.fsproj`.
