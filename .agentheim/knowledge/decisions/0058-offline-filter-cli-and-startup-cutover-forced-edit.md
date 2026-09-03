---
id: 0058
title: Offline event-log filter ships as a Server.fsproj CLI subcommand, not a standalone script; StartupCutover.fs's play-session phase is reduced to a compile-forced guard rather than left broken
scope: administration
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
amends: []
related_tasks: [administration-z6ymt]
related_research: []
---

# ADR 0058: EventLogFilter's CLI shape, and the StartupCutover.fs compile dependency

> Note on ADR numbering: authored as 0057 in a parallel worker (batch of 2026-08-04); renumbered to 0058 at integration because the sibling task games-j6wkr's ADR-0057 landed first.

## Context

`administration-z6ymt` required two implementation decisions ADR-0056 didn't settle:

1. **How the offline NDJSON filter is invoked.** The task explicitly left this to the worker
   ("fsx script, console entry point, or similar"), but it needed to be runnable by the builder on a
   laptop against an exported file, with fixture-backed tests in the ADR-0029 `EventStoreNdjsonTests.fs`
   shape (`StringReader`/`StringWriter`, plain Expecto, no HTTP).
2. **`StartupCutover.fs` unexpectedly calls the exact functions this task deletes.** The task's Notes
   say "Note: StartupCutover.fs itself is NOT in your scope — only the h4mrd play-session migration
   machinery is." But `StartupCutover.playSessionPhase` calls
   `Administration.previewPlaySessionMigration`/`runPlaySessionMigration` directly — a hard compile-time
   dependency the task's scope note didn't anticipate (`git grep PlaySessionMigration` against
   `StartupCutover.fs` alone, run during refinement, apparently missed this substring match).

## Decision

**1. The filter ships as a `dotnet run --project src/Server -- filter-demoted-events <in> <out>` CLI
subcommand**, dispatched from `Program.fs`'s `main` before `Composition.buildApp` runs, rather than a
standalone `.fsx` script or a new console project:

- The pure filter logic (`EventLogFilter.filterNdjson`, `TextReader`/`TextWriter`, no
  `SqliteConnection`) lives in `src/Server/EventLogFilter.fs`, compiled into `Server.fsproj` — the same
  project `EventStore.exportNdjson`/`importNdjson` live in, so it's directly testable by
  `Server.Tests.fsproj` the same way (`EventLogFilterTests.fs`, `EventStoreNdjsonTests.fs`'s shape).
- A standalone `.fsx` script would either duplicate the deny-list as an untested literal or need a
  `#load`/project reference that fights `dotnet fsi`'s own resolution; a new console project would add
  a fourth `.fsproj` to the solution for one CLI entry point with no independent build/test surface.
- The CLI branch is a `match args with | "filter-demoted-events" :: input :: output :: _ -> ... | _ -> ...`
  guard at the very top of `main`, before `WebApplication.CreateBuilder` ever runs — it never opens
  `DATA_DIR`, never touches a `SqliteConnection`, and exits without starting the Giraffe host. This
  keeps the offline tool genuinely offline (satisfies "workers never touch the live database" as a
  structural property, not just a convention) while reusing the existing build/test pipeline instead of
  introducing a new one.

**2. `StartupCutover.playSessionPhase` is reduced to a guard, not deleted, and not left broken.** Its
body now checks `EventStore.getSampleEventForType conn "Game_play_time_set"`: `None` (the only reachable
case — `games-v4nqe` demoted the event's only writer, so no code path can ever emit it again) returns
`Ok ()` immediately; the `Some _` arm — should be unreachable — returns a loud `Error` naming what was
retired, rather than silently doing nothing (the task's own "no route left wired whose re-run would
silently no-op" discipline, applied to this forced edit too).

This is the narrowest edit that keeps `Server.fsproj` compiling: `StartupCutover.fs`'s shape (`run`,
`seriesPhase`, `ensureSafeCatchUp`, the phase-marker machinery) is otherwise untouched, and no existing
test in `StartupCutoverTests.fs` exercised `playSessionPhase`/`run`'s play-session behavior (confirmed
by search before editing), so nothing needed adapting there. A full retirement of `StartupCutover.fs`
(ADR-0052's own "deletable after stable period" precedent, which would in fact apply — it too ran
COMPLETE in production 2026-08-03) stays out of this task's scope, as directed; that's a separate task.

## Consequences

- The runbook (`docs/runbooks/purge-demoted-metadata-events.md`) documents the exact CLI invocation.
- `StartupCutover.fs` carries one small, clearly-commented seam pointing at this ADR and at
  `administration-z6ymt`, for whoever eventually does retire the whole file.
- A future worker retiring `StartupCutover.fs` outright should delete `playSessionPhase` (and its call
  site in `run`) entirely rather than treat this guard as permanent — it exists only to keep the build
  green across this task's deletion, not because the check has any ongoing value of its own.

## Retirement note (2026-09-03)

Retired by `infrastructure-r8kqt`. Both things this ADR decided the shape of are deleted: the
`EventLogFilter.fs` module (`filterNdjson`, `purgeEligibleEventTypes`) and its `filter-demoted-events`
CLI subcommand and dispatch branch in `Program.fs`'s `main`, and `StartupCutover.fs` — including the
`playSessionPhase` guard Decision 2 introduced — are gone entirely, not just inert. `EventLogFilterTests.fs`
was deleted alongside the module. This ADR stays as the historical record of the CLI shape decided for
the one-shot purge (`administration-z6ymt`, run 2026-08-05) and of the compile dependency it uncovered
in `StartupCutover.fs`; git history is the escape hatch for the deleted code itself.
