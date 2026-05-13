---
id: 0007
title: Single-user app, no authentication
scope: global
status: accepted
date: 2026-05-12
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0007: Single-user app, no authentication

> Backfill — the deployment posture and the security model.

## Context

Mediatheca is a self-hosted personal tool. One human uses it. It runs in a Docker container on a Linux server controlled by the same person. There is no multi-tenancy, no sharing, no accounts. "Friends" in the domain are *referenced* people who share watch sessions; they are not users of the system.

Adding authentication for one user would be ceremony: a login screen the owner clicks past, a password to manage, a session token to debug.

## Decision

- **No authentication layer inside the application.** The app trusts every request.
- **Access control is delegated to the deployment perimeter.** The expected posture is: deploy behind a private network, a VPN, or a reverse proxy that handles auth (e.g. Authelia, Tailscale, Cloudflare Access).
- **No multi-user code paths.** No "owner id" on aggregates, no per-user projections. The single user is implicit.
- **The Docker container does not bind to public interfaces by default.** Operational hardening lives in the deployment recipe, not the app.

## Consequences

### Positive
- Massive simplification: no login UI, no token management, no per-user partitioning of events, no permission checks.
- Every event in the store belongs to "the user" — no ambiguity about ownership.
- Deployment story is simpler too: one process, one DB file, no auth provider to wire up.

### Negative
- The app **must not be exposed to the public internet without a perimeter auth layer**. Doing so leaks the entire library. This requirement lives in operator's head, not in code.
- If the project ever needs to become multi-user (vision lists this as out-of-scope for v1 *and* v2), it's a significant rewrite: aggregate ids would need owner scoping, projections would need user partitioning, the API would need authentication and authorization.
- No audit log of *who* did what — only *what* happened. Fine for one user, would be insufficient for any team scenario.

### Neutral
- The "single-user" assumption is now a load-bearing design constraint and shows up in many places (Slug uniqueness, dashboard intent, etc.).

## Alternatives considered

- **Basic auth in the app** — adds a password to manage for one user. Net negative.
- **OAuth / OIDC integration** — overkill; the user would still be a single human authenticating to themselves.
- **Multi-user from the start** — would have permanently raised the complexity ceiling for zero current benefit. Easier to add later if ever needed than to remove.

## References

- `CLAUDE.md` § "Conventions": "Single-user app — no authentication".
- Vision §"Target User": "Single user (self-hosted)".
