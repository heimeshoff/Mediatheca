/// books-f33e2: client-side coverage for `Progress.buildProgressRequest` —
/// the pure seam behind the book detail page's "Update progress" popover.
module Mediatheca.Client.Pages.BookDetail.ProgressTests

open Fable.Mocha
open Mediatheca.Client.Pages.BookDetail.Progress

let progressTests =
    testList "books-f33e2: Progress.buildProgressRequest" [

        testCase "a page and a total build a Page-shaped request" <| fun () ->
            let result = buildProgressRequest (ByPage ("120", "300"))
            Expect.equal result (Ok { Percent = None; Page = Some 120; TotalPages = Some 300 }) "page/total shape"

        testCase "a bare percent builds a Percent-shaped request" <| fun () ->
            let result = buildProgressRequest (ByPercent "45")
            Expect.equal result (Ok { Percent = Some 45; Page = None; TotalPages = None }) "percent shape"

        testCase "a percent over 100 is rejected" <| fun () ->
            match buildProgressRequest (ByPercent "101") with
            | Error _ -> ()
            | Ok fields -> failwithf "expected an error, got %A" fields

        testCase "a page past its own total is rejected" <| fun () ->
            match buildProgressRequest (ByPage ("301", "300")) with
            | Error _ -> ()
            | Ok fields -> failwithf "expected an error, got %A" fields

        testCase "a negative percent is rejected" <| fun () ->
            match buildProgressRequest (ByPercent "-5") with
            | Error _ -> ()
            | Ok fields -> failwithf "expected an error, got %A" fields

        testCase "a non-numeric percent is rejected" <| fun () ->
            match buildProgressRequest (ByPercent "abc") with
            | Error _ -> ()
            | Ok fields -> failwithf "expected an error, got %A" fields
    ]

Mocha.runTests progressTests |> ignore
