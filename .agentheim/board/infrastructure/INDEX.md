# infrastructure -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 0
- **Doing:** 0
- **Done:** 6
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **infrastructure-r8kqt** — Retire the one-shot cutover machinery and its backups once production has been stable for two weeks — delete StartupCutover.fs plus its tests and Composition call sites, revert ensureSafeCatchUp to Projection.startAllProjections, and remove the pre-cutover backup files from the server and dev volumes (chore) — `done/infrastructure-r8kqt-retire-cutover-machinery-and-backups.md`
- **infrastructure-j7v3c** — Stand up the Vitest-through-vite-plugin-fable client unit-test harness — `vitest@^3.2.4` driven through the app's existing `vite.config.mts` Fable plugin, Fable.Mocha as the DSL, `npm run test:client`, plus the ADR recording the harness and its boundary against ADR-0027's e2e suite (chore) — `done/infrastructure-j7v3c-vitest-fable-client-test-harness.md`
- **infrastructure-e4kwm** — Record the event-worthiness doctrine — an event records an observation of the user's own engagement, a cache records a third party's description — and amend ADR-0012's retracted justification (decision) — `done/infrastructure-e4kwm-event-worthiness-doctrine.md`
- **infrastructure-p1h9a** — "Fail the client build on Fable compile errors — `vite build` exits 0 while emitting throwing placeholders for FS-level errors, so broken UI ships silently (twice already)" (chore) — `done/infrastructure-p1h9a-fable-compile-error-build-gate.md`
- **infrastructure-npyhb** — "Pin Feliz.DaisyUI to the exact 5.2.0 — 5.3.0's prebuilt dll needs Feliz 3.1.1, NuGet downgrades it to the pinned 2.9.0 (NU1605), and `dotnet build` then fails FS0193 on the missing `HtmlHelper`. 5.2.0's Fable sources are byte-identical, so nothing shipped changes." (chore) — `done/infrastructure-npyhb-feliz-package-downgrade.md`
- **infrastructure-w8fnp** — Photino desktop shell prototype — Kestrel in-process, native webview, self-contained Windows/Mac packaging (spike) — `done/infrastructure-w8fnp-photino-desktop-shell-prototype.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->


## Pointers

- Knowledge half (ADRs / research / concepts / BC README) for this BC: `../../knowledge/contexts/infrastructure/INDEX.md`
