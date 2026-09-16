module Mediatheca.Tests.CatalogsTests

open Expecto
open Mediatheca.Server.Catalogs
open Mediatheca.Shared

let private sampleCreateData: CatalogCreatedData = {
    Name = "My Favorites"
    Description = "All-time favorite movies"
    IsSorted = true
}

// curation-cyxbc (ADR-0079): legacy-shaped (no MediaType) by default, matching
// events recorded before entries were typed — most existing tests below exercise
// that shape unchanged. Typed cases construct their own data explicitly.
let private sampleEntryData: EntryAddedData = {
    EntryId = "entry-1"
    MovieSlug = "inception-2010"
    Note = Some "Mind-bending classic"
    MediaType = None
}

let private sampleEntryData2: EntryAddedData = {
    EntryId = "entry-2"
    MovieSlug = "the-matrix-1999"
    Note = None
    MediaType = None
}

let private givenWhenThen (given: CatalogEvent list) (command: CatalogCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let catalogTests =
    testList "Catalogs" [

        testList "Create_catalog" [
            testCase "creating catalog produces event" <| fun _ ->
                let result = givenWhenThen [] (Create_catalog sampleCreateData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Catalog_created data ->
                        Expect.equal data.Name "My Favorites" "Name should match"
                        Expect.equal data.Description "All-time favorite movies" "Description should match"
                        Expect.equal data.IsSorted true "IsSorted should match"
                    | _ -> failtest "Expected Catalog_created"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "creating catalog when already exists fails" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData ]
                                (Create_catalog sampleCreateData)
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Update_catalog" [
            testCase "updating catalog produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData ]
                                (Update_catalog { Name = "Updated Name"; Description = "New desc" })
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Catalog_updated data ->
                        Expect.equal data.Name "Updated Name" "Name should match"
                        Expect.equal data.Description "New desc" "Description should match"
                    | _ -> failtest "Expected Catalog_updated"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "updating non-existent catalog fails" <| fun _ ->
                let result = givenWhenThen [] (Update_catalog { Name = "Foo"; Description = "" })
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Remove_catalog" [
            testCase "removing catalog produces event" <| fun _ ->
                let result = givenWhenThen [ Catalog_created sampleCreateData ] Remove_catalog
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Catalog_removed -> ()
                    | _ -> failtest "Expected Catalog_removed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing already-removed catalog fails" <| fun _ ->
                let result = givenWhenThen [ Catalog_created sampleCreateData; Catalog_removed ] Remove_catalog
                match result with
                | Error msg -> Expect.stringContains msg "has been removed" "Should say has been removed"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Add_entry" [
            testCase "adding entry produces event with position" <| fun _ ->
                let result = givenWhenThen [ Catalog_created sampleCreateData ] (Add_entry sampleEntryData)
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Entry_added (data, pos) ->
                        Expect.equal data.EntryId "entry-1" "EntryId should match"
                        Expect.equal data.MovieSlug "inception-2010" "MovieSlug should match"
                        Expect.equal data.Note (Some "Mind-bending classic") "Note should match"
                        Expect.equal pos 0 "Position should be 0 for first entry"
                    | _ -> failtest "Expected Entry_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding second entry gets next position" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                                (Add_entry sampleEntryData2)
                match result with
                | Ok events ->
                    match events.[0] with
                    | Entry_added (_, pos) ->
                        Expect.equal pos 1 "Position should be 1 for second entry"
                    | _ -> failtest "Expected Entry_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding duplicate entry id fails" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                                (Add_entry sampleEntryData)
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"

            testCase "adding same movie twice fails" <| fun _ ->
                let duplicateMovie = { sampleEntryData2 with MovieSlug = "inception-2010" }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                                (Add_entry duplicateMovie)
                match result with
                | Error msg -> Expect.stringContains msg "already in this catalog" "Should say already in catalog"
                | Ok _ -> failtest "Expected error"

            // curation-cyxbc (ADR-0079 §3): duplicate rule cases for typed entries.
            testCase "adding same (MediaType, slug) pair twice fails" <| fun _ ->
                let typedMovie: EntryAddedData = { sampleEntryData with MediaType = Some MediaType.Movie }
                let duplicate: EntryAddedData = { sampleEntryData2 with MovieSlug = "inception-2010"; MediaType = Some MediaType.Movie }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (typedMovie, 0) ]
                                (Add_entry duplicate)
                match result with
                | Error msg -> Expect.stringContains msg "already in this catalog" "Should say already in catalog"
                | Ok _ -> failtest "Expected error"

            // Amended during curation-cyxbc's implementation (see ADR-0079's
            // amendment note): §3's typed-pair identity governs resolution,
            // type-scoped lookups and the removal cascade, but is not yet the
            // duplicate key here — the projection's UNIQUE(catalog_slug,
            // movie_slug) stays slug-only (§5) until a `media_type` backfill
            // lands, so accepting this add would emit an event the projection
            // can't durably store (its `INSERT OR REPLACE` would silently
            // clobber the earlier entry). Rejected for now; the follow-up that
            // widens the UNIQUE constraint relaxes this back to strict pair
            // identity.
            testCase "Add_entry rejects a same-slug entry of a different media type while the projection key is slug-only (ADR-0079 §5)" <| fun _ ->
                let typedMovie: EntryAddedData = { sampleEntryData with MediaType = Some MediaType.Movie }
                let typedBook: EntryAddedData = { sampleEntryData2 with MovieSlug = "inception-2010"; MediaType = Some MediaType.Book }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (typedMovie, 0) ]
                                (Add_entry typedBook)
                match result with
                | Error msg -> Expect.stringContains msg "already in this catalog" "Should say already in catalog"
                | Ok _ -> failtest "Expected error"

            testCase "adding a different type with a different slug when both are typed succeeds" <| fun _ ->
                let typedMovie: EntryAddedData = { sampleEntryData with MediaType = Some MediaType.Movie }
                let typedBook: EntryAddedData = { sampleEntryData2 with MediaType = Some MediaType.Book }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (typedMovie, 0) ]
                                (Add_entry typedBook)
                match result with
                | Ok events ->
                    match events.[0] with
                    | Entry_added (data, _) ->
                        Expect.equal data.MediaType (Some MediaType.Book) "MediaType should match"
                    | _ -> failtest "Expected Entry_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding a same-slug entry when the existing entry is legacy-untyped fails, naming the reason" <| fun _ ->
                let legacyMovie: EntryAddedData = { sampleEntryData with MediaType = None }
                let typedBook: EntryAddedData = { sampleEntryData2 with MovieSlug = "inception-2010"; MediaType = Some MediaType.Book }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (legacyMovie, 0) ]
                                (Add_entry typedBook)
                match result with
                | Error msg -> Expect.stringContains msg "before entries were typed" "Should name the legacy reason"
                | Ok _ -> failtest "Expected error"

            testCase "adding a same-slug entry when the new entry is untyped fails, naming the reason" <| fun _ ->
                let typedMovie: EntryAddedData = { sampleEntryData with MediaType = Some MediaType.Movie }
                let legacyDuplicate: EntryAddedData = { sampleEntryData2 with MovieSlug = "inception-2010"; MediaType = None }
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (typedMovie, 0) ]
                                (Add_entry legacyDuplicate)
                match result with
                | Error msg -> Expect.stringContains msg "before entries were typed" "Should name the legacy reason"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Update_entry" [
            testCase "updating entry produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                                (Update_entry { EntryId = "entry-1"; Note = Some "Updated note" })
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Entry_updated data ->
                        Expect.equal data.EntryId "entry-1" "EntryId should match"
                        Expect.equal data.Note (Some "Updated note") "Note should match"
                    | _ -> failtest "Expected Entry_updated"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "updating non-existent entry fails" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData ]
                                (Update_entry { EntryId = "entry-99"; Note = None })
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Remove_entry" [
            testCase "removing entry produces event" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                                (Remove_entry "entry-1")
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Entry_removed eid -> Expect.equal eid "entry-1" "EntryId should match"
                    | _ -> failtest "Expected Entry_removed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent entry is no-op" <| fun _ ->
                let result = givenWhenThen
                                [ Catalog_created sampleCreateData ]
                                (Remove_entry "entry-99")
                match result with
                | Ok events -> Expect.equal (List.length events) 0 "Should produce no events"
                | Error e -> failtest $"Expected success but got: {e}"
        ]

        testList "Reorder_entries" [
            testCase "reordering entries produces event" <| fun _ ->
                let given = [
                    Catalog_created sampleCreateData
                    Entry_added (sampleEntryData, 0)
                    Entry_added (sampleEntryData2, 1)
                ]
                let result = givenWhenThen given (Reorder_entries [ "entry-2"; "entry-1" ])
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Entries_reordered eids ->
                        Expect.equal eids [ "entry-2"; "entry-1" ] "Entry ids should match new order"
                    | _ -> failtest "Expected Entries_reordered"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "reordering with non-existent entry fails" <| fun _ ->
                let given = [ Catalog_created sampleCreateData; Entry_added (sampleEntryData, 0) ]
                let result = givenWhenThen given (Reorder_entries [ "entry-1"; "entry-99" ])
                match result with
                | Error msg -> Expect.stringContains msg "do not exist" "Should say do not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Evolve" [
            testCase "evolve applies reorder correctly" <| fun _ ->
                let events = [
                    Catalog_created sampleCreateData
                    Entry_added (sampleEntryData, 0)
                    Entry_added (sampleEntryData2, 1)
                    Entries_reordered [ "entry-2"; "entry-1" ]
                ]
                let state = reconstitute events
                match state with
                | Active catalog ->
                    let e1 = catalog.Entries |> Map.find "entry-1"
                    let e2 = catalog.Entries |> Map.find "entry-2"
                    Expect.equal e2.Position 0 "entry-2 should be at position 0"
                    Expect.equal e1.Position 1 "entry-1 should be at position 1"
                | _ -> failtest "Expected Active state"

            testCase "evolve removes entry from state" <| fun _ ->
                let events = [
                    Catalog_created sampleCreateData
                    Entry_added (sampleEntryData, 0)
                    Entry_removed "entry-1"
                ]
                let state = reconstitute events
                match state with
                | Active catalog ->
                    Expect.equal (Map.count catalog.Entries) 0 "Should have no entries"
                | _ -> failtest "Expected Active state"

            testCase "evolve updates catalog name" <| fun _ ->
                let events = [
                    Catalog_created sampleCreateData
                    Catalog_updated { Name = "New Name"; Description = "New Desc" }
                ]
                let state = reconstitute events
                match state with
                | Active catalog ->
                    Expect.equal catalog.Name "New Name" "Name should be updated"
                    Expect.equal catalog.Description "New Desc" "Description should be updated"
                | _ -> failtest "Expected Active state"
        ]

        testList "Serialization" [
            testCase "Catalog_created round-trip" <| fun _ ->
                let event = Catalog_created sampleCreateData
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Catalog_updated round-trip" <| fun _ ->
                let event = Catalog_updated { Name = "Updated"; Description = "Desc" }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Catalog_removed round-trip" <| fun _ ->
                let event = Catalog_removed
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Entry_added round-trip" <| fun _ ->
                let event = Entry_added (sampleEntryData, 5)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Entry_added with no note round-trip" <| fun _ ->
                let event = Entry_added (sampleEntryData2, 0)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            // curation-cyxbc (ADR-0079): legacy events (recorded before entries
            // were typed) carry no `mediaType` key at all — the decoder must
            // treat its absence as `None`, not fail.
            testCase "legacy JSON without mediaType decodes to MediaType = None" <| fun _ ->
                let legacyJson = """{"data":{"entryId":"entry-1","movieSlug":"inception-2010","note":null},"position":0}"""
                let deserialized = Serialization.deserialize "Entry_added" legacyJson
                match deserialized with
                | Some (Entry_added (data, position)) ->
                    Expect.equal data.EntryId "entry-1" "EntryId should match"
                    Expect.equal data.MovieSlug "inception-2010" "MovieSlug should match"
                    Expect.equal data.MediaType None "MediaType should be None"
                    Expect.equal position 0 "Position should match"
                | _ -> failtest "Expected Entry_added"

            testCase "typed Entry_added round-trips with its MediaType" <| fun _ ->
                let typedData: EntryAddedData = { sampleEntryData with MediaType = Some MediaType.Book }
                let event = Entry_added (typedData, 3)
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
                match deserialized with
                | Some (Entry_added (d, _)) -> Expect.equal d.MediaType (Some MediaType.Book) "MediaType should round-trip"
                | _ -> failtest "Expected Entry_added"

            testCase "Entry_updated round-trip" <| fun _ ->
                let event = Entry_updated { EntryId = "e1"; Note = Some "A note" }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Entry_updated with None note round-trip" <| fun _ ->
                let event = Entry_updated { EntryId = "e1"; Note = None }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Entry_removed round-trip" <| fun _ ->
                let event = Entry_removed "entry-1"
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Entries_reordered round-trip" <| fun _ ->
                let event = Entries_reordered [ "entry-2"; "entry-1"; "entry-3" ]
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
