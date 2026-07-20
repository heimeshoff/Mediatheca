---
id: 0017
title: Administration console gets its own Fable.Remoting API (IAdminApi)
scope: administration
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [administration-p0jka]
related_research: []
---

# ADR 0017: Administration console gets its own Fable.Remoting API (IAdminApi)

## Context

The event-sourcing observability tooling (event browser today; projection dashboard,
store health, scheduled-jobs view, and event surgery in follow-up tasks) needed a
structural home. `IMediathecaApi` ([[0004-fable-remoting]]) is already the single
contract for every domain BC (Movies, Series, Games, Friends, Catalogs, Dashboard,
Settings, Integration...). Folding admin plumbing into it would mean every domain
change and every admin change touch the same 250+ method interface, and admin
methods (`getEvents`, `getEventStreams`, `getEventTypes`, and future
`rebuildProjection`, `getStoreHealth`, etc.) would sit alongside unrelated domain
methods with no grouping.

Fable.Remoting supports mounting multiple independent APIs on one server, each with
its own `Remoting.withRouteBuilder`.

## Decision

Give the Administration console its own Remoting contract: `IAdminApi` in
`src/Shared/Shared.fs`, implemented server-side by `src/Server/Administration.fs`
(`Administration.create`), and proxied client-side alongside `api` in
`src/Client/App.fs`. Routed under `/api/admin/{Method}` via a dedicated
`AdminRoute.builder` (ignores the Remoting-supplied type name and emits a flat,
readable `/api/admin/...` path rather than `/api/IAdminApi/...`).

The existing event-browser methods (`getEvents`, `getEventStreams`,
`getEventTypes`) moved from `IMediathecaApi`/`Api.fs` into `IAdminApi`/
`Administration.fs` as the first tenant. Follow-up admin-console tasks
(projection dashboard, health, jobs, surgery) add their methods to `IAdminApi`,
not `IMediathecaApi`.

## Consequences

### Positive
- Admin plumbing has its own namespace on both sides of the wire — no bloating
  `IMediathecaApi`, no admin methods mixed into domain BC listings.
- Two Remoting APIs coexist on one Giraffe app via `choose` (see
  `src/Server/Program.fs`) — no new hosting infrastructure needed.
- `/api/admin/...` reads clearly as "administration surface" in server logs and
  browser devtools.

### Negative
- Two API record literals to keep in sync with two client proxies in `App.fs`
  and two branches threaded through the root MVU (`init`/`update` now take both
  `api` and `adminApi`).
- A worker adding an admin-console method must remember which interface it
  belongs to.

### Neutral
- `AdminRoute.builder` intentionally ignores its `typeName` argument (Remoting
  always calls the builder with `(typeName, methodName)`); this is a one-line
  shim, not indicative of a mismatch.

## Alternatives considered

- **Fold admin methods into `IMediathecaApi`** — rejected: five sibling tasks
  (projection dashboard, health, jobs, surgery, explorer upgrades) would keep
  adding to an already-large domain interface.
- **REST endpoints for admin routes, bypassing Remoting** — rejected: loses the
  type-safety and DU/Option transparency that motivated [[0004-fable-remoting]]
  in the first place, for no benefit (admin console has the same single-user,
  single-origin shape as the rest of the app).

## References

- `src/Shared/Shared.fs` — `IAdminApi`, `AdminRoute`.
- `src/Server/Administration.fs` — server implementation.
- `src/Server/Program.fs` — second `Remoting.buildHttpHandler`, mounted via `choose`.
- `src/Client/App.fs` — second `Remoting.buildProxy<IAdminApi>`.
- `src/Client/Pages/Admin/` — the tabbed shell that consumes it.
