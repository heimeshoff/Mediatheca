---
id: infrastructure-npyhb
title: "Pin Feliz.DaisyUI to the exact 5.2.0 — 5.3.0's prebuilt dll needs Feliz 3.1.1, NuGet downgrades it to the pinned 2.9.0 (NU1605), and `dotnet build` then fails FS0193 on the missing `HtmlHelper`. 5.2.0's Fable sources are byte-identical, so nothing shipped changes."
status: done
type: chore
context: infrastructure
created: 2026-07-31
completed: 2026-07-31
depends_on: []
blocks: [infrastructure-p1h9a]
tags: [nuget, packages, feliz, daisyui, build-health, tech-debt]
related_adrs: [0001, 0036]
related_research: []
prior_art: []
---

## Why

`dotnet build src/Client/Client.fsproj` reports:

```
warning NU1605: Detected package downgrade: Feliz from 3.1.1 to 2.9.0.
  Client -> Feliz.DaisyUI 5.3.0 -> Feliz (>= 3.1.1)
  Client -> Feliz (>= 2.0.0)
```

`Client.fsproj` pins `Feliz Version="2.*"` and `Feliz.DaisyUI Version="5.*"`.
The latter floats to 5.3.0, which declares `Feliz >= 3.1.1` — but the direct
`2.*` pin wins, so DaisyUI 5.3.0 binds against Feliz 2.9.0, a major version
below what it asks for.

**The original capture's premise — "Nothing is currently known to be broken by
this" — is falsified.** `design-system-q4ebg`'s bounced worker discovered that
once the sixteen `.bordered` `FS0039`s were cleared, `dotnet build` fails
outright on a previously-masked error:

```
FSC : error FS0193: The module/namespace 'Feliz' from compilation unit 'Feliz'
did not contain the namespace, module or type 'HtmlHelper'
```

Confirmed deterministic via `git stash`/`pop`: reverting the deletion reproduces
the 16-`FS0039` baseline with no `FS0193`; reapplying it reproduces `FS0193`
across repeated rebuilds. The `.bordered` errors were aborting the compile pass
before it reached the binding failure.

### The mechanism (established during this refinement, from the NuGet cache)

This project compiles the client **two ways, from two different sets of inputs**:

| Pathway | Consumes | Sees `HtmlHelper`? |
|---|---|---|
| `npm run build` (vite-plugin-fable) | `feliz.daisyui/5.3.0/fable/*.fs` | **No** |
| `dotnet build Client.fsproj` (MSBuild) | `feliz.daisyui/5.3.0/lib/netstandard2.1/Feliz.DaisyUI.dll` | **Yes** |

`grep -rn "HtmlHelper" feliz.daisyui/5.3.0/fable/` → nothing. It exists only in
the compiled assembly, which was built against Feliz 3.x. Feliz 2.9.0 has no
`HtmlHelper` anywhere. So **the failure is confined to the MSBuild pathway and
has never touched the bundle that ships** — which is precisely why q4ebg saw
`npm run build` fully clean while `dotnet build` failed.

This does not make it harmless. It forecloses using an MSBuild pass as a client
typecheck gate, which is exactly the mechanism `infrastructure-p1h9a` depends on,
and it leaves a contributor unable to tell which Feliz surface is in play from
reading the fsproj — plausibly why the `.bordered` confusion was easy to fall
into in the first place.

## What

Pin `Feliz.DaisyUI` to the exact `5.2.0` — the last 5.x release built against
the Feliz 2 line — replacing the floating `5.*`. One line in
`src/Client/Client.fsproj`:

```xml
- <PackageReference Include="Feliz.DaisyUI" Version="5.*" />
+ <PackageReference Include="Feliz.DaisyUI" Version="5.2.0" />
```

Then record the pinning rule in the infrastructure README.

**This was a spike; it is now a chore.** The investigation the spike existed to
run was completed during refinement, and the mitigation is known and cheap —
ADR-0065's stop-loss applies. The evidence, all verified against the local NuGet
cache and nuget.org:

