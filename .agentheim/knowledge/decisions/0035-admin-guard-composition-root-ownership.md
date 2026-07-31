---
id: 0035
title: Ambient module-level single-flight guards in Administration.fs become explicitly-owned values, constructed once at the composition root
scope: administration
status: accepted
date: 2026-07-31
supersedes: []
superseded_by: []
related_tasks: [administration-jrflk]
related_research: []
---

# ADR 0035: Ambient module-level single-flight guards in Administration.fs become explicitly-owned values, constructed once at the composition root

## Context

`Administration.fs` held three name-keyed `ConcurrentDictionary<string, unit>`
single-flight guards at **module level**: `runningJobs` (ADR-0026, concurrent-
trigger refusal for scheduled jobs), `rebuildingProjections` (ADR-0024/0025,
rebuild single-flight plus the not-dirty guard's mid-rebuild half), and
`driftCheckInProgress` (ADR-0031, drift-check single-flight). In a server
process there is exactly one of everything, so "module-level" and
"per-instance" coincide and the ambient shape is invisible — every ADR above
was written and reasoned about as if the guard were scoped to "the one
recorder"/"the one server", never naming that this was actually a property of
there being only one process, not of the code.

In the test assembly the two diverge: Expecto runs test cases across the
whole assembly in parallel by default (`runTestsInAssemblyWithCLIArgs`), so
any two test files that happen to use the same job or projection name collide
on a guard neither of them knows they share. This already bit once:
`JobRunsTests.fs` and `JobConnectionConcurrencyTests.fs` both used the
literal job names `"Job C"`, `"Job D"`, `"Job E"` for unrelated specs, and
whenever both files' same-named jobs were in flight at once, one side's
`TryClaim` lost the race and `ScheduledJobs.tryStartJob` returned
`Result.Error ()`, intermittently failing `JobRunsTests.fs` with "Expected
the trigger to succeed" (reproduced ~3 times in 4 consecutive full-suite runs
during administration-mz6kp's verification). administration-mz6kp shipped a
`"JobRunsTests "` job-name prefix as an explicitly-labeled narrow mitigation,
pointing here for the real fix. The other two guards carried the same latent
defect — no test happened to reach them through the SSE handlers that hold
them, only through direct calls to the underlying functions
(`ProjectionRebuildTests.fs`/`ProjectionDriftTests.fs`) — so this task closed
all three rather than waiting for a second flake to surface independently.

## Decision

Replace ambient module-level guard state with **explicitly-owned state
constructed once at the composition root and threaded to every consumer**.
Two different mechanisms, because the two guard families already have
different natural owners:

### The job guard (`runningJobs`) moves into `makeJobRunRecorder`'s closure

`ScheduledJobs.JobRunRecorder` is already the per-instance handle for exactly
this state (ADR-0026: every recorder built from the same `conn`/`jobLock`
pair shares the same guard state). `runningJobs` is now a local
`ConcurrentDictionary` created inside `makeJobRunRecorder`'s body, closed over
by the returned record's `TryClaim`/`Release` functions. **No signature
change** — `makeJobRunRecorder` still takes exactly `conn` and `jobLock`.
This guard is deliberately **not** folded into the `AdminGuards` record below:
the recorder is the correct owner (it already exists, already threads through
`Composition.fs` to both `ScheduledJobs.startAll` and `Administration.create`
as one shared instance), and adding a separate parameter for a guard the
recorder can just close over would buy nothing.

### The projection guards (`rebuildingProjections`, `driftCheckInProgress`) become an `AdminGuards` record

```fsharp
type AdminGuards = {
    RebuildingProjections: ConcurrentDictionary<string, unit>
    DriftCheckInProgress: ConcurrentDictionary<string, unit>
}

let makeGuards () : AdminGuards = ...
```

These two have five consumers spread across module-level functions and
Giraffe handler factories (`buildProjectionStats`, `isAnyProjectionDirty`,
`driftCheckStreamHandler`, `projectionRebuildStreamHandler`, `create`), so a
closure alone can't reach all of them — each now takes an explicit
`guards: AdminGuards` parameter. `Composition.fs` builds **one**
`Administration.makeGuards ()` before wiring the admin surface and passes
that same value to `create`, `projectionRebuildStreamHandler`, and
`driftCheckStreamHandler` — so the process-wide singleton survives in
*behavior* (production still has exactly one `AdminGuards`, exactly as
before), it just stops being ambient: "one guard per process" is now a
property of `Composition.fs`'s wiring, not of `Administration.fs`'s module
scope.

### The mz6kp mitigation is retired

`JobRunsTests.fs`'s `"JobRunsTests "` job-name prefix and its collision
comment block are removed; the file uses the bare `"Job A"`..`"Job H"` names
again, `"Job C"`/`"Job D"`/`"Job E"` once more literally shared with
`JobConnectionConcurrencyTests.fs:132-134`. The suite staying green with the
names re-collided is the proof that the structural fix — not the workaround —
is carrying it.

### A new regression test proves the class is closed by construction

`AdminGuardOwnershipTests.fs` builds two independent `JobRunRecorder`s and
asserts both can `TryClaim` the *same* job name, and builds two independent
`AdminGuards` and asserts both can claim the *same* projection name — cases a
module-level singleton would have refused for the second claimant. This is
deliberately a stronger claim than "the full suite passes": a green suite
alone doesn't distinguish "the guard is genuinely per-instance" from "no two
test files' claims for the same name happened to overlap in time this run."

## Alternatives considered

- **`testSequenced` around the affected test lists.** Rejected: sacrifices
  Expecto's cross-file parallelism to hide the defect rather than fix it, and
  does nothing for a *future* test file that reintroduces a name collision.
- **Rename job/projection names by convention and document the shared
  namespace.** Rejected: leaves the underlying defect unsatisfiable by
  construction — the next new test file (or the next admin feature) can
  reintroduce the collision by simply not knowing the convention exists. The
  whole point of this task is that no future consumer should have to know
  these keys are process-global.

## Consequences

### Positive
- The acute flake class (module-level guard shared across the whole test
  assembly) is closed structurally, not papered over — verified by
  `AdminGuardOwnershipTests.fs` forcing the exact overlap a singleton would
  refuse, not merely by a clean suite run.
- Production single-flight semantics are unchanged: within one composition
  root there is still exactly one `AdminGuards` and one job-guard-holding
  recorder, so the job concurrent-trigger refusal, the rebuild
  "already-rebuilding" rejection, and the drift-check "already-running"
  rejection all still hold exactly as before.
- "One guard per process" is now a property of `Composition.fs`'s wiring —
  visible, greppable, and impossible to accidentally duplicate or omit — 
  rather than an invisible consequence of there happening to be one server
  process.
- The `"JobRunsTests "` prefix and its explanatory comment block, an
  explicitly-labeled stopgap, are gone; `JobRunsTests.fs` reads like any
  other test file again.

### Negative / accepted tradeoffs
- `Administration.create`, `projectionRebuildStreamHandler`,
  `driftCheckStreamHandler`, `buildProjectionStats`, and
  `isAnyProjectionDirty` each gained a parameter — a mechanical, compile-
  error-driven edit across `Composition.fs` and every test file building an
  `IAdminApi` (`AdministrationTests.fs`, `AdminSurgeryTests.fs`,
  `JobRunsTests.fs`, `ProjectionDriftTests.fs`).
- Every test that only cares about the lag-based half of
  `isAnyProjectionDirty` now has to pass *some* `AdminGuards` value, even
  when it's freshly constructed and never touched otherwise — a small,
  uniform tax paid at every call site.

### Neutral
- The guard *values* used in production are still `ConcurrentDictionary<string, unit>`
  with the same `TryAdd`/`TryRemove`/`ContainsKey` shape as before — this ADR
  changes ownership and wiring, not the underlying mechanism.

## References

- `src/Server/Administration.fs` — `AdminGuards`, `makeGuards`,
  `buildProjectionStats`/`isAnyProjectionDirty`/`driftCheckStreamHandler`/
  `projectionRebuildStreamHandler`/`create` all gain a `guards: AdminGuards`
  parameter; `makeJobRunRecorder` now declares `runningJobs` in its own body.
- `src/Server/Composition.fs` — `adminGuards = Administration.makeGuards ()`,
  built once and passed to `create`, `projectionRebuildStreamHandler`, and
  `driftCheckStreamHandler`.
- `tests/Server.Tests/AdminGuardOwnershipTests.fs` — the new class-closing
  regression test (two independently-built recorders / `AdminGuards` both
  claiming the same name).
- `tests/Server.Tests/JobRunsTests.fs` — mz6kp's `"JobRunsTests "` prefix and
  collision comment removed; bare `"Job A"`..`"Job H"` names restored.
- `tests/Server.Tests/AdministrationTests.fs`,
  `tests/Server.Tests/AdminSurgeryTests.fs`,
  `tests/Server.Tests/ProjectionDriftTests.fs` — updated call sites for the
  new parameters.
- ADR-0024 — projection rebuild stream/concurrency; amended here on the
  guard-ownership axis only (rebuild single-flight semantics unchanged).
- ADR-0025 — image-cache orphan detection's not-dirty guard; amended here on
  the guard-ownership axis only (what `isAnyProjectionDirty` computes is
  unchanged, only where its mid-rebuild half reads state from).
- ADR-0026 — job-runs recording's shared registry and "Run now"; amended here
  on the guard-ownership axis only (the recorder remains the single source of
  truth for the concurrent-trigger refusal; only where its guard lives
  changed).
- ADR-0031 — projection drift detector's throwaway shadow connection;
  amended here on the guard-ownership axis only (drift-check single-flight
  semantics unchanged).
- ADR-0033 — the per-request connection factory migration whose verification
  first surfaced this flake and filed it as administration-jrflk.
- administration-mz6kp — shipped the `"JobRunsTests "` prefix mitigation this
  task retires.
- administration-jrflk — the task that shipped this ADR.
