/// Coverage for `Motion.Flip.plan` (the pure arithmetic core) and
/// `Motion.flipKey` (a pure property-list builder) — the only two members of
/// `Motion.fs` that don't touch the DOM/WAAPI/matchMedia, per ADR-0073 §9's
/// pure-core/untested-shell split. `Flip.snapshot`, `Flip.play`,
/// `growSurface`, `prefersReducedMotion` and `cancel` are deliberately
/// untested here — see `Motion.fs`'s module doc comment.
module Mediatheca.Client.MotionTests

open Fable.Mocha
open Mediatheca.Client.Motion

let private box (left: float) (top: float) (width: float) (height: float) : Box =
    { Left = left; Top = top; Width = width; Height = height }

let motionTests =
    testList "Motion.Flip.plan" [
        testCase "equal-size boxes that moved yield a pure translate" <| fun () ->
            let before = Map.ofList [ "a", box 100.0 200.0 50.0 50.0 ]
            let after = Map.ofList [ "a", box 10.0 20.0 50.0 50.0 ]
            let moves = Flip.plan before after
            Expect.equal moves [ { Key = "a"; Dx = 90.0; Dy = 180.0 } ] "translate is before - after"

        testCase "a pure resize with no position delta produces no move" <| fun () ->
            let before = Map.ofList [ "a", box 0.0 0.0 80.0 110.0 ]
            let after = Map.ofList [ "a", box 0.0 0.0 200.0 60.0 ]
            let moves = Flip.plan before after
            Expect.isEmpty moves "a size-only change with no position change has nothing to translate, so it is dropped like any other sub-threshold move"

        testCase "a key present only in the before map produces no move" <| fun () ->
            let before = Map.ofList [ "a", box 0.0 0.0 10.0 10.0; "gone", box 5.0 5.0 10.0 10.0 ]
            let after = Map.ofList [ "a", box 0.0 0.0 10.0 10.0 ]
            let moves = Flip.plan before after
            Expect.isEmpty moves "before-only keys are ignored; 'a' didn't move so it also produces no move"

        testCase "a key present only in the after map produces no move" <| fun () ->
            let before = Map.ofList [ "a", box 0.0 0.0 10.0 10.0 ]
            let after = Map.ofList [ "a", box 0.0 0.0 10.0 10.0; "new", box 5.0 5.0 10.0 10.0 ]
            let moves = Flip.plan before after
            Expect.isEmpty moves "after-only keys are ignored; 'a' didn't move so it also produces no move"

        testCase "a sub-threshold move (< 0.5px in both axes) is dropped" <| fun () ->
            let before = Map.ofList [ "a", box 100.0 100.0 50.0 50.0 ]
            let after = Map.ofList [ "a", box 100.3 99.8 50.0 50.0 ]
            let moves = Flip.plan before after
            Expect.isEmpty moves "a move under the sub-pixel threshold in both axes counts as no move"

        testCase "a move that clears the threshold on only one axis still animates" <| fun () ->
            let before = Map.ofList [ "a", box 100.0 100.0 50.0 50.0 ]
            let after = Map.ofList [ "a", box 100.1 105.0 50.0 50.0 ]
            let moves = Flip.plan before after
            Expect.equal (List.length moves) 1 "one axis exceeding the threshold is enough to produce a move"

        testCase "the plan's length never exceeds min(|before|, |after|)" <| fun () ->
            let before =
                Map.ofList [
                    "a", box 0.0 0.0 10.0 10.0
                    "b", box 50.0 50.0 10.0 10.0
                    "onlyBefore", box 200.0 200.0 10.0 10.0
                ]
            let after =
                Map.ofList [
                    "a", box 100.0 100.0 10.0 10.0
                    "b", box 150.0 150.0 10.0 10.0
                    "onlyAfter", box 300.0 300.0 10.0 10.0
                ]
            let moves = Flip.plan before after
            Expect.isTrue (List.length moves <= min before.Count after.Count) "the plan is bounded by the intersection of the two key sets"
    ]

let flipKeyTests =
    testList "Motion.flipKey" [
        testCase "emits both prop.key and data-flip-key from one value" <| fun () ->
            let props = flipKey "poster-42" |> List.map (fun p -> unbox<string * obj> p)
            let names = props |> List.map fst
            Expect.isTrue (List.contains "key" names) "flipKey must emit React's own key property"
            Expect.isTrue (List.contains "data-flip-key" names) "flipKey must emit the DOM attribute Flip.snapshot/Flip.play read"
            for (_, value) in props do
                Expect.equal (unbox<string> value) "poster-42" "both properties must carry the same key value, so they cannot drift apart"
    ]

Mocha.runTests motionTests |> ignore
Mocha.runTests flipKeyTests |> ignore
