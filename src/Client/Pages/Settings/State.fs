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

/// integration-hebjs: `Steam.mintFamilyAccessToken`'s missing/rejected
/// refresh-token errors are prefixed "reconnect required" (mirroring
/// ADR-0011's Jellyfin `reauthThunk`). Used to distinguish "your Steam
/// connection needs re-establishing" from any other family-fetch failure, so
/// the UI can show a clear "Reconnect Steam" prompt instead of a generic
/// error banner (acceptance criterion 4).
let private isReconnectRequired (message: string) : bool =
    not (System.String.IsNullOrEmpty(message))
    && message.ToLowerInvariant().Contains("reconnect required")

/// DOM id of the Projections section's outer card — the dirty banner's
/// "Go to Projections" scroll target (administration-k3vmt).
let projectionsSectionElementId = "settings-admin-projections"

// Administration section load Cmds (administration-k3vmt): each mirrors the
// exact Cmd the corresponding child page's own `init` returns (see
// Pages/Admin/State.init, whose Cmd is discarded on construction below in
// favor of firing these lazily on first expand instead). Kept next to each
// other so the "one Cmd per section, matching that section's own init" shape
// stays obviously true at a glance.
let private loadEventsCmd : Cmd<Msg> =
    Cmd.batch [
        Cmd.ofMsg Mediatheca.Client.Pages.EventBrowser.Types.Load_filter_options
        Cmd.ofMsg (Mediatheca.Client.Pages.EventBrowser.Types.Load_page (None, []))
    ]
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Event_browser_msg
    |> Cmd.map Admin_msg

let private loadHealthCmd : Cmd<Msg> =
    Cmd.ofMsg Mediatheca.Client.Pages.AdminHealth.Types.Load
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Health_msg
    |> Cmd.map Admin_msg

/// Public: this is the one section whose load also fires eagerly, from root
/// `State.Url_changed`'s Settings branch rather than only on first expand —
/// see the Model doc comment for why.
let loadProjectionStatsCmd : Cmd<Msg> =
    Cmd.ofMsg Mediatheca.Client.Pages.AdminProjections.Types.Load
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Projections_msg
    |> Cmd.map Admin_msg

let private loadImagesCmd : Cmd<Msg> =
    Cmd.ofMsg Mediatheca.Client.Pages.AdminImages.Types.Load
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Images_msg
    |> Cmd.map Admin_msg

let private loadJobsCmd : Cmd<Msg> =
    Cmd.ofMsg Mediatheca.Client.Pages.AdminJobs.Types.Load
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Jobs_msg
    |> Cmd.map Admin_msg

let private loadSurgeryCmd : Cmd<Msg> =
    Cmd.ofMsg Mediatheca.Client.Pages.AdminSurgery.Types.Load_backup_stats
    |> Cmd.map Mediatheca.Client.Pages.Admin.Types.Surgery_msg
    |> Cmd.map Admin_msg

/// Scrolls the Projections section's card into view once the DOM has had a
/// tick to reflect the just-set `ProjectionsSectionOpen = true` (the collapse
/// content needs to actually be laid out before `scrollIntoView` has
/// anything meaningful to measure).
let private scrollToProjectionsCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        Fable.Core.JS.setTimeout
            (fun () ->
                let el = Browser.Dom.document.getElementById projectionsSectionElementId
                if not (isNull el) then
                    el?scrollIntoView ({| behavior = "smooth"; block = "start" |})
            )
            50
        |> ignore)

/// DOM id of the danger gate's unlock box — the scroll/focus target the
/// dirty banner falls back to while the administration sections are locked.
let adminUnlockInputElementId = "settings-admin-unlock"

/// The word the operator has to type to reveal the administration sections.
/// Compared trimmed and case-insensitively (`danger`, `DANGER`, ` Danger `
/// all pass) — the gate exists to make the reveal *deliberate*, not to be a
/// spelling test, and it is emphatically not a secret.
let adminUnlockWord = "danger"

