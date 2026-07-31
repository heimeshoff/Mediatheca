---
id: administration-jrflk
title: Retire Administration.fs's three ambient module-level guards (runningJobs, rebuildingProjections, driftCheckInProgress) in favour of composition-root-owned per-instance state, closing the cross-file test-collision class the JobRunsTests name prefix papers over
status: todo
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: [administration-n8kqw]
tags: [testing, flaky, jobs, expecto, concurrency, projections]
related_adrs: [0024, 0025, 0026, 0028, 0031, 0033]
related_research: []
prior_art: [administration-yamm5, administration-qjcp4, administration-btvqa, administration-tj8n2, administration-mz6kp]
---

## Why

`Administration.fs` holds **three** name-keyed `ConcurrentDictionary<string, unit>`
single-flight guards at **module level**, shared by the whole process regardless
of which `IAdminApi` / recorder / handler instance is asking:

| Guard | Line | Purpose | ADR |
|---|---|---|---|
| `runningJobs` | `Administration.fs:880` | concurrent-trigger refusal for scheduled jobs | ADR-0026 |
| `rebuildingProjections` | `Administration.fs:402` | rebuild single-flight + the not-dirty guard's mid-rebuild half | ADR-0024 / ADR-0025 |
| `driftCheckInProgress` | `Administration.fs:444` | drift-check single-flight | ADR-0031 |

In a server process there is exactly one of everything, so "module-level" and
"per-instance" coincide and the ambient shape is invisible. **In the test
assembly they diverge**: Expecto runs test cases across the assembly in
parallel by default (`runTestsInAssemblyWithCLIArgs`), so any two test files
that happen to use the same key collide on a guard neither of them knows they
share.

That already bit once. `JobRunsTests.fs` and `JobConnectionConcurrencyTests.fs`
both used the literal job names `"Job C"`, `"Job D"`, `"Job E"` for unrelated
specs; whenever both files' same-named jobs were in flight at once, one side's
`TryClaim` lost the race and `ScheduledJobs.tryStartJob` returned
`Result.Error ()`, surfacing as "Expected the trigger to succeed" in whichever
`JobRunsTests.fs` test lost. Reproduced ~3 times in 4 consecutive full-suite
runs during administration-mz6kp's verification.

**The acute flake is already mitigated, and this task is the real fix.**
administration-mz6kp shipped a `"JobRunsTests "` prefix on every job name in
`JobRunsTests.fs` (see the comment block at `JobRunsTests.fs:11-20`, which
explicitly labels itself "the narrow, in-scope mitigation" and points here).
So jrflk is no longer "stop a failing suite" — it is "remove the class", and
the prefix is the thing to delete once the class is gone.

The pre-existing diagnosis stands: the guard is pure in-memory bookkeeping with
no dependency on which `SqliteConnection`/factory a test uses — the flake
reproduced identically against both the pre-mz6kp shared-`:memory:` fixture and
the post-mz6kp per-test temp-file `TestDb`. `TryClaim`/`Release`/`tryStartJob`
were untouched by mz6kp (confirmed via `git diff` — `JobConnectionConcurrencyTests.fs`
has zero changes in that task). administration-cx92m's prior-art note flagged
the same symptom as "pre-existing flaky failure ... confirmed unrelated"; this
task pins down and removes the mechanism.

The other two guards are the **same defect, currently latent**: no test reaches
them today, because `ProjectionRebuildTests.fs` / `ProjectionDriftTests.fs`
exercise `Projection.rebuildProjectionWithProgress` and `checkProjectionDrift`
directly rather than the SSE handlers that hold the guards. The builder's
explicit call (2026-07-31 refinement) is to fix all three now rather than wait
for the second flake — the whole point is that no future test file should have
to know these keys are process-global.

## What

Replace ambient module-level guard state with **explicitly-owned state
constructed once at the composition root and threaded to every consumer**. Two
different mechanisms, because the two families already have different natural
owners:

