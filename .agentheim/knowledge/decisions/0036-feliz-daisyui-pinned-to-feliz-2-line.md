---
id: 0036
title: Feliz.DaisyUI is pinned to the exact 5.2.0 — the last release built against the Feliz 2 line — rather than bumping the client to Feliz 3
scope: infrastructure
status: accepted
date: 2026-07-31
supersedes: []
superseded_by: []
related_tasks: [infrastructure-npyhb]
related_research: []
---

# ADR 0036: Feliz.DaisyUI is pinned to the exact 5.2.0 — the last release built against the Feliz 2 line — rather than bumping the client to Feliz 3

## Context

`src/Client/Client.fsproj` pinned `Feliz Version="2.*"` and
`Feliz.DaisyUI Version="5.*"`. The floating DaisyUI pin resolved to **5.3.0**,
which declares `Feliz >= 3.1.1`. The direct `2.*` pin wins, so NuGet emitted:

```
warning NU1605: Detected package downgrade: Feliz from 3.1.1 to 2.9.0.
  Client -> Feliz.DaisyUI 5.3.0 -> Feliz (>= 3.1.1)
  Client -> Feliz (>= 2.0.0)
```

This was carried for some time as warning-only noise. It is not. Once
`design-system-q4ebg` cleared the sixteen `.bordered` `FS0039`s that had been
aborting the compile earlier, `dotnet build src/Client/Client.fsproj` failed on
a previously-masked error:

```
FSC : error FS0193: The module/namespace 'Feliz' from compilation unit 'Feliz'
did not contain the namespace, module or type 'HtmlHelper'
```

### The mechanism — two compilation pathways with different inputs

The decisive fact is that this project compiles the client **two different
ways, from two different sets of inputs**:

| Pathway | Consumes | Sees `HtmlHelper`? |
|---|---|---|
| `npm run build` / `dev:client` (vite-plugin-fable) | `feliz.daisyui/<v>/fable/*.fs` sources | **No** |
| `dotnet build src/Client/Client.fsproj` (MSBuild) | `feliz.daisyui/<v>/lib/netstandard2.1/Feliz.DaisyUI.dll` | **Yes** |

`HtmlHelper` appears **only in the compiled `.dll`** — `grep -rn HtmlHelper`
over `feliz.daisyui/5.3.0/fable/` returns nothing, and it is absent from
Feliz 2.9.0 entirely (sources and all). The DaisyUI 5.3.0 assembly was built
against Feliz 3.x and carries a type reference into `Feliz.HtmlHelper`; binding
it against Feliz 2.9.0 is exactly FS0193.

Fable never links that assembly — it compiles DaisyUI from source against
Feliz 2.9.0's own sources, which is why `npm run build` has always been clean
while `dotnet build` fails. **The shipping artifact was never affected.**

### What the version graph actually permits

Verified against the local NuGet cache and nuget.org:

- **`Feliz.DaisyUI` 5.2.0 depends on `Feliz 2.9.0`** (netstandard2.0);
  **5.3.0 depends on `Feliz 3.1.1`** (netstandard2.1). 5.2.0 is the last 5.x
  release built against the Feliz 2 line.
- **5.2.0's and 5.3.0's Fable sources are byte-identical.** `diff -rq` across
  `fable/` reports a difference in `Feliz.DaisyUI.fsproj` **only** — the target
  framework and the dependency bump. `DaisyUI.fs`, `Modifiers.fs`, and
  `Operators.fs` are the same files.
- `Feliz.UseElmish 2.5.0` declares **no `Feliz` dependency at all**; it is
  irrelevant to this constraint.
- `Feliz.Router 4.0.0` declares `Feliz >= 2.3.0` — a floor, not a ceiling.
- Latest published: `Feliz.DaisyUI` **5.3.0**, `Feliz` **3.3.3**.

## Decision

**Pin `Feliz.DaisyUI` to the exact version `5.2.0`** in
`src/Client/Client.fsproj`, replacing the floating `5.*`. Keep `Feliz` on the
`2.*` line.

```xml
<PackageReference Include="Feliz.DaisyUI" Version="5.2.0" />
```

This makes the declared dependency graph coherent — DaisyUI 5.2.0 asks for
Feliz 2.9.0, which is what the `2.*` pin resolves to — so `NU1605` and the
downstream `FS0193` both disappear at the root rather than being suppressed.

Because the Fable sources are byte-identical between 5.2.0 and 5.3.0, **the
shipped bundle is provably unchanged**. This is not a downgrade in delivered
behavior; it is a correction of which prebuilt assembly the MSBuild pathway is
asked to bind.

## Alternatives considered

**Bump `Feliz` to `3.*` (latest 3.3.3), keep DaisyUI floating.** Rejected for
now, not on principle — on cost and risk asymmetry. Feliz 2 → 3 is a major
version bump across ~90 client files with `prop`/`Html` surface changes as the
risk area, requires a network restore (no 3.x in the local cache), and would
put `Feliz.Router 4.0.0` — an assembly built in the Feliz 2 era — into exactly
the FS0193-class position DaisyUI 5.3.0 occupies today, just pointed the other
way. Against that, the re-pin is one line, entirely cached, and provably
behavior-neutral. The bump remains the correct move the day a DaisyUI release
carries Fable-source changes this project actually wants; it should be taken
deliberately, with its own verification pass, not as a side effect of clearing
a build warning.

**Accept the downgrade and suppress `NU1605`.** Rejected. It would leave
`dotnet build` fatally broken on FS0193, which forecloses using an MSBuild pass
as a typecheck gate (`infrastructure-p1h9a`) and leaves a contributor unable to
reason about which Feliz surface is in play from reading the fsproj.

## Consequences

- `dotnet build src/Client/Client.fsproj` becomes usable as a client typecheck
  pathway. That is the precondition `infrastructure-p1h9a`'s build gate needs;
  without this ADR its chosen mechanism cannot ship green.
- **`Feliz.DaisyUI` no longer floats.** A future 5.4.0 will not be picked up
  automatically. This is intended: the float is what silently introduced a
  Feliz-3-built assembly into a Feliz-2 project. Taking a future DaisyUI
  release is now an explicit decision, and — if that release targets Feliz 3 —
  it is the same decision as taking the Feliz 3 bump.
- The client stays on Feliz 2.9.0, the last of the 2 line. Accepted as a known,
  recorded position rather than an accidental one.
- **`dotnet build` is not a faithful proxy for the Fable pathway.** The two
  consume different inputs (prebuilt assemblies vs. Fable sources), so an
  MSBuild pass can fail on binding problems that never reach the bundle — FS0193
  being the worked example. It remains a genuine and useful F# typecheck of
  *this project's own code*, which is what `infrastructure-p1h9a` wants from it;
  it is not evidence about what Fable will emit. `p1h9a`'s open note about
  whether `vite-plugin-fable` can make FS errors fatal directly is the more
  faithful mechanism if it exists.