let private matchesUnlockWord (value: string) =
    value.Trim().ToLowerInvariant() = adminUnlockWord

/// Mirrors `scrollToProjectionsCmd` for the locked case: brings the unlock
/// box into view and focuses it, so the dirty banner's affordance still
/// leads somewhere useful without itself bypassing the gate.
let private focusUnlockInputCmd : Cmd<Msg> =
    Cmd.ofEffect (fun _ ->
        Fable.Core.JS.setTimeout
            (fun () ->
                let el = Browser.Dom.document.getElementById adminUnlockInputElementId
                if not (isNull el) then
                    el?scrollIntoView ({| behavior = "smooth"; block = "center" |})
                    el?focus ()
            )
            50
        |> ignore)

let init () : Model * Cmd<Msg> =
    let adminModel, _ = Mediatheca.Client.Pages.Admin.State.init ()
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
      SteamConnected = false
      IsConnectingSteam = false
      SteamConnectQrDataUrl = None
      SteamConnectError = None
      SteamNeedsReconnect = false
      SteamFamilyMembers = []
      Friends = []
      IsFetchingFamilyMembers = false
      FetchFamilyMembersResult = None
      IsImportingSteamFamily = false
      SteamFamilyImportResult = None
      ImportProgress = None
      ImportLog = []
      JellyfinServerUrl = ""
      JellyfinServerUrlInput = ""
      JellyfinUsername = ""
      JellyfinUsernameInput = ""
      JellyfinPasswordInput = ""
      IsTestingJellyfin = false
      IsSavingJellyfin = false
      JellyfinTestResult = None
      JellyfinSaveResult = None
      IsScanningJellyfin = false
      JellyfinScanResult = None
      IsImportingJellyfin = false
      JellyfinImportResult = None
      PlaytimeSyncStatus = None
      JellyfinLastSyncTime = None
      JellyfinSyncStatus = None
      SteamFamilyLastSync = None
      AdminModel = adminModel
      AdminUnlockInput = ""
      AdminUnlocked = false
      EventsSectionOpen = false
      EventsSectionLoaded = false
      ProjectionsSectionOpen = false
      ProjectionsSectionLoaded = false
      HealthSectionOpen = false
      HealthSectionLoaded = false
      ImagesSectionOpen = false
      ImagesSectionLoaded = false
      JobsSectionOpen = false
      JobsSectionLoaded = false
      SurgerySectionOpen = false
      SurgerySectionLoaded = false },
    Cmd.batch [ Cmd.ofMsg Load_tmdb_key; Cmd.ofMsg Load_rawg_key; Cmd.ofMsg Load_steam_key; Cmd.ofMsg Load_steam_id; Cmd.ofMsg Load_steam_family_token; Cmd.ofMsg Load_steam_connect_status; Cmd.ofMsg Load_steam_family_members; Cmd.ofMsg Load_friends; Cmd.ofMsg Load_jellyfin_settings; Cmd.ofMsg Load_playtime_sync_status; Cmd.ofMsg Load_jellyfin_sync_status; Cmd.ofMsg Load_steam_family_last_sync ]

