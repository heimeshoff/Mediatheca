---
id: administration-jrflk
title: Fix cross-file job-name collision flake between JobRunsTests.fs and JobConnectionConcurrencyTests.fs
status: backlog
type: bug
context: administration
created: 2026-07-22
completed:
depends_on: []
blocks: []
tags: [testing, flaky, jobs, expecto]
related_adrs: [0026, 0028]
related_research: []
prior_art: []
---

## Why
Discovered while executing administration-mz6kp (per-request `SqliteConnection`
factory migration). `Administration.fs`'s `runningJobs` claim guard
(`TryClaim`/`Release`, backing `ScheduledJobs.tryStartJob`) is a single
**module-level** `ConcurrentDictionary<string, unit>`, shared by the whole
test process regardless of which `IAdminApi`/recorder instance is asking.
`JobRunsTests.fs` and `JobConnectionConcurrencyTests.fs` both happen to use
the literal job names `"Job C"`, `"Job D"`, `"Job E"` for unrelated specs.
Expecto runs test cases across the assembly in parallel by default
(`runTestsInAssemblyWithCLIArgs`), so whenever both files' same-named jobs
are in flight at once, one side's `TryClaim` loses the race and its
`ScheduledJobs.tryStartJob` call returns `Result.Error ()` — surfacing as
"Expected the trigger to succeed" in whichever `JobRunsTests.fs` test lost.

This is pre-existing: `TryClaim`/`Release`/`tryStartJob` were untouched by
administration-mz6kp (confirmed via `git diff` — `JobConnectionConcurrencyTests.fs`
has zero changes in that task), and the guard is pure in-memory bookkeeping
with no dependency on which `SqliteConnection`/factory a test uses — the
flake reproduces identically whether `JobRunsTests.fs` builds its fixture
from a shared `:memory:` connection (the pre-mz6kp shape) or a per-test
temp-file `TestDb` (the post-mz6kp shape). Reproduced ~3 times in 4
consecutive full-suite runs during administration-mz6kp's verification.
administration-cx92m's own prior-art note already flagged a similar
"pre-existing flaky failure in JobConnectionConcurrencyTests... under full-suite
load, confirmed unrelated" — this task pins down the actual mechanism.

## What
Either:
- Make the job names used across `JobRunsTests.fs`/`JobConnectionConcurrencyTests.fs`
  file-locally unique (cheapest fix, but fragile — the next new test file
  could reintroduce the same collision), or
- Make `runningJobs` (and any other module-level test-visible mutable
  registries in `Administration.fs`) injectable/resettable per test fixture
  instead of a bare module-level singleton, so two unrelated test files can
  never collide on it regardless of naming, or
- Mark the affected test lists `testSequenced` so they never race, at the
  cost of losing whatever wall-clock benefit Expecto's default parallelism
  gives the suite.

## Acceptance criteria
- [ ] Running the full suite repeatedly (at least 10 consecutive runs) shows
      zero occurrences of "Expected the trigger to succeed" /
      `ScheduledJobs.tryStartJob` returning `Result.Error ()` for a job that
      no test intentionally left in flight.
- [ ] The fix does not require every future test file to remember to pick
      globally-unique job names by convention alone (if the chosen fix is
      renaming, add a short comment/registry making the constraint
      discoverable).

## Notes
Not fixed as part of administration-mz6kp itself: the flake is unrelated to
that task's connection-factory migration (root cause is pure in-memory
state, confirmed by reproducing it against both the pre- and post-migration
`JobRunsTests.fs` shapes), and fixing shared test-fixture architecture was
out of that task's scope.