- **`Feliz.DaisyUI` 5.2.0 → `Feliz 2.9.0`; 5.3.0 → `Feliz 3.1.1`.** 5.2.0 is
  the last release compatible with the `Feliz 2.*` pin.
- **5.2.0's and 5.3.0's Fable sources are byte-identical.** `diff -rq` across
  `fable/` reports a difference in `Feliz.DaisyUI.fsproj` only (target framework
  + dependency bump); `DaisyUI.fs`, `Modifiers.fs`, `Operators.fs` are the same
  files. **The shipped bundle is therefore provably unchanged by the re-pin.**
- This independently reconfirms `design-system-q4ebg`'s finding that `.bordered`
  is absent from `select`/`textarea` in both versions — the re-pin does **not**
  resurrect it.
- `Feliz.UseElmish 2.5.0` declares **no `Feliz` dependency at all** — a non-issue,
  contrary to the original capture's third investigation bullet.
- `Feliz.Router 4.0.0` declares `Feliz >= 2.3.0` — a floor, not a ceiling.
- Both packages are already in the local NuGet cache; no network restore needed.

The **Feliz 3.3.3 bump** was considered and deliberately deferred — a major bump
across ~90 client files, needing a network restore, and putting `Feliz.Router
4.0.0` into the same FS0193-class position DaisyUI 5.3.0 occupies today. See
ADR-0036 for the full reasoning; it should be taken as its own deliberate task,
not as a side effect of clearing a build warning.

## Acceptance criteria

- [x] `src/Client/Client.fsproj` pins `Feliz.DaisyUI` to the exact `5.2.0`
      (no floating `5.*`).
- [x] `dotnet build src/Client/Client.fsproj` emits **zero** `NU1605` lines.
- [x] `dotnet build src/Client/Client.fsproj` emits **zero** `error FS0193`
      lines. (If other pre-existing `FS` errors remain, record them and their
      count — this task owns FS0193 and NU1605 only.)
- [x] `npm run build` still exits 0 and still emits the bundle to
      `deploy/public/`, with zero `ERROR FS` lines.
- [x] The emitted bundle is unchanged in substance versus a pre-change build —
      expected by construction, since the Fable sources are byte-identical.
      Record the observed comparison (e.g. matching bundle byte sizes, or a
      `deploy/public/assets/` diff) rather than asserting it.
- [x] `npm test` still passes (416 tests green at last run).
- [x] The infrastructure README's ubiquitous language records the pinning rule:
      Feliz.DaisyUI is pinned exactly because a floating `5.*` silently pulled a
      Feliz-3-built assembly into a Feliz-2 project, and taking a future DaisyUI
      release is now an explicit decision. **Prose-only, unenforced** (ADR-0059):
      `NU1605` is a warning, so `infrastructure-p1h9a`'s errors-only build gate
      will not catch a future re-float. Noted deliberately rather than mechanized —
      the exact pin is itself the structural guard.

## Outcome

Pinned `Feliz.DaisyUI` to the exact `5.2.0` in `src/Client/Client.fsproj`,
replacing the floating `5.*`.

**Criterion 2 (zero `NU1605`) — verified directly.** Baseline `dotnet build`
(pre-change) emitted 3 `NU1605` lines (Feliz 3.1.1 → 2.9.0 downgrade). Post-change,
zero `NU1605` lines, confirmed by grep over full build output.

**Criterion 3 (zero `FS0193`) — verified via the sanctioned temporary aid, then
reverted.** The tree carries `design-system-q4ebg`'s 16 pre-existing `FS0039`
`.bordered` errors (out of scope, running in a separate worktree/BC), which abort
the compile before reaching FS0193 either way — so a plain build in this tree
never shows FS0193 regardless of the pin. To get the real signal, I temporarily
applied only the two `.bordered`-deletion hunks from
`.agentheim/salvage/design-system-q4ebg-bounced.patch` (`StreamDetail/Views.fs`,
`AdminSurgery/Views.fs`) atop both fsproj states:
- Floating `5.*` + `.bordered` fix applied → `FSC : error FS0193: ... HtmlHelper` (1 error), confirming the mechanism.
- Pinned `5.2.0` + `.bordered` fix applied → **Build succeeded, 0 errors.**
Both Views.fs files were reverted (`git checkout --`) immediately after; final
diff carries only `Client.fsproj` and this task's own files, confirmed via
`git status`/`git diff --stat`.

