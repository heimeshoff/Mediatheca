# infrastructure -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 0
- **Todo:** 1
- **Doing:** 0
- **Done:** 2
<!-- task-counts:end -->

### Todo
<!-- todo-list:start -->
- **infrastructure-p1h9a** — "Fail the client build on Fable compile errors — `vite build` exits 0 while emitting throwing placeholders for FS-level errors, so broken UI ships silently (twice already)" (chore) — `todo/infrastructure-p1h9a-fable-compile-error-build-gate.md`
<!-- todo-list:end -->

### Doing
<!-- doing-list:start -->
<!-- no tasks in doing -->
<!-- doing-list:end -->

### Done (most recent first; older entries kept for prior-art search)
<!-- done-list:start -->
- **infrastructure-npyhb** — "Pin Feliz.DaisyUI to the exact 5.2.0 — 5.3.0's prebuilt dll needs Feliz 3.1.1, NuGet downgrades it to the pinned 2.9.0 (NU1605), and `dotnet build` then fails FS0193 on the missing `HtmlHelper`. 5.2.0's Fable sources are byte-identical, so nothing shipped changes." (chore) — `done/infrastructure-npyhb-feliz-package-downgrade.md`
- **infrastructure-w8fnp** — Photino desktop shell prototype — Kestrel in-process, native webview, self-contained Windows/Mac packaging (spike) — `done/infrastructure-w8fnp-photino-desktop-shell-prototype.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0036** -- Feliz.DaisyUI is pinned to the exact 5.2.0 — the last release built against the Feliz 2 line — rather than bumping the client to Feliz 3 -- 2026-07-31 -- `knowledge/decisions/0036-feliz-daisyui-pinned-to-feliz-2-line.md`
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
<!-- no research touching this BC -->
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- no concept pages yet -->
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
