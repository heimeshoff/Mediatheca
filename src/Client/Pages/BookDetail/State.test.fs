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

/// books-xyqyb: `Set_book_status Finished` stamps today's local date instead
/// of `None` (ADR-0082 §8), and the hero's `finished {date}` line becomes
/// click-to-edit via `Edit_finished_date` / `Commit_finished_date` /
/// `Cancel_edit_finished_date`.
let finishedDateTests =
    testList "books-xyqyb: BookDetail.State finished-date" [

        testCaseAsync "Set_book_status Finished sends effectiveOn = Some <today, yyyy-MM-dd>" <| async {
            let mutable captured : (string * BookStatus * string option) option = None
            let api : IMediathecaApi =
                createObj [
                    "setBookStatus" ==> (fun (slug: string) (status: BookStatus) (effectiveOn: string option) ->
                        captured <- Some (slug, status, effectiveOn)
                        async { return Ok () })
                ] |> unbox
            let model, _ = init "some-book"
            let _, cmd = update api (Set_book_status BookStatus.Finished) model
            let! _ = runCmd cmd
            let expectedToday = System.DateTime.Now.ToString("yyyy-MM-dd")
            match captured with
            | Some (slug, status, effectiveOn) ->
                Expect.equal slug "some-book" "the book's own slug"
                Expect.equal status BookStatus.Finished "the requested status"
                Expect.equal effectiveOn (Some expectedToday) "effectiveOn is today's local date, not None"
            | None -> failtest "expected setBookStatus to have been called"
        }

        testCaseAsync "Set_book_status Backlog/InFocus/Abandoned still send effectiveOn = None" <| async {
            for status in [ BookStatus.Backlog; BookStatus.InFocus; BookStatus.Abandoned ] do
                let mutable captured : string option option = None
                let api : IMediathecaApi =
                    createObj [
                        "setBookStatus" ==> (fun (_: string) (_: BookStatus) (effectiveOn: string option) ->
                            captured <- Some effectiveOn
                            async { return Ok () })
                    ] |> unbox
                let model, _ = init "some-book"
                let _, cmd = update api (Set_book_status status) model
                let! _ = runCmd cmd
                Expect.equal captured (Some None) (sprintf "%A carries no source date" status)
        }

        testCaseAsync "Commit_finished_date issues setBookStatus Finished (Some picked) and reloads on Ok" <| async {
            let mutable captured : (string * BookStatus * string option) option = None
            let calls = ResizeArray<string>()
            let api : IMediathecaApi =
                createObj [
                    "setBookStatus" ==> (fun (slug: string) (status: BookStatus) (effectiveOn: string option) ->
                        captured <- Some (slug, status, effectiveOn)
                        calls.Add "setBookStatus"
                        async { return Ok () })
                    "getBook" ==> (fun (_: string) -> calls.Add "getBook"; async { return None })
                ] |> unbox
            let model, _ = init "moby-dick-1851"
            let editing = { model with IsEditingFinishedDate = true }
            let afterCommit, cmd = update api (Commit_finished_date "2026-03-10") editing
            Expect.isFalse afterCommit.IsEditingFinishedDate "editing closes immediately on commit"
            let! dispatched = runCmd cmd
            match captured with
            | Some (slug, status, effectiveOn) ->
                Expect.equal slug "moby-dick-1851" "the book's own slug"
                Expect.equal status BookStatus.Finished "re-dating stays Finished"
                Expect.equal effectiveOn (Some "2026-03-10") "the picked date"
            | None -> failtest "expected setBookStatus to have been called"
            // Status_result (Ok ()) reloads the book — dispatched, then run again.
            for msg in dispatched do
                let _, reloadCmd = update api msg model
                let! _ = runCmd reloadCmd
                ()
            Expect.isTrue (calls |> Seq.contains "getBook") "the book reloads after a successful re-date"
        }

        testCase "Cancel_edit_finished_date clears IsEditingFinishedDate with no api call" <| fun () ->
            let fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
            let model, _ = init "some-book"
            let editing = { model with IsEditingFinishedDate = true }
            let cancelled, cmd = update fakeApi Cancel_edit_finished_date editing
            Expect.isFalse cancelled.IsEditingFinishedDate "editing closes"
            Expect.isEmpty cmd "no command — no api call"

        testCase "Edit_finished_date sets IsEditingFinishedDate with no api call" <| fun () ->
            let fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
            let model, _ = init "some-book"
            let editing, cmd = update fakeApi Edit_finished_date model
            Expect.isTrue editing.IsEditingFinishedDate "editing opens"
            Expect.isEmpty cmd "no command — no api call"
    ]

