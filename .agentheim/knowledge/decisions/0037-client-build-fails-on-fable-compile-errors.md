---
id: 0037
title: "`npm run build` runs a `dotnet build` typecheck first and fails on F# compile errors — `vite-plugin-fable` alone lets a broken client ship green"
scope: infrastructure
status: accepted
date: 2026-07-31
supersedes: []
superseded_by: []
related_tasks: [infrastructure-p1h9a]
related_research: []
---

# ADR 0037: `npm run build` runs a `dotnet build` typecheck first and fails on F# compile errors

## Context

`vite-plugin-fable` (installed `0.1.1`) treats F# compile errors as **non-fatal
to bundle emission**. When a `.fs` file fails to compile, the plugin logs
`ERROR FS…` to the console and emits a throwing placeholder (`throw 1`) in
place of the unresolvable expression — but its `transform` hook returns
`state.compilableFiles` unconditionally, with no severity check, so `vite
build` completes and exits **0**. The bundle ships. The first user to render
that component gets an unhandled exception with no error boundary, which
unmounts the entire `#feliz-app` root — a blank page.

This is not hypothetical. It happened twice in nine days:

| Date | Event |
|---|---|
| 2026-07-21 | `design-system-dib4q` clears 3 recurring `FS0039`s, warning a grep-based cleanup "can mask a genuinely new compile error a future change introduces" |
| 2026-07-22 | `administration-xjmda` ships 2 new `FS0039`s — composer dead on arrival |
| 2026-07-22 | `administration-wwc36` ships 2 more — Surgery Edit panel dead on arrival, and its own `[human-eye]` UI sign-off passed anyway |
| 2026-07-31 | Discovered only because `administration-svq3t` drove the Surgery tab with Playwright and caught the `pageerror` |

Nobody scrolled past the asset-size summary at the tail of a `vite build`
run. The server side has no equivalent hole — `npm test` and `dotnet run`
both go through a real MSBuild pass that fails on errors — but the
Fable-compiled client had no such gate.

### Whether the plugin itself can be made to fail — checked and ruled out

`vite-plugin-fable`'s `PluginOptions` typedef exposes only `fsproj`, `jsx`,
`noReflection`, `exclude` — nothing severity-related. `logDiagnostics` logs
every diagnostic via `console.log`/`warn`/`error` regardless of severity and
never throws, never calls Rollup's `this.error(...)`, never sets
`process.exitCode`. The two `throw new Error(...)` sites in the plugin fire
only on a daemon RPC-transport failure — a compile that succeeds but
*contains* FS errors still returns the `Success` case
(`Fable.Daemon/Types.fs`'s `ProjectChangedResult.Success` carries
`diagnostics` as a normal, non-failing field), so FS errors flow into
`logDiagnostics` rather than the throwing path. Grepping the plugin for
`this.error`, `exitCode`, `process.exit`, `strict`, `fatal`, `failOn` returns
zero matches. The package README describes itself as pre-alpha and
unmaintained ("up for adoption"). **Conclusion: there is no plugin option to
make FS errors fatal today.**

### The available mechanism — a real MSBuild pass

`dotnet build src/Client/Client.fsproj` performs a genuine F# compile of the
same project and, unlike the Fable pathway, fails with a non-zero exit code
on any compile error:

```
$ dotnet build src/Client/Client.fsproj -v q --nologo
... error FS0039: The type 'select' does not define ... 'bordered'
    3 Warning(s)
    16 Error(s)
EXIT=1
```

This depended on the tree actually typechecking under MSBuild, which it did
not until two preconditions landed the same day this task shipped:
`design-system-q4ebg` cleared sixteen `.bordered` `FS0039`s, and
`infrastructure-npyhb` (ADR-0036) pinned `Feliz.DaisyUI` to the exact `5.2.0`,
clearing an `FS0193` binding failure the `FS0039`s had been masking. With both
landed, `dotnet build src/Client/Client.fsproj -v q --nologo` exits 0 with
exactly `1 Warning(s)  0 Error(s)` (the sole survivor, `FS0020` at
`AdminProjections/Views.fs(199,13)`, is a real but separate one-line cleanup
left unfixed here).

## Decision

Add a `typecheck` script and make `build` depend on it:

```json
"typecheck": "dotnet build src/Client/Client.fsproj",
"build": "npm run typecheck && vite build",
```

`npm run build` now cannot succeed on a client that does not compile — the
MSBuild pass runs first and its non-zero exit code short-circuits the `&&`
before `vite build` ever runs. Only F# **errors** fail the gate; warnings do
not, and `TreatWarningsAsErrors` was deliberately not reached for (the lone
surviving `FS0020` warning must keep passing).

`npm run dev:client` is left untouched — the watch loop stays fast and
tolerant; this gate protects the build, not the edit-save-reload cycle.

Proven by construction: a `textarea.bordered` (the exact `FS0039`-class error
from the table above) was temporarily reintroduced in
`src/Client/Pages/AdminSurgery/Views.fs`, `npm run build` was run, and it
exited **1** with `error FS0039` printed. After reverting, `git status`/`git
diff --stat` confirmed the tree carried no leftover changes. `npm run build`
was then re-run on the clean tree: exit **0**, one warning (`FS0020`), and the
emitted bundle's Vite content hashes (`index-Dnf1E92D.css`,
`index-UcBhDRFf.js`) were unchanged from a build taken immediately before the
deliberate-error round-trip.

## Alternatives considered

**Grep the `vite build` log for `ERROR FS` and fail the script on a match.**
Rejected. Works, but reimplements in shell what MSBuild already reports via
exit code, and is brittle against log format changes and ANSI escape
sequences wrapping the real log lines.

**A `vite-plugin-fable` option to make FS errors fatal.** Would be the more
faithful mechanism — it would check the exact pathway that ships — but does
not exist in the installed `0.1.1` (see above) and the package is
unmaintained. If a future version of the plugin gains such an option, it
should supersede this ADR's mechanism.

**Add CI and gate there instead.** Rejected as out of scope. This project has
no CI pipeline; the gate belongs in the build script that already exists, and
remains useful the day CI does arrive.

## Consequences

- `npm run build` fails fast on any F# compile error in the client, closing
  the gap that let two features ship dead-on-arrival behind a green build.
- **Inherited blind spot (ADR-0036):** `dotnet build` and Fable consume
  *different inputs* — MSBuild binds prebuilt `lib/*.dll` assemblies, Fable
  compiles `fable/*.fs` sources. The gate is a genuine typecheck of this
  project's own F#, which is what it is asked to catch, but it is **not**
  proof of what Fable will emit. It can fail on assembly-binding problems
  that never reach the bundle (`FS0193` was the worked example, now closed by
  ADR-0036's exact pin), and in principle a Fable-source-only problem could go
  uncaught. Recorded in the infrastructure BC README alongside the gate
  itself.
- Adds ~27s (measured, warm) to `npm run build`. `dev:client` is unaffected.
- If `vite-plugin-fable` is ever replaced or gains a native fatal-error
  option, this gate's `dotnet build` step becomes a redundant (but still
  correct) safety net rather than the sole mechanism, and could be
  reconsidered then.
