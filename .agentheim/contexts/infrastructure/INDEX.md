# infrastructure -- Index

Catalog of everything in this bounded context: tasks by status, ADRs scoped to this BC,
research touching this BC, and concept synthesis pages.

> Updated by: `model` (tasks), `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).

---

## Tasks by status

<!-- task-counts:start -->
- **Backlog:** 2
- **Todo:** 0
- **Doing:** 0
- **Done:** 1
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
- **infrastructure-w8fnp** — Photino desktop shell prototype — Kestrel in-process, native webview, self-contained Windows/Mac packaging (spike) — `done/infrastructure-w8fnp-photino-desktop-shell-prototype.md`
<!-- done-list:end -->

### Backlog
<!-- backlog-list:start -->
- **infrastructure-p1h9a** — Fail the client build on Fable compile errors — `vite build` exits 0 while emitting throwing placeholders for FS-level errors, so broken UI ships silently (twice already) (chore) — `backlog/infrastructure-p1h9a-fable-compile-error-build-gate.md`
- **infrastructure-npyhb** — NU1605 — Client.fsproj pins Feliz 2.* while Feliz.DaisyUI 5.3.0 requires >= 3.1.1, so NuGet silently downgrades 3.1.1 → 2.9.0 and the client compiles against a Feliz surface its dependency does not expect (spike) — `backlog/infrastructure-npyhb-feliz-package-downgrade.md`
<!-- backlog-list:end -->

## ADRs scoped to this BC

<!-- adr-local:start -->
<!-- no ADRs scoped to this BC -->
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