**Remaining pre-existing `FS` errors in the actual (un-aided) tree, post-pin:**
16 `FS0039` lines (4 distinct call sites × duplicate resolution passes) —
`StreamDetail/Views.fs:234` (`select.bordered`), `StreamDetail/Views.fs:258`
(`textarea.bordered`, ×2 sites), `AdminSurgery/Views.fs:92` and `:105`
(`textarea.bordered`). These belong to `design-system-q4ebg`, not this task.
1 pre-existing `FS0020` warning (`AdminProjections/Views.fs:199`), unrelated and
unchanged by this task.

**Criterion 5 (bundle unchanged) — measured, not asserted.** Ran `npm run build`
with the pin applied, then `git stash`/rebuild/`git stash pop` to compare against
the floating-`5.*` baseline. Emitted filenames and sizes were identical in both
runs: `assets/index-C921bzMz.js` (1,780.79 kB / 413.95 kB gzip) and
`assets/index-Dnf1E92D.css` (175.97 kB / 28.02 kB gzip) — Vite content-hashes
filenames, so identical hashes across both builds is strong evidence the emitted
bytes are identical, not just the same size.

**Criterion 6 (`npm test`)** — 416/416 tests passed, 0 failed, 0 errored.

**Criterion 7 (README)** — added a "Client package pinning rule" entry to
`.agentheim/contexts/infrastructure/README.md`'s ubiquitous language section,
recording the mechanism, the ADR-0036 pointer, and the prose-only/unenforced
caveat (ADR-0059).

No new ADR was written — ADR-0036 was already accepted and pre-loaded; this
task applied and verified it per the task's own instruction.

Key files: `src/Client/Client.fsproj`,
`.agentheim/contexts/infrastructure/README.md`.

## Notes

- **ADR-0036 is written and accepted** (`0036-feliz-daisyui-pinned-to-feliz-2-line.md`),
  covering the mechanism, the version graph, and why the Feliz 3 bump was
  deferred. The worker applies and verifies the decision; it does not need to
  re-derive or re-author it.
- Routing: globally-true — the client's base library versions are a whole-system
  tooling concern, not any single BC's.
- **Relationship to `design-system-q4ebg` (settled this session):** q4ebg's
  acceptance criterion 2 has been narrowed to the `npm run build` pathway it
  actually owns, so it is **not** blocked on this task and can land immediately
  with its already-verified salvage patch. `FS0193` travels here instead. q4ebg
  gains no `depends_on` edge to this task.
- **Relationship to `infrastructure-p1h9a` (the build gate):** p1h9a's chosen
  mechanism *is* `dotnet build src/Client/Client.fsproj`, which cannot exit 0
  until this task lands — so p1h9a now carries `depends_on: [infrastructure-npyhb]`
  in addition to its existing q4ebg edge, and this task carries the matching
  `blocks`. p1h9a's Note that its gate "lands on an already-clean tree" is true
  only after both.
- **Surfaced for p1h9a, not actioned here:** `dotnet build` and the Fable pathway
  consume *different inputs* (prebuilt assemblies vs. Fable sources), so an
  MSBuild pass is a genuine typecheck of this project's own F# but is **not**
  evidence about what Fable will emit. That strengthens p1h9a's own open note
  about whether `vite-plugin-fable` can make FS errors fatal directly. Left for
  p1h9a's own refinement rather than rewritten here.
- The four original spike bullets are all now answered: UseElmish is a non-issue,
  Router is a floor-only constraint, the Feliz 3 blast radius was judged (not
  measured) too wide for an incidental fix, and the decision is recorded.
