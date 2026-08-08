/// Domain-free smoke spec proving the Vitest-through-vite-plugin-fable pipeline works
/// end-to-end: a `.fs` file is transformed via vite-node/SSR and its Fable.Mocha tests
/// register and run under Vitest. Deliberately carries no app/domain logic — see
/// ADR-0064 and infrastructure-j7v3c's Notes for why.
module Smoke.Tests

open Fable.Mocha

let smokeTests =
    testList "Smoke — Vitest/Fable pipeline" [
        testCase "arithmetic assertion runs through the compiled pipeline" <| fun () ->
            Expect.equal (2 + 2) 4 "the pipeline can compile and execute a trivial F# assertion"
    ]

Mocha.runTests smokeTests |> ignore
