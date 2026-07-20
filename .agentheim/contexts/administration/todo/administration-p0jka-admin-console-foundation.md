---
id: administration-p0jka
title: Admin console foundation — IAdminApi contract, Administration.fs, /admin section with tabs
status: todo
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [design-system-001]
blocks: []
tags: [admin-console, event-store, api]
related_adrs: [0002, 0004]
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
- [ ] `IAdminApi` exists in Shared, served alongside `IMediathecaApi`, and the client proxies both.
- [ ] Event browser functionality is unchanged for the user but now served through `IAdminApi`.
- [ ] `/admin` renders a tabbed shell (Events, Projections, Health, Jobs, Surgery) styled per the design system; `/admin/events` shows the current event browser.
- [ ] `/events` still resolves (redirect or alias) to the Events tab.
- [ ] `npm run build` and `npm test` pass.

## Notes
Foundation task for the Administration console — administration-g5dfy, -v4y9g, -mtf1f, -qjcp4, -hw74a and the backlog surgery/ops tasks all depend on it. Tab-level routing should be URL-addressable (`/admin/projections` etc.) so later tasks slot in without reworking the shell. Frontend gate design-system-001 is done.
