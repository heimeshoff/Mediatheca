---
id: 0056
title: Live-data migrations ship as guarded boot routines only when the operation has a crash-unsafe window and replay-recoverable worst case; event-log-mutating, already-atomic operations stay operator-executed through existing guarded UI
scope: global
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
amends: []
related_tasks: [administration-z6ymt]
related_research: []
---

# ADR 0056: When a live-data migration runs as a guarded boot routine vs. operator-executed tooling

## Context

The project now has two shipped precedents for getting a one-time migration onto the deployed
production instance without anyone touching the live database out-of-band (the 2026-08-02 incident
rule: workers never touch the live DB; live actions are builder/conductor-only):

1. **Automated guarded boot routine** — `StartupCutover.fs` (ADR-0052): marker-gated, runs once at
   startup, aborts-and-retries-next-boot on failure, goes inert after firing. Ran COMPLETE in
   production 2026-08-03.
2. **Operator-triggered flow through existing guarded UI** — the games-h4mrd play-session migration
   (ADR-0050 addendum): preview + explicit confirm, `VACUUM INTO` backup, single-flight guard,
   executed by the builder from the Settings administration surface.

`administration-z6ymt` (purging ~8,000 demoted metadata events via ADR-0038's wipe-first import)
forced the question of which shape a destructive event-log purge takes. ADR-0052 opened this policy
question but did not bound it.

## Decision

**Automate at boot** when the operation is a multi-step sequence whose intermediate window is not
crash-safe on its own, and whose worst case is recoverable by replay or re-run — ADR-0052's cutover
is the exemplar. The boot routine's abort-and-retry-next-boot posture is exactly right there: a
half-applied state heals on the next start, and no human needs to be present at the moment the
deploy lands.

**Keep it operator-executed** when the operation is single-step, already atomic in one transaction,
and **mutates the event log itself** — `administration-z6ymt`'s purge is the exemplar. Two reasons:

- **The failure mode automation guards against isn't the dangerous one.** The wipe-import's
  one-transaction design already recovers from a *malformed* input (rollback to byte-identical). The
  dangerous failure is a *semantically wrong filter* — every line well-formed, the wrong rows gone.
  Only a human comparing the confirm dialog's discard-side count and the incoming-side line count
  against pre-computed expectations catches that. A boot routine has no one looking.
- **Retry-next-boot is incompatible with backup-restore as the recovery path.** If the builder
  restores the `VACUUM INTO` backup after a bad purge and restarts, an un-fired completion marker
  means the routine fires again and re-destroys the restored store — forcing exactly the out-of-band
  live-DB intervention the incident rule exists to prevent.

## Consequences

- `administration-z6ymt` ships as worker-built offline tooling + a runbook; the builder executes
  export → filter → wipe-import by hand through the Settings UI (Projections tab, Backup section).
- Future one-time migrations pick their shape by this criterion, not by copying whichever precedent
  shipped last. The operator-triggered-SSE shape (games-h4mrd) is a middle point: use it when the
  operation needs server-side data access but still warrants a human at the confirm step.
- Nothing in ADR-0038's mechanism changes; this ADR governs *who initiates* destructive migrations,
  not how they execute.