**1. Job guard → the recorder's closure.** `ScheduledJobs.JobRunRecorder` is
already the per-instance handle for exactly this state (ADR-0026: "every
recorder built from the same `conn`/`jobLock` pair shares the same guard state").
Move `runningJobs` from module level into `makeJobRunRecorder`'s body so each
recorder closes over its own dictionary. **No signature change.** Do *not* fold
this guard into the `AdminGuards` record below — the recorder is the correct
owner and adding a parameter would buy nothing.

Production is provably unaffected: `Composition.fs:322` builds exactly **one**
`jobRunRecorder` and passes that same instance to both `ScheduledJobs.startAll`
and `Administration.create`. Existing tests are unaffected too — every test in
`JobRunsTests.fs` and `JobConnectionConcurrencyTests.fs` builds one recorder and
reuses it within the test, including the concurrent-trigger-refusal spec
(`JobRunsTests.fs:212-233`), so the semantics they assert live *inside* one
recorder and survive the move intact.

**2. Projection guards → an `AdminGuards` value.** These two have five
consumers spread across module-level functions and Giraffe handler factories,
so a closure won't reach them all. Introduce a record plus a constructor:

```fsharp
type AdminGuards = {
    RebuildingProjections: ConcurrentDictionary<string, unit>
    DriftCheckInProgress:  ConcurrentDictionary<string, unit>
}

let makeGuards () : AdminGuards = ...
```

and thread it explicitly through every consumer:

| Consumer | Current site | Change |
|---|---|---|
| `buildProjectionStats` | `Administration.fs:404` (private; `IsRebuilding` read at :419) | takes `AdminGuards` |
| `isAnyProjectionDirty` | `Administration.fs:429` (mid-rebuild half at :433) | takes `AdminGuards` — 3 call sites, all in-file (:601, :1259, :1275) |
| `driftCheckStreamHandler` | `Administration.fs:584` (claim/release :604, :621) | takes `AdminGuards` |
| `projectionRebuildStreamHandler` | `Administration.fs:827` (claim/release :852, :867) | takes `AdminGuards` |
| `create` | `Administration.fs:1136` (calls the two functions above at :1250, :1259, :1275) | gains an `AdminGuards` parameter |

`Composition.fs` builds **one** `Administration.makeGuards ()` before line 326
and passes that same value to `create` (:326), `projectionRebuildStreamHandler`
(:355), and `driftCheckStreamHandler` (:357) — so the process-wide singleton
survives in behaviour, it just stops being ambient.

**3. Delete the mitigation.** Remove the `"JobRunsTests "` prefix from all job
names in `JobRunsTests.fs` (Jobs A-H, ~20 sites) and the collision comment block
at `JobRunsTests.fs:11-20`, restoring the bare `"Job C"/"Job D"/"Job E"` names
that collide with `JobConnectionConcurrencyTests.fs:132-134`. The suite staying
green with the names re-collided is the proof that the structural fix — not the
workaround — is carrying it.

The alternatives considered at capture (`testSequenced` the affected lists;
rename job names and rely on convention) are **rejected**: the first sacrifices
Expecto's parallelism to hide the defect, and the second leaves criterion 2
unsatisfiable by construction — the next new test file reintroduces the
collision.

**ADR-0035 (reserved)** records the decision: *ambient process-singleton
in-memory guards in `Administration.fs` become explicitly-owned values —
constructed once at the composition root and passed to every consumer — so
that "one guard per process" is a property of the wiring rather than of the
module.* It amends the guard-ownership half of ADR-0024, ADR-0025, ADR-0026,
and ADR-0031 without changing any of their concurrency semantics.

## Acceptance criteria

- [ ] `Administration.fs` declares **no** module-level mutable registry: no
      `let private runningJobs`, `let private rebuildingProjections`, or
      `let private driftCheckInProgress` at module scope. `runningJobs` lives
      inside `makeJobRunRecorder`'s body; the other two are fields of an
      `AdminGuards` record returned by `Administration.makeGuards ()`.
- [ ] Every projection-guard consumer receives `AdminGuards` explicitly —
      `buildProjectionStats`, `isAnyProjectionDirty`, `driftCheckStreamHandler`,
      `projectionRebuildStreamHandler`, and `create` — and `Composition.fs`
      calls `makeGuards ()` exactly **once**, passing that same value to all
      of `create`, `projectionRebuildStreamHandler`, and `driftCheckStreamHandler`.
- [ ] Production single-flight semantics are unchanged: the job
      concurrent-trigger refusal, the rebuild "already rebuilding" rejection,
      and the drift-check "already running" rejection all still hold within one
      composition root. Existing specs asserting them (notably
      `JobRunsTests.fs`'s "a second concurrent trigger of the same job
      (manual-while-scheduled) is refused") pass **unmodified except for the
      added parameter** — no assertion is relaxed or deleted.
- [ ] The mz6kp mitigation is gone: `JobRunsTests.fs` uses the bare
      `"Job A"`..`"Job H"` names (no `"JobRunsTests "` prefix), the comment
      block at `JobRunsTests.fs:11-20` is replaced by a one-line pointer to
      ADR-0035, and `"Job C"/"Job D"/"Job E"` are once again literally shared
      with `JobConnectionConcurrencyTests.fs:132-134`.
- [ ] A new test proves the class is closed by construction: two
      **independently built** recorders both successfully claim the *same* job
      name concurrently, and two **independently built** `AdminGuards` both
      successfully claim the *same* projection name — cases a module-level
      singleton would have refused. This is the criterion that survives future
      test files; the green suite alone is not.
- [ ] Full `npm test` passes (one clean run — with the guards per-instance the
      cross-file collision is impossible by construction, so a repeated-run loop
      would add cost without adding evidence).
- [ ] `ADR-0035` is written to
      `.agentheim/knowledge/decisions/0035-*.md`, stating the guard-ownership
      rule and naming ADR-0024/0025/0026/0031 as amended (semantics unchanged).
- [ ] The administration BC README's ubiquitous language records the rule —
      one sentence: single-flight guards are per-instance values owned by the
      composition root, never module-level singletons — so the next admin
      feature adding a guard follows it without rediscovering this task.

## Notes

- **Call sites that gain a parameter** (compile-error-driven, so the worker
  can't miss one): `Composition.fs:326, 355, 357`; `AdministrationTests.fs:67,
  70, 386`; `AdminSurgeryTests.fs:60`; `JobRunsTests.fs:152, 270, 289, 304`.
  The job-guard half needs **no** call-site changes anywhere.
- **`isAnyProjectionDirty` is the only public function in the set** — but grep
  confirms all three callers are inside `Administration.fs` itself
  (`:601, :1259, :1275`); `Shared.fs:1583` and `EventStore.fs:611` merely
  mention it in comments. So the signature change stays contained to one file
  plus the composition root.
- **Blast radius flagged at refinement:** `isAnyProjectionDirty` is the
  load-bearing ADR-0025 not-dirty guard gating image-cache purge and the drift
  detector. Threading a parameter through it must not change *what* it
  computes — only where the mid-rebuild half reads its state from.
- Not fixed as part of administration-mz6kp: the flake was unrelated to that
  task's connection-factory migration (root cause is pure in-memory state,
  confirmed against both the pre- and post-migration `JobRunsTests.fs` shapes),
  and reshaping shared test-fixture architecture was out of its scope. mz6kp
  shipped only the name prefix and this pointer.
- No frontend surface — the design-system styleguide gate does not apply.
- ADR-0059's convention check does not apply: the scope is product source
  (`src/Server`, `tests/Server.Tests`), not a doctrine-bearing path.
