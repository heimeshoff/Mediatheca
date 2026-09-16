/// books-f3sb2: MVU coverage for the book detail page's catalog wiring --
/// `Load_book` fires both catalog loads alongside the existing book/friends
/// loads, `Add_to_catalog` (and its create-and-add sibling) send a typed
/// `AddCatalogEntryRequest` (ADR-0079: `MediaType = Book`), and
/// `Catalog_result (Ok ())` reloads both catalog lists. The api stand-in is
/// a plain JS object carrying only the members each case touches -- the
/// same `createObj [...] |> unbox` idiom `Dashboard/ExpandCard.test.fs`
/// uses; `Unchecked.defaultof<IMediathecaApi>` is `null` in Fable and would
/// throw the moment `update` built the fetch command. `Cmd.OfAsync.perform`
/// schedules its dispatch through a 1ms JS timer even in tests (see
/// `Fable.Elmish`'s `AsyncHelpers.start`), so every case that needs an
/// effect to actually run is a `testCaseAsync` with a short `Async.Sleep`
/// before asserting -- a plain `testCase` would race the timer.
module Mediatheca.Client.Pages.BookDetail.StateTests

open Elmish
open Fable.Core.JsInterop
open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.Types
open Mediatheca.Client.Pages.BookDetail.State

/// Runs every effect in a `Cmd<Msg>` against a dispatch that records what
/// came back, then gives the JS timer behind `Cmd.OfAsync.perform` a beat to
/// fire before returning what was collected.
let private runCmd (cmd: Cmd<Msg>) : Async<Msg list> = async {
    let dispatched = ResizeArray<Msg>()
    for effect in cmd do
        effect dispatched.Add
    do! Async.Sleep 50
    return List.ofSeq dispatched
}

let stateTests =
    testList "books-f3sb2: BookDetail.State catalogs" [

        testCaseAsync "Load_book issues both catalog loads alongside the book and friends loads" <| async {
            let calls = ResizeArray<string>()
            let api : IMediathecaApi =
                createObj [
                    "getBook" ==> (fun (_: string) -> calls.Add "getBook"; async { return None })
                    "getFriends" ==> (fun () -> calls.Add "getFriends"; async { return [] })
                    "getCatalogs" ==> (fun () -> calls.Add "getCatalogs"; async { return [] })
                    "getCatalogsForBook" ==> (fun (_: string) -> calls.Add "getCatalogsForBook"; async { return [] })
                ] |> unbox
            let model, _ = init "some-book"
            let _, cmd = update api (Load_book "some-book") model
            let! _ = runCmd cmd
            Expect.equal
                (calls |> Seq.sort |> List.ofSeq)
                [ "getBook"; "getCatalogs"; "getCatalogsForBook"; "getFriends" ]
                "all four loads fire, including both catalog loads"
        }

        testCaseAsync "Add_to_catalog sends an AddCatalogEntryRequest with MediaType = Book and the book's slug" <| async {
            let mutable captured : (string * AddCatalogEntryRequest) option = None
            let api : IMediathecaApi =
                createObj [
                    "addCatalogEntry" ==> (fun (catalogSlug: string) (request: AddCatalogEntryRequest) ->
                        captured <- Some (catalogSlug, request)
                        async { return Ok "entry-1" })
                ] |> unbox
            let model, _ = init "moby-dick-1851"
            let _, cmd = update api (Add_to_catalog "want-to-read") model
            let! _ = runCmd cmd
            match captured with
            | Some (catalogSlug, request) ->
                Expect.equal catalogSlug "want-to-read" "the target catalog's slug"
                Expect.equal request.MediaSlug "moby-dick-1851" "the book's own slug"
                Expect.equal request.MediaType MediaType.Book "typed as a Book entry (ADR-0079)"
                Expect.equal request.Note None "no note is attached from the pill row"
            | None -> failtest "expected addCatalogEntry to have been called"
        }

        testCaseAsync "Create_catalog_and_add creates the catalog, then sends the same typed request into it" <| async {
            let mutable createRequest : CreateCatalogRequest option = None
            let mutable addedTo : (string * AddCatalogEntryRequest) option = None
            let api : IMediathecaApi =
                createObj [
                    "createCatalog" ==> (fun (request: CreateCatalogRequest) ->
                        createRequest <- Some request
                        async { return Ok "new-catalog" })
                    "addCatalogEntry" ==> (fun (catalogSlug: string) (request: AddCatalogEntryRequest) ->
                        addedTo <- Some (catalogSlug, request)
                        async { return Ok "entry-1" })
                ] |> unbox
            let model, _ = init "moby-dick-1851"
            let _, cmd = update api (Create_catalog_and_add "Whaling Classics") model
            let! _ = runCmd cmd
            Expect.equal (createRequest |> Option.map (fun r -> r.Name)) (Some "Whaling Classics") "the new catalog's name"
            match addedTo with
            | Some (catalogSlug, request) ->
                Expect.equal catalogSlug "new-catalog" "added into the freshly-created catalog"
                Expect.equal request.MediaType MediaType.Book "still typed as a Book entry"
                Expect.equal request.MediaSlug "moby-dick-1851" "the book's own slug"
            | None -> failtest "expected addCatalogEntry to have been called after createCatalog"
        }

        testCaseAsync "Catalog_result (Ok ()) reloads both the all-catalogs and this-book's-catalogs lists" <| async {
            let calls = ResizeArray<string>()
            let api : IMediathecaApi =
                createObj [
                    "getCatalogs" ==> (fun () -> calls.Add "getCatalogs"; async { return [] })
                    "getCatalogsForBook" ==> (fun (_: string) -> calls.Add "getCatalogsForBook"; async { return [] })
                ] |> unbox
            let model, _ = init "some-book"
            let _, cmd = update api (Catalog_result (Ok ())) model
            let! _ = runCmd cmd
            Expect.equal
                (calls |> Seq.sort |> List.ofSeq)
                [ "getCatalogs"; "getCatalogsForBook" ]
                "both catalog lists reload"
        }

        testCase "Open_catalog_picker / Close_catalog_picker toggle ShowCatalogPicker without touching the api" <| fun () ->
            let fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
            let model, _ = init "some-book"
            let opened, _ = update fakeApi Open_catalog_picker model
            Expect.isTrue opened.ShowCatalogPicker "picker opens"
            let closed, _ = update fakeApi Close_catalog_picker opened
            Expect.isFalse closed.ShowCatalogPicker "picker closes"
    ]

Mocha.runTests stateTests |> ignore
