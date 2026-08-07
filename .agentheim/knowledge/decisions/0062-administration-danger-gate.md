---
id: 0062
title: Administration sections hidden behind a type-the-word danger gate on Settings
scope: administration
status: accepted
date: 2026-08-07
supersedes: []
superseded_by: []
related_tasks: []
related_research: []
---

# ADR 0062: Administration sections hidden behind a type-the-word danger gate on Settings

## Context

ADR-0034 (event surgery) and ADR-0025 (image purge) each guard their own
destructive action with a preview plus an explicit typed confirm, and
ADR-0049 blocks rebuild outright for projections with out-of-band writers.
Those guardrails are all *intra-action*: they fire once the operator has
already clicked the button.

administration-k3vmt (ADR-0041) dissolved the standalone `/admin` console
into six inline collapsible sections at the bottom of the Settings page. That was
right for navigation — one page, no dead deep links — but it put "Rebuild
all", "Purge orphans" and the raw event-log surgery panels on the same
page an operator opens to paste a TMDB key or kick off a Steam import. The
sections start collapsed, so nothing destructive is one click away; but a
section header is, and inside it the destructive buttons are. In a
single-user app there is no second operator to catch a misclick, and
several of these actions are recovery machinery whose whole purpose is to
be used rarely and deliberately.

## Decision

**Nothing in the Administration block renders until the operator types the
word `danger`.**

- `Pages/Settings/Views.fs`'s `adminUnlockGate` replaces the six
  `adminSectionCard`s while `Model.AdminUnlocked` is false. The six cards
  live behind `adminSections`, which is not called at all in that state —
  the sections are **absent from the DOM**, not merely visually hidden, so
  neither a stray click nor a stray script can reach them.
- The match is `value.Trim().ToLowerInvariant() = "danger"` — deliberateness
  is the point, not a spelling test. The word is not a secret: it is the
  input's own placeholder, and the gate involves no auth, no persistence and
  no server round-trip. The threat model is the operator's own hand.
- Unlocking is one-way for the visit: once matched, the box is replaced by
  the sections plus a **Lock** control in the section header. `Lock`
  (`Lock_admin_sections`) re-hides them, collapses all six back to closed,
  and stops any live Events tail via the same idempotent
  `Admin.State.stopFollowing` the collapse and page-departure paths use
  (ADR-0023).
- The unlock is **model state only**, and `Settings.State.init` runs on every
  `/settings` visit (root `State.Url_changed`'s Settings branch), so leaving
  the page and returning re-locks. Nothing is written to storage; there is
  no "stay unlocked" affordance to forget about.

**The ADR-0034 dirty banner stays visible above the gate.** "A projection is
stale" is read-only information the operator needs regardless; hiding it
would cost awareness and buy no safety. Its "Go to Projections" affordance,
however, must not become a hole in the gate: `Go_to_projections_section`
branches on `AdminUnlocked` and, while locked, scrolls to and focuses the
unlock box instead of expanding a section that isn't rendered. The operator
still learns what's wrong and where to go, and still has to type the word.

The gate is per-page-region, not per-action: it does **not** replace any of
the existing per-action confirms, which continue to fire unchanged once the
sections are open.

## Alternatives considered

**A confirm dialog on first expand of any section.** Rejected: a modal
answered by clicking is exactly the reflex a misclick already has. Typing a
word requires a different motor action than the click that got you there.

**A settings toggle ("show advanced/administration tools") persisted in the
DB.** Rejected: persistence defeats the purpose. The value of re-locking on
every visit is that the dangerous surface is never the default state of a
page opened for routine reasons, and a persisted toggle would be flipped on
once and stay on forever.

**Moving the sections back to a separate `/admin` route.** Rejected as a
regression of ADR-0041 — it re-introduces the dead deep links
and the duplicate page chrome that dissolution removed, and a bookmark or a
stale link lands you straight on the dangerous page anyway.

**Real authentication.** Out of scope and contrary to the app's shape:
Mediatheca is explicitly single-user with no auth (see CLAUDE.md). There is
no second principal to authenticate against.

## Consequences

- An operator who wants the administration tools types six letters per
  Settings visit. Accepted cost; these are rarely-used recovery tools.
- E2E specs that drive any administration section must pass the gate first.
  `tests/e2e/admin-gate.ts`'s `unlockAdminSections` is the shared helper,
  called from each spec's own expand helper (`settings-admin-sections`,
  `admin-surgery`, `event-tail-follow`, `event-tail-follow.smoke`) — it must
  run after every `page.goto`, since the gate re-locks per visit.
- The eager `getProjectionStats` load (ADR-0041's one deliberate
  exception, feeding the dirty banner) is unaffected: it fires from root
  `Url_changed` regardless of gate or collapse state, which is what keeps
  the banner truthful while locked.
- The gate is client-side only. The `/api/admin/*` endpoints are as reachable
  as they ever were — this ADR buys protection against a misclick, not
  against a deliberate request. Any future need for the latter is a
  different decision (server-side authorization), not an extension of this
  one.

## References

- ADR-0034 — event surgery guardrails (the per-action confirms this gate
  sits in front of)
- ADR-0025 — typed purge outcomes / image orphan purge
- ADR-0023 — Follow epoch and `stopFollowing` teardown triggers
- ADR-0041 / administration-k3vmt — the dissolution of `/admin` into the
  Settings sections this gate now fronts
- `src/Client/Pages/Settings/{Types,State,Views}.fs`, `tests/e2e/admin-gate.ts`
