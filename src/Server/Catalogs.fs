namespace Mediatheca.Server

open Thoth.Json.Net

module Catalogs =

    // Data records for events

    type CatalogCreatedData = {
        Name: string
        Description: string
        IsSorted: bool
    }

    type CatalogUpdatedData = {
        Name: string
        Description: string
    }

    type EntryAddedData = {
        EntryId: string
        MovieSlug: string
        Note: string option
    }

    type EntryUpdatedData = {
        EntryId: string
        Note: string option
    }

    // Events

    type CatalogEvent =
        | Catalog_created of CatalogCreatedData
        | Catalog_updated of CatalogUpdatedData
        | Catalog_removed
        | Entry_added of EntryAddedData * position: int
        | Entry_updated of EntryUpdatedData
        | Entry_removed of entryId: string
        | Entries_reordered of entryIds: string list

    // State

    type EntryState = {
        EntryId: string
        MovieSlug: string
        Note: string option
        Position: int
    }

    type ActiveCatalog = {
        Name: string
        Description: string
        IsSorted: bool
        Entries: Map<string, EntryState>
    }

    type CatalogState =
        | Not_created
        | Active of ActiveCatalog
        | Removed

    // Commands

    type CatalogCommand =
        | Create_catalog of CatalogCreatedData
        | Update_catalog of CatalogUpdatedData
        | Remove_catalog
        | Add_entry of EntryAddedData
        | Update_entry of EntryUpdatedData
        | Remove_entry of entryId: string
        | Reorder_entries of entryIds: string list

    // Evolve

    let evolve (state: CatalogState) (event: CatalogEvent) : CatalogState =
        match state, event with
        | Not_created, Catalog_created data ->
            Active {
                Name = data.Name
                Description = data.Description
                IsSorted = data.IsSorted
                Entries = Map.empty
            }
        | Active _, Catalog_removed -> Removed
        | Active catalog, Catalog_updated data ->
            Active { catalog with Name = data.Name; Description = data.Description }
        | Active catalog, Entry_added (data, position) ->
            let entry: EntryState = {
                EntryId = data.EntryId
                MovieSlug = data.MovieSlug
                Note = data.Note
                Position = position
            }
            Active { catalog with Entries = catalog.Entries |> Map.add data.EntryId entry }
        | Active catalog, Entry_updated data ->
            match catalog.Entries |> Map.tryFind data.EntryId with
            | Some entry ->
                let updated = { entry with Note = data.Note }
                Active { catalog with Entries = catalog.Entries |> Map.add data.EntryId updated }
            | None -> state
        | Active catalog, Entry_removed entryId ->
            Active { catalog with Entries = catalog.Entries |> Map.remove entryId }
        | Active catalog, Entries_reordered entryIds ->
            let updatedEntries =
                entryIds
                |> List.mapi (fun i eid ->
                    match catalog.Entries |> Map.tryFind eid with
                    | Some entry -> Some (eid, { entry with Position = i })
                    | None -> None)
                |> List.choose id
                |> List.fold (fun m (k, v) -> Map.add k v m) catalog.Entries
            Active { catalog with Entries = updatedEntries }
        | _ -> state

    let reconstitute (events: CatalogEvent list) : CatalogState =
        List.fold evolve Not_created events

    // Decide

    let decide (state: CatalogState) (command: CatalogCommand) : Result<CatalogEvent list, string> =
        match state, command with
        | Not_created, Create_catalog data ->
            Ok [ Catalog_created data ]
        | Active _, Create_catalog _ ->
            Error "Catalog already exists"
        | Active _, Update_catalog data ->
            Ok [ Catalog_updated data ]
        | Active _, Remove_catalog ->
            Ok [ Catalog_removed ]
        | Active catalog, Add_entry data ->
            if catalog.Entries |> Map.containsKey data.EntryId then
                Error $"Entry with id '{data.EntryId}' already exists"
            else
                let movieAlreadyInCatalog =
                    catalog.Entries |> Map.values |> Seq.exists (fun e -> e.MovieSlug = data.MovieSlug)
                if movieAlreadyInCatalog then
                    Error $"Movie '{data.MovieSlug}' is already in this catalog"
                else
                    let maxPos =
                        catalog.Entries
                        |> Map.values
                        |> Seq.map (fun e -> e.Position)
                        |> Seq.fold max -1
                    let position = maxPos + 1
                    Ok [ Entry_added (data, position) ]
        | Active catalog, Update_entry data ->
            if catalog.Entries |> Map.containsKey data.EntryId then
                Ok [ Entry_updated data ]
            else
                Error $"Entry with id '{data.EntryId}' does not exist"
        | Active catalog, Remove_entry entryId ->
            if catalog.Entries |> Map.containsKey entryId then
                Ok [ Entry_removed entryId ]
            else
                Ok []
        | Active catalog, Reorder_entries entryIds ->
            let allExist = entryIds |> List.forall (fun eid -> catalog.Entries |> Map.containsKey eid)
            if not allExist then
                Error "One or more entry ids do not exist"
            else
                Ok [ Entries_reordered entryIds ]
        | Removed, _ ->
            Error "Catalog has been removed"
        | Not_created, _ ->
            Error "Catalog does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Catalog-%s" slug

    // Serialization

    module Serialization =

        let private encodeCatalogCreatedData (data: CatalogCreatedData) =
            Encode.object [
                "name", Encode.string data.Name
                "description", Encode.string data.Description
                "isSorted", Encode.bool data.IsSorted
            ]

        let private decodeCatalogCreatedData: Decoder<CatalogCreatedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Description = get.Required.Field "description" Decode.string
                IsSorted = get.Required.Field "isSorted" Decode.bool
            })

        let private encodeCatalogUpdatedData (data: CatalogUpdatedData) =
            Encode.object [
                "name", Encode.string data.Name
                "description", Encode.string data.Description
            ]

        let private decodeCatalogUpdatedData: Decoder<CatalogUpdatedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Description = get.Required.Field "description" Decode.string
            })

        let private encodeEntryAddedData (data: EntryAddedData) =
            Encode.object [
                "entryId", Encode.string data.EntryId
                "movieSlug", Encode.string data.MovieSlug
                "note", Encode.option Encode.string data.Note
            ]

        let private decodeEntryAddedData: Decoder<EntryAddedData> =
            Decode.object (fun get -> {
                EntryId = get.Required.Field "entryId" Decode.string
                MovieSlug = get.Required.Field "movieSlug" Decode.string
                Note = get.Optional.Field "note" Decode.string
            })

        let private encodeEntryUpdatedData (data: EntryUpdatedData) =
            Encode.object [
                "entryId", Encode.string data.EntryId
                "note", Encode.option Encode.string data.Note
            ]

        let private decodeEntryUpdatedData: Decoder<EntryUpdatedData> =
            Decode.object (fun get -> {
                EntryId = get.Required.Field "entryId" Decode.string
                Note = get.Optional.Field "note" Decode.string
            })

        let serialize (event: CatalogEvent) : string * string =
            match event with
            | Catalog_created data ->
                "Catalog_created", Encode.toString 0 (encodeCatalogCreatedData data)
            | Catalog_updated data ->
                "Catalog_updated", Encode.toString 0 (encodeCatalogUpdatedData data)
            | Catalog_removed ->
                "Catalog_removed", "{}"
            | Entry_added (data, position) ->
                "Entry_added", Encode.toString 0 (Encode.object [
                    "data", encodeEntryAddedData data
                    "position", Encode.int position
                ])
            | Entry_updated data ->
                "Entry_updated", Encode.toString 0 (encodeEntryUpdatedData data)
            | Entry_removed entryId ->
                "Entry_removed", Encode.toString 0 (Encode.object [ "entryId", Encode.string entryId ])
            | Entries_reordered entryIds ->
                "Entries_reordered", Encode.toString 0 (Encode.object [
                    "entryIds", entryIds |> List.map Encode.string |> Encode.list
                ])

        let deserialize (eventType: string) (data: string) : CatalogEvent option =
            match eventType with
            | "Catalog_created" ->
                Decode.fromString decodeCatalogCreatedData data
                |> Result.toOption
                |> Option.map Catalog_created
            | "Catalog_updated" ->
                Decode.fromString decodeCatalogUpdatedData data
                |> Result.toOption
                |> Option.map Catalog_updated
            | "Catalog_removed" ->
                Some Catalog_removed
            | "Entry_added" ->
                let decoder =
                    Decode.object (fun get ->
                        let d = get.Required.Field "data" decodeEntryAddedData
                        let pos = get.Required.Field "position" Decode.int
                        Entry_added (d, pos))
                Decode.fromString decoder data
                |> Result.toOption
            | "Entry_updated" ->
                Decode.fromString decodeEntryUpdatedData data
                |> Result.toOption
                |> Option.map Entry_updated
            | "Entry_removed" ->
                Decode.fromString (Decode.field "entryId" Decode.string) data
                |> Result.toOption
                |> Option.map Entry_removed
            | "Entries_reordered" ->
                Decode.fromString (Decode.field "entryIds" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map Entries_reordered
            | _ -> None

        let toEventData (event: CatalogEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : CatalogEvent option =
            deserialize storedEvent.EventType storedEvent.Data
