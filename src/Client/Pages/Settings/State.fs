module Mediatheca.Client.Pages.Settings.State

open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types

[<Emit("fetch($0)")>]
let private jsFetch (url: string) : JS.Promise<obj> = jsNative

[<Emit("new TextDecoder().decode($0)")>]
let private decodeBytes (value: obj) : string = jsNative

let init () : Model * Cmd<Msg> =
    { TmdbApiKey = ""
      TmdbKeyInput = ""
      IsTesting = false
      IsSaving = false
      TestResult = None
      SaveResult = None
      RawgApiKey = ""
      RawgKeyInput = ""
      IsTestingRawg = false
      IsSavingRawg = false
      RawgTestResult = None
      RawgSaveResult = None
      SteamApiKey = ""
      SteamKeyInput = ""
      IsTestingSteam = false
      IsSavingSteam = false
      SteamTestResult = None
      SteamSaveResult = None
      SteamId = ""
      SteamIdInput = ""
      IsSavingSteamId = false
      SteamIdSaveResult = None
      IsResolvingVanity = false
      VanityInput = ""
      VanityResult = None
      IsImportingSteam = false
      SteamImportResult = None
      SteamFamilyToken = ""
      SteamFamilyTokenInput = ""
      IsSavingFamilyToken = false
      FamilyTokenSaveResult = None
      SteamFamilyMembers = []
      Friends = []
      IsFetchingFamilyMembers = false
      FetchFamilyMembersResult = None
      IsImportingSteamFamily = false
      SteamFamilyImportResult = None
      ImportProgress = None
      ImportLog = []
      CinemarcoDbPath = ""
      CinemarcoImagesPath = ""
      IsImporting = false
      ImportResult = None },
    Cmd.batch [ Cmd.ofMsg Load_tmdb_key; Cmd.ofMsg Load_rawg_key; Cmd.ofMsg Load_steam_key; Cmd.ofMsg Load_steam_id; Cmd.ofMsg Load_steam_family_token; Cmd.ofMsg Load_steam_family_members; Cmd.ofMsg Load_friends ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_tmdb_key ->
        model, Cmd.OfAsync.perform api.getTmdbApiKey () Tmdb_key_loaded

    | Tmdb_key_loaded key ->
        { model with TmdbApiKey = key }, Cmd.none

    | Tmdb_key_input_changed value ->
        { model with TmdbKeyInput = value; TestResult = None; SaveResult = None }, Cmd.none

    | Test_tmdb_key ->
        { model with IsTesting = true; TestResult = None },
        Cmd.OfAsync.either api.testTmdbApiKey model.TmdbKeyInput
            Test_result
            (fun ex -> Test_result (Error ex.Message))

    | Test_result result ->
        let testResult =
            match result with
            | Ok () -> Ok "Connection successful"
            | Error e -> Error e
        { model with IsTesting = false; TestResult = Some testResult }, Cmd.none

    | Save_tmdb_key ->
        { model with IsSaving = true; SaveResult = None },
        Cmd.OfAsync.either api.setTmdbApiKey model.TmdbKeyInput
            Save_result
            (fun ex -> Save_result (Error ex.Message))

    | Save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "API key saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_tmdb_key
            | Error _ -> Cmd.none
        { model with IsSaving = false; SaveResult = Some saveResult }, cmd

    | Load_rawg_key ->
        model, Cmd.OfAsync.perform api.getRawgApiKey () Rawg_key_loaded

    | Rawg_key_loaded key ->
        { model with RawgApiKey = key }, Cmd.none

    | Rawg_key_input_changed value ->
        { model with RawgKeyInput = value; RawgTestResult = None; RawgSaveResult = None }, Cmd.none

    | Test_rawg_key ->
        { model with IsTestingRawg = true; RawgTestResult = None },
        Cmd.OfAsync.either api.testRawgApiKey model.RawgKeyInput
            Rawg_test_result
            (fun ex -> Rawg_test_result (Error ex.Message))

    | Rawg_test_result result ->
        let testResult =
            match result with
            | Ok () -> Ok "Connection successful"
            | Error e -> Error e
        { model with IsTestingRawg = false; RawgTestResult = Some testResult }, Cmd.none

    | Save_rawg_key ->
        { model with IsSavingRawg = true; RawgSaveResult = None },
        Cmd.OfAsync.either api.setRawgApiKey model.RawgKeyInput
            Rawg_save_result
            (fun ex -> Rawg_save_result (Error ex.Message))

    | Rawg_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "API key saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_rawg_key
            | Error _ -> Cmd.none
        { model with IsSavingRawg = false; RawgSaveResult = Some saveResult }, cmd

    // Steam Integration
    | Load_steam_key ->
        model, Cmd.OfAsync.perform api.getSteamApiKey () Steam_key_loaded

    | Steam_key_loaded key ->
        { model with SteamApiKey = key }, Cmd.none

    | Steam_key_input_changed value ->
        { model with SteamKeyInput = value; SteamTestResult = None; SteamSaveResult = None }, Cmd.none

    | Test_steam_key ->
        { model with IsTestingSteam = true; SteamTestResult = None },
        Cmd.OfAsync.either api.testSteamApiKey model.SteamKeyInput
            Steam_test_result
            (fun ex -> Steam_test_result (Error ex.Message))

    | Steam_test_result result ->
        let testResult =
            match result with
            | Ok () -> Ok "Connection successful"
            | Error e -> Error e
        { model with IsTestingSteam = false; SteamTestResult = Some testResult }, Cmd.none

    | Save_steam_key ->
        { model with IsSavingSteam = true; SteamSaveResult = None },
        Cmd.OfAsync.either api.setSteamApiKey model.SteamKeyInput
            Steam_save_result
            (fun ex -> Steam_save_result (Error ex.Message))

    | Steam_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "API key saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_steam_key
            | Error _ -> Cmd.none
        { model with IsSavingSteam = false; SteamSaveResult = Some saveResult }, cmd

    | Load_steam_id ->
        model, Cmd.OfAsync.perform api.getSteamId () Steam_id_loaded

    | Steam_id_loaded steamId ->
        { model with SteamId = steamId; SteamIdInput = steamId }, Cmd.none

    | Steam_id_input_changed value ->
        { model with SteamIdInput = value; SteamIdSaveResult = None }, Cmd.none

    | Save_steam_id ->
        { model with IsSavingSteamId = true; SteamIdSaveResult = None },
        Cmd.OfAsync.either api.setSteamId model.SteamIdInput
            Steam_id_save_result
            (fun ex -> Steam_id_save_result (Error ex.Message))

    | Steam_id_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "Steam ID saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_steam_id
            | Error _ -> Cmd.none
        { model with IsSavingSteamId = false; SteamIdSaveResult = Some saveResult }, cmd

    | Vanity_input_changed value ->
        { model with VanityInput = value; VanityResult = None }, Cmd.none

    | Resolve_vanity_url ->
        { model with IsResolvingVanity = true; VanityResult = None },
        Cmd.OfAsync.either api.resolveSteamVanityUrl model.VanityInput
            Vanity_resolved
            (fun ex -> Vanity_resolved (Error ex.Message))

    | Vanity_resolved result ->
        match result with
        | Ok steamId ->
            { model with IsResolvingVanity = false; VanityResult = Some (Ok steamId); SteamIdInput = steamId }, Cmd.none
        | Error e ->
            { model with IsResolvingVanity = false; VanityResult = Some (Error e) }, Cmd.none

    | Import_steam_library ->
        { model with IsImportingSteam = true; SteamImportResult = None },
        Cmd.OfAsync.either api.importSteamLibrary ()
            Steam_import_completed
            (fun ex -> Steam_import_completed (Error ex.Message))

    | Steam_import_completed result ->
        { model with IsImportingSteam = false; SteamImportResult = Some result }, Cmd.none

    // Steam Family
    | Load_steam_family_token ->
        model, Cmd.OfAsync.perform api.getSteamFamilyToken () Steam_family_token_loaded

    | Steam_family_token_loaded token ->
        { model with SteamFamilyToken = token }, Cmd.none

    | Steam_family_token_input_changed value ->
        { model with SteamFamilyTokenInput = value; FamilyTokenSaveResult = None }, Cmd.none

    | Save_steam_family_token ->
        { model with IsSavingFamilyToken = true; FamilyTokenSaveResult = None },
        Cmd.OfAsync.either api.setSteamFamilyToken model.SteamFamilyTokenInput
            Steam_family_token_save_result
            (fun ex -> Steam_family_token_save_result (Error ex.Message))

    | Steam_family_token_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "Family token saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_steam_family_token
            | Error _ -> Cmd.none
        { model with IsSavingFamilyToken = false; FamilyTokenSaveResult = Some saveResult }, cmd

    | Load_steam_family_members ->
        model, Cmd.OfAsync.perform api.getSteamFamilyMembers () Steam_family_members_loaded

    | Steam_family_members_loaded members ->
        { model with SteamFamilyMembers = members }, Cmd.none

    | Fetch_steam_family_members ->
        { model with IsFetchingFamilyMembers = true; FetchFamilyMembersResult = None },
        Cmd.OfAsync.either api.fetchSteamFamilyMembers ()
            Steam_family_members_fetched
            (fun ex -> Steam_family_members_fetched (Error ex.Message))

    | Steam_family_members_fetched result ->
        match result with
        | Ok members ->
            { model with
                IsFetchingFamilyMembers = false
                FetchFamilyMembersResult = Some (Ok (sprintf "Found %d family members" members.Length))
                SteamFamilyMembers = members }, Cmd.none
        | Error e ->
            { model with
                IsFetchingFamilyMembers = false
                FetchFamilyMembersResult = Some (Error e) }, Cmd.none

    | Load_friends ->
        model, Cmd.OfAsync.perform api.getFriends () Friends_loaded

    | Friends_loaded friends ->
        { model with Friends = friends }, Cmd.none

    | Update_family_member_friend (steamId, friendSlug) ->
        let updated =
            model.SteamFamilyMembers |> List.map (fun m ->
                if m.SteamId = steamId then { m with FriendSlug = friendSlug }
                else m)
        { model with SteamFamilyMembers = updated }, Cmd.none

    | Save_steam_family_members ->
        model, Cmd.OfAsync.either api.setSteamFamilyMembers model.SteamFamilyMembers
            Steam_family_members_save_result
            (fun ex -> Steam_family_members_save_result (Error ex.Message))

    | Steam_family_members_save_result _ ->
        model, Cmd.none

    | Import_steam_family ->
        { model with IsImportingSteamFamily = true; SteamFamilyImportResult = None; ImportProgress = None; ImportLog = [] },
        Cmd.ofEffect (fun dispatch ->
            async {
                try
                    let! response = jsFetch "/api/stream/import-steam-family" |> Async.AwaitPromise
                    let reader: obj = response?body?getReader()
                    let mutable buffer = ""
                    let mutable reading = true
                    while reading do
                        let! chunk = (reader?read() : JS.Promise<obj>) |> Async.AwaitPromise
                        let isDone: bool = chunk?``done``
                        if isDone then
                            reading <- false
                        else
                            let value: obj = chunk?value
                            let text = decodeBytes value
                            buffer <- buffer + text
                            let mutable idx = buffer.IndexOf("\n\n")
                            while idx >= 0 do
                                let message = buffer.[0..idx-1]
                                buffer <- buffer.[idx+2..]
                                let dataLine =
                                    if message.StartsWith("data: ") then message.[6..]
                                    else message
                                if dataLine <> "" then
                                    let parsed: obj = JS.JSON.parse dataLine
                                    let eventType: string = parsed?``type``
                                    match eventType with
                                    | "progress" ->
                                        let progress: SteamFamilyImportProgress = {
                                            Current = parsed?current |> int
                                            Total = parsed?total |> int
                                            GameName = parsed?gameName |> string
                                            Action = parsed?action |> string
                                        }
                                        dispatch (Steam_family_import_progress progress)
                                    | "complete" ->
                                        let errors: string list =
                                            let arr: obj array = parsed?errors
                                            arr |> Array.map string |> Array.toList
                                        let result: SteamFamilyImportResult = {
                                            FamilyMembers = parsed?familyMembers |> int
                                            GamesProcessed = parsed?gamesProcessed |> int
                                            GamesCreated = parsed?gamesCreated |> int
                                            FamilyOwnersSet = parsed?familyOwnersSet |> int
                                            Errors = errors
                                        }
                                        dispatch (Steam_family_import_completed (Ok result))
                                    | "error" ->
                                        let errorMsg: string = parsed?message |> string
                                        dispatch (Steam_family_import_completed (Error errorMsg))
                                    | _ -> ()
                                idx <- buffer.IndexOf("\n\n")
                with ex ->
                    dispatch (Steam_family_import_completed (Error ex.Message))
            } |> Async.StartImmediate
        )

    | Steam_family_import_progress progress ->
        { model with
            ImportProgress = Some progress
            ImportLog = model.ImportLog @ [ (progress.GameName, progress.Action) ] }, Cmd.none

    | Steam_family_import_completed result ->
        { model with IsImportingSteamFamily = false; SteamFamilyImportResult = Some result; ImportProgress = None }, Cmd.none

    | Cinemarco_db_path_changed value ->
        { model with CinemarcoDbPath = value; ImportResult = None }, Cmd.none

    | Cinemarco_images_path_changed value ->
        { model with CinemarcoImagesPath = value; ImportResult = None }, Cmd.none

    | Start_cinemarco_import ->
        let request: ImportFromCinemarcoRequest = {
            DatabasePath = model.CinemarcoDbPath
            ImagesPath = model.CinemarcoImagesPath
        }
        { model with IsImporting = true; ImportResult = None },
        Cmd.OfAsync.either api.importFromCinemarco request
            Import_completed
            (fun ex -> Import_completed (Error ex.Message))

    | Import_completed result ->
        { model with IsImporting = false; ImportResult = Some result }, Cmd.none