let update (api: IMediathecaApi) (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
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
                SteamFamilyMembers = members
                SteamNeedsReconnect = false }, Cmd.none
        | Error e ->
            { model with
                IsFetchingFamilyMembers = false
                FetchFamilyMembersResult = Some (Error e)
                SteamNeedsReconnect = isReconnectRequired e }, Cmd.none

    // ── Steam Connect (integration-hebjs): one-time "Connect Steam" QR login ──

    | Load_steam_connect_status ->
        model, Cmd.OfAsync.perform api.getSteamConnectionStatus () Steam_connect_status_loaded

    | Steam_connect_status_loaded connected ->
        { model with SteamConnected = connected }, Cmd.none

    | Start_steam_connect ->
        { model with
            IsConnectingSteam = true
            SteamConnectQrDataUrl = None
            SteamConnectError = None
            SteamNeedsReconnect = false },
        Cmd.ofEffect (fun dispatch ->
            async {
                try
                    let! response = jsFetch "/api/stream/steam-connect" |> Async.AwaitPromise
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
                                    | "qr" ->
                                        let dataUrl: string = parsed?dataUrl |> string
                                        dispatch (Steam_connect_qr_received dataUrl)
                                    | "complete" ->
                                        dispatch (Steam_connect_completed (Ok ()))
                                    | "error" ->
                                        let errorMsg: string = parsed?message |> string
                                        dispatch (Steam_connect_completed (Error errorMsg))
                                    | _ -> ()
                                idx <- buffer.IndexOf("\n\n")
                with ex ->
                    dispatch (Steam_connect_completed (Error ex.Message))
            } |> Async.StartImmediate
        )

    | Steam_connect_qr_received dataUrl ->
        { model with SteamConnectQrDataUrl = Some dataUrl }, Cmd.none

    | Steam_connect_completed result ->
        match result with
        | Ok () ->
            { model with
                IsConnectingSteam = false
                SteamConnectQrDataUrl = None
                SteamConnectError = None
                SteamConnected = true
                SteamNeedsReconnect = false }, Cmd.none
        | Error e ->
            { model with
                IsConnectingSteam = false
                SteamConnectQrDataUrl = None
                SteamConnectError = Some e }, Cmd.none

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
        let needsReconnect =
            match result with
            | Error e -> isReconnectRequired e
            | Ok _ -> false
        { model with
            IsImportingSteamFamily = false
            SteamFamilyImportResult = Some result
            ImportProgress = None
            SteamNeedsReconnect = model.SteamNeedsReconnect || needsReconnect }, Cmd.none

    // Jellyfin Integration
    | Load_jellyfin_settings ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getJellyfinServerUrl () (fun url -> Jellyfin_settings_loaded (url, ""))
            Cmd.OfAsync.perform api.getJellyfinUsername () (fun username -> Jellyfin_settings_loaded ("", username))
        ]

    | Jellyfin_settings_loaded (serverUrl, username) ->
        let m =
            if serverUrl <> "" then { model with JellyfinServerUrl = serverUrl; JellyfinServerUrlInput = serverUrl }
            elif username <> "" then { model with JellyfinUsername = username; JellyfinUsernameInput = username }
            else model
        m, Cmd.none

    | Jellyfin_server_url_input_changed value ->
        { model with JellyfinServerUrlInput = value; JellyfinTestResult = None; JellyfinSaveResult = None }, Cmd.none

    | Jellyfin_username_input_changed value ->
        { model with JellyfinUsernameInput = value; JellyfinTestResult = None; JellyfinSaveResult = None }, Cmd.none

    | Jellyfin_password_input_changed value ->
        { model with JellyfinPasswordInput = value; JellyfinTestResult = None; JellyfinSaveResult = None }, Cmd.none

    | Test_jellyfin_connection ->
        { model with IsTestingJellyfin = true; JellyfinTestResult = None },
        Cmd.OfAsync.either api.testJellyfinConnection (model.JellyfinServerUrlInput, model.JellyfinUsernameInput, model.JellyfinPasswordInput)
            Jellyfin_test_result
            (fun ex -> Jellyfin_test_result (Error ex.Message))

    | Jellyfin_test_result result ->
        let cmd =
            match result with
            | Ok _ -> Cmd.ofMsg Load_jellyfin_settings
            | Error _ -> Cmd.none
        { model with IsTestingJellyfin = false; JellyfinTestResult = Some result }, cmd

    | Save_jellyfin_settings ->
        { model with IsSavingJellyfin = true; JellyfinSaveResult = None },
        Cmd.OfAsync.either api.setJellyfinCredentials (model.JellyfinUsernameInput, model.JellyfinPasswordInput)
            Jellyfin_save_result
            (fun ex -> Jellyfin_save_result (Error ex.Message))

    | Jellyfin_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "Credentials saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_jellyfin_settings
            | Error _ -> Cmd.none
        { model with IsSavingJellyfin = false; JellyfinSaveResult = Some saveResult }, cmd

    | Scan_jellyfin_library ->
        { model with IsScanningJellyfin = true; JellyfinScanResult = None; JellyfinImportResult = None },
        Cmd.OfAsync.either api.scanJellyfinLibrary ()
            Jellyfin_scan_completed
            (fun ex -> Jellyfin_scan_completed (Error ex.Message))

    | Jellyfin_scan_completed result ->
        { model with IsScanningJellyfin = false; JellyfinScanResult = Some result }, Cmd.none

    | Import_jellyfin_watch_history ->
        { model with IsImportingJellyfin = true; JellyfinImportResult = None },
        Cmd.OfAsync.either api.importJellyfinWatchHistory ()
            Jellyfin_import_completed
            (fun ex -> Jellyfin_import_completed (Error ex.Message))

    | Jellyfin_import_completed result ->
        { model with IsImportingJellyfin = false; JellyfinImportResult = Some result; JellyfinScanResult = None }, Cmd.none

    // Sync Status
    | Load_playtime_sync_status ->
        model, Cmd.OfAsync.perform api.getPlaytimeSyncStatus () Playtime_sync_status_loaded

    | Playtime_sync_status_loaded status ->
        { model with PlaytimeSyncStatus = Some status }, Cmd.none

    | Load_jellyfin_sync_status ->
        model, Cmd.OfAsync.perform api.getJellyfinSyncStatus () Jellyfin_sync_status_loaded

    | Jellyfin_sync_status_loaded status ->
        let lastSync =
            match status with
            | SyncIdle lastTime -> lastTime
            | SyncCompleted (_, lastTime) -> Some lastTime
            | SyncFailed (_, lastTime) -> lastTime
            | SyncInProgress -> model.JellyfinLastSyncTime
        { model with JellyfinLastSyncTime = lastSync; JellyfinSyncStatus = Some status }, Cmd.none

    | Load_steam_family_last_sync ->
        model, Cmd.OfAsync.perform api.getSteamFamilyLastSync () Steam_family_last_sync_loaded

    | Steam_family_last_sync_loaded lastSync ->
        { model with SteamFamilyLastSync = lastSync }, Cmd.none

    // Administration (administration-k3vmt)
    | Admin_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.Admin.State.update adminApi childMsg model.AdminModel
        let model = { model with AdminModel = childModel }
        // Single source of truth for "this section has loaded once": flip
        // the matching flag whenever that section's own load message flows
        // through here, whether it was dispatched by a Toggle_*_section
        // handler below (first expand) or, for Projections only, by root
        // `Url_changed`'s eager `loadProjectionStatsCmd` on every /settings
        // visit. Every other admin child message is a no-op here.
        let model =
            match childMsg with
            | Mediatheca.Client.Pages.Admin.Types.Event_browser_msg Mediatheca.Client.Pages.EventBrowser.Types.Load_filter_options ->
                { model with EventsSectionLoaded = true }
            | Mediatheca.Client.Pages.Admin.Types.Health_msg Mediatheca.Client.Pages.AdminHealth.Types.Load ->
                { model with HealthSectionLoaded = true }
            | Mediatheca.Client.Pages.Admin.Types.Projections_msg Mediatheca.Client.Pages.AdminProjections.Types.Load ->
                { model with ProjectionsSectionLoaded = true }
            | Mediatheca.Client.Pages.Admin.Types.Images_msg Mediatheca.Client.Pages.AdminImages.Types.Load ->
                { model with ImagesSectionLoaded = true }
            | Mediatheca.Client.Pages.Admin.Types.Jobs_msg Mediatheca.Client.Pages.AdminJobs.Types.Load ->
                { model with JobsSectionLoaded = true }
            | Mediatheca.Client.Pages.Admin.Types.Surgery_msg Mediatheca.Client.Pages.AdminSurgery.Types.Load_backup_stats ->
                { model with SurgerySectionLoaded = true }
            | _ -> model
        model, Cmd.map Admin_msg childCmd

    | Admin_unlock_input_changed value ->
        // Unlocking is one-way for the life of this page visit: once the
        // word has been typed the box is replaced by the sections plus a
        // "Lock" button, so there is no "kept typing and it re-hid" state.
        { model with
            AdminUnlockInput = value
            AdminUnlocked = model.AdminUnlocked || matchesUnlockWord value },
        Cmd.none

    | Lock_admin_sections ->
        // Collapse everything on the way out so re-unlocking starts from the
        // same all-closed state a fresh visit gives, and stop the live-tail
        // poll via the same idempotent `stopFollowing` the collapse and
        // page-departure paths use (ADR-0023).
        { model with
            AdminUnlocked = false
            AdminUnlockInput = ""
            AdminModel = Mediatheca.Client.Pages.Admin.State.stopFollowing model.AdminModel
            EventsSectionOpen = false
            ProjectionsSectionOpen = false
            HealthSectionOpen = false
            ImagesSectionOpen = false
            JobsSectionOpen = false
            SurgerySectionOpen = false },
        Cmd.none

    | Toggle_events_section ->
        let opening = not model.EventsSectionOpen
        let model = { model with EventsSectionOpen = opening }
        if not opening then
            // Collapsing without navigating away stops the live-tail poll
            // via the same idempotent `stopFollowing` the Settings-departure
            // path (root State.Url_changed) uses — one function, two
            // triggers (ADR-0023, amended by administration-k3vmt).
            { model with
                AdminModel =
                    { model.AdminModel with
                        EventBrowserModel = Mediatheca.Client.Pages.EventBrowser.State.stopFollowing model.AdminModel.EventBrowserModel } },
            Cmd.none
        elif model.EventsSectionLoaded then
            model, Cmd.none
        else
            model, loadEventsCmd

    | Toggle_projections_section ->
        let opening = not model.ProjectionsSectionOpen
        let model = { model with ProjectionsSectionOpen = opening }
        if opening && not model.ProjectionsSectionLoaded then model, loadProjectionStatsCmd
        else model, Cmd.none

    | Toggle_health_section ->
        let opening = not model.HealthSectionOpen
        let model = { model with HealthSectionOpen = opening }
        if opening && not model.HealthSectionLoaded then model, loadHealthCmd
        else model, Cmd.none

    | Toggle_images_section ->
        let opening = not model.ImagesSectionOpen
        let model = { model with ImagesSectionOpen = opening }
        if opening && not model.ImagesSectionLoaded then model, loadImagesCmd
        else model, Cmd.none

    | Toggle_jobs_section ->
        let opening = not model.JobsSectionOpen
        let model = { model with JobsSectionOpen = opening }
        if opening && not model.JobsSectionLoaded then model, loadJobsCmd
        else model, Cmd.none

    | Toggle_surgery_section ->
        let opening = not model.SurgerySectionOpen
        let model = { model with SurgerySectionOpen = opening }
        if opening && not model.SurgerySectionLoaded then model, loadSurgeryCmd
        else model, Cmd.none

    | Go_to_projections_section ->
        // While the danger gate is locked the banner's affordance must not
        // be a way around it — a dirty projection is information, rebuilding
        // one is a destructive recovery action. So it leads to the unlock
        // box rather than to the (unrendered) Projections section.
        if not model.AdminUnlocked then
            model, focusUnlockInputCmd
        else
            { model with ProjectionsSectionOpen = true }, scrollToProjectionsCmd
