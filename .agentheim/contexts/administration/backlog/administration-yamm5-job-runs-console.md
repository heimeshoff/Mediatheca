---
id: administration-yamm5
title: Job runs console — history, outcomes, and run-now for scheduled jobs
status: backlog
type: feature
context: administration
created: 2026-07-20
completed:
depends_on: [administration-p0jka, design-system-001]
blocks: []
tags: [admin-console, jobs, observability]
related_adrs: []
related_research: []
prior_art: []
---

## Why
The two scheduled jobs (Steam playtime sync, TMDB series refresh — Program.fs / ScheduledJobs.fs) report only to stderr; their history evaporates. "Did last night's sync run? What did it do?" is unanswerable from the app.

## What
- A `job_runs` table: job name, started/finished timestamps, outcome (ok/error/skipped), summary text (e.g. the counts PlaytimeTracker already formats). `ScheduledJobs.startAll` (or the job bodies) write a row per run.
- **Jobs tab** (`/admin/jobs`): per job — last run outcome + summary, recent-run history, next scheduled fire time (derived from the configured hour), and a "Run now" button that triggers the job immediately (guarded against concurrent runs of the same job).
- Manual runs are recorded like scheduled ones, marked with their trigger source.

## Acceptance criteria
- [ ] Scheduled and manual runs both produce `job_runs` rows with outcome and summary.
- [ ] Jobs tab shows history and next-fire time for both jobs.
- [ ] "Run now" executes the job and the new run appears without page reload (poll or refetch on completion).
- [ ] A job already running refuses a second concurrent trigger.

## Notes
Instrumentation lives in Integration's job code (`ScheduledJobs.fs`, `PlaytimeTracker`, `SeriesRefresh`) while the surface is Administration's — cross-BC touchpoint, consistent with the context map ("Administration owns the operational surface"). Refine whether the recording wrapper belongs in `ScheduledJobs.startAll` (one choke point, preferred) or in each job body.
