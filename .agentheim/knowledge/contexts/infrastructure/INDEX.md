## ADRs scoped to this BC

<!-- adr-local:start -->
- **0037** -- `npm run build` runs a `dotnet build` typecheck of `Client.fsproj` first and fails on F# compile errors, since `vite-plugin-fable` alone emits throwing placeholders and still exits 0. Known blind spot: MSBuild and Fable consume different inputs, so this is not proof of what Fable emits. -- 2026-07-31 -- `knowledge/decisions/0037-client-build-fails-on-fable-compile-errors.md`
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
- Task board (tasks by status) for this BC: `../../../board/infrastructure/INDEX.md`