/// books-wk67x (amending ADR-0076 §2): the History list can hold several
/// same-day, same-source rows now — `EntryId`, not `(ObservedOn, Source)`,
/// is what a row is keyed and removed by. `sampleBook` carries two such
/// rows (same day, same source, different EntryId) exactly as the History
/// list would render them; the tests below prove the remove flow tracks
/// and dispatches the RIGHT row's id, never the other one's.
let private sampleBook (history: ReadingProgressDto list) : BookDetail =
    { Slug = "moby-dick-1851"
      Title = "Moby-Dick"
      Authors = [ "Herman Melville" ]
      Year = Some 1851
      CoverRef = None
      Subjects = []
      Format = BookFormat.Audiobook
      Status = BookStatus.InFocus
      ProgressPercent = 40
      ProgressSource = Some ProgressSource.Audible
      ProgressObservedOn = Some "2026-09-18"
      PersonalRating = None
      FinishedAt = None
      AddedAt = Some "2026-09-01"
      Isbn13 = None
      OpenLibraryWorkKey = None
      OpenLibraryEditionKey = None
      AudibleAsin = Some "B000JMKNQQ"
      RecommendedBy = []
      Description = None
      PageCount = None
      RuntimeMinutes = None
      Narrators = []
      SeriesName = None
      SeriesPosition = None
      Publisher = None
      PublishedDate = None
      AverageRating = None
      Language = None
      ProgressHistory = history
      HasNotesContent = false }

let private twoSameDaySameSourceRows : ReadingProgressDto list =
    [ { EntryId = 101L; ObservedOn = "2026-09-18"; Source = ProgressSource.Audible; Percent = 20; Position = None; Kind = Observed }
      { EntryId = 205L; ObservedOn = "2026-09-18"; Source = ProgressSource.Audible; Percent = 40; Position = None; Kind = Observed } ]

let historyEntryRemovalTests =
    testList "books-wk67x: BookDetail.State history-entry removal by id" [

        testCase "Confirm_remove_observation tracks the SPECIFIC row's entry id, not the other same-day/source row's" <| fun () ->
            let fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
            let model, _ = init "moby-dick-1851"
            let loaded = { model with Book = Some (sampleBook twoSameDaySameSourceRows) }
            let confirmed, cmd = update fakeApi (Confirm_remove_observation 205L) loaded
            Expect.equal confirmed.ConfirmingRemoveObservation (Some 205L) "tracks the row the user actually clicked"
            Expect.isFalse (confirmed.ConfirmingRemoveObservation = Some 101L) "never confused with the OTHER same-day/source row"
            Expect.isEmpty cmd "confirming alone makes no api call"

        testCaseAsync "Remove_observation dispatches removeBookProgressEntry with the confirmed row's own id, never the sibling row's" <| async {
            let mutable captured : (string * int64) option = None
            let api : IMediathecaApi =
                createObj [
                    "removeBookProgressEntry" ==> (fun (slug: string) (entryId: int64) ->
                        captured <- Some (slug, entryId)
                        async { return Ok () })
                ] |> unbox
            let model, _ = init "moby-dick-1851"
            let loaded = { model with Book = Some (sampleBook twoSameDaySameSourceRows); ConfirmingRemoveObservation = Some 205L }
            let cleared, cmd = update api (Remove_observation 205L) loaded
            Expect.equal cleared.ConfirmingRemoveObservation None "the confirm state clears immediately"
            let! _ = runCmd cmd
            match captured with
            | Some (slug, entryId) ->
                Expect.equal slug "moby-dick-1851" "the book's own slug"
                Expect.equal entryId 205L "removes the row the user confirmed"
                Expect.isFalse (entryId = 101L) "never removes the OTHER same-day/source row instead"
            | None -> failtest "expected removeBookProgressEntry to have been called"
        }
    ]

Mocha.runTests stateTests |> ignore
Mocha.runTests finishedDateTests |> ignore
Mocha.runTests historyEntryRemovalTests |> ignore
