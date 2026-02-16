module Mediatheca.Client.Pages.Settings.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Components
open Mediatheca.Client

// ── Helpers ──

let private statusBadge configured (label: string) =
    Daisy.badge [
        if configured then badge.success else badge.warning
        prop.className "gap-1 text-xs"
        prop.text label
    ]

let private feedbackAlert (result: Result<string, string> option) =
    match result with
    | Some (Ok msg) -> Daisy.alert [ alert.success; prop.className "mb-2"; prop.text msg ]
    | Some (Error msg) -> Daisy.alert [ alert.error; prop.className "mb-2"; prop.text msg ]
    | None -> Html.none

// ── Integration Card Wrapper ──

let private integrationCard (icon: unit -> ReactElement) (title: string) (description: string) (badge: ReactElement) (detail: ReactElement) =
    Html.div [
        prop.className (DesignSystem.glassCard + " " + DesignSystem.cardHover + " overflow-hidden")
        prop.children [
            Html.div [
                prop.className "collapse collapse-arrow"
                prop.children [
                    Html.input [ prop.type' "checkbox" ]
                    Html.div [
                        prop.className "collapse-title p-5"
                        prop.children [
                            Html.div [
                                prop.className "flex items-start gap-4"
                                prop.children [
                                    Html.div [
                                        prop.className "p-3 rounded-xl bg-base-300/50 text-primary flex-none"
                                        prop.children [ icon () ]
                                    ]
                                    Html.div [
                                        prop.className "flex-1 min-w-0"
                                        prop.children [
                                            Html.div [
                                                prop.className "flex items-center gap-2 mb-1"
                                                prop.children [
                                                    Html.h3 [
                                                        prop.className "font-bold font-display"
                                                        prop.text title
                                                    ]
                                                    badge
                                                ]
                                            ]
                                            Html.p [
                                                prop.className DesignSystem.secondaryText
                                                prop.text description
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "collapse-content px-5 pb-5"
                        prop.children [
                            Html.div [
                                prop.className "border-t border-base-content/10 pt-4"
                                prop.children [ detail ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Per-Integration Detail Functions ──

let private tmdbDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Enter your TMDB API key to enable movie search. Get a free key at "
                    Html.a [
                        prop.href "https://www.themoviedb.org/settings/api"
                        prop.target "_blank"
                        prop.className "link link-primary"
                        prop.text "themoviedb.org"
                    ]
                    Html.text "."
                ]
            ]

            if model.TmdbApiKey <> "" then
                Html.div [
                    prop.className "mb-3"
                    prop.children [
                        Daisy.badge [
                            badge.success
                            prop.className "gap-1"
                            prop.text ("Configured: " + model.TmdbApiKey)
                        ]
                    ]
                ]

            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "API Key" ]
                        ]
                    ]
                    Daisy.input [
                        prop.type' "password"
                        prop.className "w-full"
                        prop.placeholder "Enter your TMDB API key..."
                        prop.value model.TmdbKeyInput
                        prop.onChange (Tmdb_key_input_changed >> dispatch)
                    ]
                ]
            ]

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.outline
                        if model.IsTesting then button.disabled
                        prop.onClick (fun _ -> dispatch Test_tmdb_key)
                        prop.disabled (model.TmdbKeyInput = "" || model.IsTesting)
                        prop.children [
                            if model.IsTesting then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test Connection"
                        ]
                    ]
                    Daisy.button.button [
                        button.primary
                        if model.IsSaving then button.disabled
                        prop.onClick (fun _ -> dispatch Save_tmdb_key)
                        prop.disabled (model.TmdbKeyInput = "" || model.IsSaving)
                        prop.children [
                            if model.IsSaving then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save"
                        ]
                    ]
                ]
            ]

            feedbackAlert model.TestResult
            feedbackAlert model.SaveResult
        ]
    ]

let private rawgDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Enter your RAWG API key to enable game search. Get a free key at "
                    Html.a [
                        prop.href "https://rawg.io/apidocs"
                        prop.target "_blank"
                        prop.className "link link-primary"
                        prop.text "rawg.io"
                    ]
                    Html.text "."
                ]
            ]

            if model.RawgApiKey <> "" then
                Html.div [
                    prop.className "mb-3"
                    prop.children [
                        Daisy.badge [
                            badge.success
                            prop.className "gap-1"
                            prop.text ("Configured: " + model.RawgApiKey)
                        ]
                    ]
                ]

            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "API Key" ]
                        ]
                    ]
                    Daisy.input [
                        prop.type' "password"
                        prop.className "w-full"
                        prop.placeholder "Enter your RAWG API key..."
                        prop.value model.RawgKeyInput
                        prop.onChange (Rawg_key_input_changed >> dispatch)
                    ]
                ]
            ]

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.outline
                        if model.IsTestingRawg then button.disabled
                        prop.onClick (fun _ -> dispatch Test_rawg_key)
                        prop.disabled (model.RawgKeyInput = "" || model.IsTestingRawg)
                        prop.children [
                            if model.IsTestingRawg then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test Connection"
                        ]
                    ]
                    Daisy.button.button [
                        button.primary
                        if model.IsSavingRawg then button.disabled
                        prop.onClick (fun _ -> dispatch Save_rawg_key)
                        prop.disabled (model.RawgKeyInput = "" || model.IsSavingRawg)
                        prop.children [
                            if model.IsSavingRawg then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save"
                        ]
                    ]
                ]
            ]

            feedbackAlert model.RawgTestResult
            feedbackAlert model.RawgSaveResult
        ]
    ]

let private steamDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Connect your Steam account to import your game library. Get an API key at "
                    Html.a [
                        prop.href "https://steamcommunity.com/dev/apikey"
                        prop.target "_blank"
                        prop.className "link link-primary"
                        prop.text "steamcommunity.com"
                    ]
                    Html.text "."
                ]
            ]

            // Steam API Key input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Steam Web API Key" ]
                        ]
                    ]
                    Daisy.input [
                        prop.type' "password"
                        prop.className "w-full"
                        prop.placeholder "Enter your Steam Web API key..."
                        prop.value model.SteamKeyInput
                        prop.onChange (Steam_key_input_changed >> dispatch)
                    ]
                ]
            ]

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.outline
                        if model.IsTestingSteam then button.disabled
                        prop.onClick (fun _ -> dispatch Test_steam_key)
                        prop.disabled (model.SteamKeyInput = "" || model.IsTestingSteam)
                        prop.children [
                            if model.IsTestingSteam then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test Connection"
                        ]
                    ]
                    Daisy.button.button [
                        button.primary
                        if model.IsSavingSteam then button.disabled
                        prop.onClick (fun _ -> dispatch Save_steam_key)
                        prop.disabled (model.SteamKeyInput = "" || model.IsSavingSteam)
                        prop.children [
                            if model.IsSavingSteam then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save"
                        ]
                    ]
                ]
            ]

            feedbackAlert model.SteamTestResult
            feedbackAlert model.SteamSaveResult

            // Steam ID input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Steam ID (SteamID64)" ]
                        ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "Enter your SteamID64 (e.g. 76561198012345678)..."
                        prop.value model.SteamIdInput
                        prop.onChange (Steam_id_input_changed >> dispatch)
                    ]
                ]
            ]

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        if model.IsSavingSteamId then button.disabled
                        prop.onClick (fun _ -> dispatch Save_steam_id)
                        prop.disabled (model.SteamIdInput = "" || model.IsSavingSteamId)
                        prop.children [
                            if model.IsSavingSteamId then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save Steam ID"
                        ]
                    ]
                ]
            ]

            feedbackAlert model.SteamIdSaveResult

            // Vanity URL resolver
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Resolve Vanity URL" ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex gap-2"
                        prop.children [
                            Daisy.input [
                                prop.className "flex-1"
                                prop.placeholder "Steam custom URL name..."
                                prop.value model.VanityInput
                                prop.onChange (Vanity_input_changed >> dispatch)
                            ]
                            Daisy.button.button [
                                button.outline
                                if model.IsResolvingVanity then button.disabled
                                prop.onClick (fun _ -> dispatch Resolve_vanity_url)
                                prop.disabled (model.VanityInput = "" || model.IsResolvingVanity)
                                prop.children [
                                    if model.IsResolvingVanity then
                                        Daisy.loading [ loading.spinner; loading.sm ]
                                    Html.text "Resolve"
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            match model.VanityResult with
            | Some (Ok steamId) ->
                Daisy.alert [ alert.success; prop.className "mb-2"; prop.text (sprintf "Resolved to: %s (filled in above)" steamId) ]
            | Some (Error msg) ->
                Daisy.alert [ alert.error; prop.className "mb-2"; prop.text msg ]
            | None -> Html.none

            // Import Steam Library button
            Html.div [
                prop.className "mb-4 mt-4"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        if model.IsImportingSteam then button.disabled
                        prop.onClick (fun _ -> dispatch Import_steam_library)
                        prop.disabled (model.SteamApiKey = "" || model.SteamId = "" || model.IsImportingSteam)
                        prop.children [
                            if model.IsImportingSteam then
                                Daisy.loading [ loading.spinner; loading.sm ]
                                Html.text "Importing..."
                            else
                                Html.text "Import My Steam Library"
                        ]
                    ]
                ]
            ]

            // Steam import result
            match model.SteamImportResult with
            | Some (Ok result) ->
                Daisy.alert [
                    alert.success
                    prop.className "mb-4"
                    prop.children [
                        Html.div [
                            Html.p [ prop.className "font-bold"; prop.text "Steam import completed!" ]
                            Html.ul [
                                prop.className "mt-2 text-sm space-y-1"
                                prop.children [
                                    Html.li [ prop.text (sprintf "Games matched: %d" result.GamesMatched) ]
                                    Html.li [ prop.text (sprintf "Games created: %d" result.GamesCreated) ]
                                    Html.li [ prop.text (sprintf "Play time updated: %d" result.PlayTimeUpdated) ]
                                ]
                            ]
                            if not (List.isEmpty result.Errors) then
                                Html.div [
                                    prop.className "mt-2"
                                    prop.children [
                                        Html.p [ prop.className "font-bold text-warning"; prop.text (sprintf "Warnings (%d):" result.Errors.Length) ]
                                        Html.ul [
                                            prop.className "text-sm text-warning"
                                            prop.children (
                                                result.Errors |> List.truncate 10 |> List.map (fun err ->
                                                    Html.li [ prop.text err ])
                                            )
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            | Some (Error msg) ->
                Daisy.alert [ alert.error; prop.className "mb-4"; prop.text msg ]
            | None -> Html.none
        ]
    ]

let private steamFamilyDetail (model: Model) (dispatch: Msg -> unit) =
    let hasMappedMembers =
        model.SteamFamilyMembers |> List.exists (fun m -> m.FriendSlug.IsSome)

    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.text "Import shared library from your Steam Family group. First discover members, map them to friends, then import."
            ]

            // Collapsible help: how to get the token
            Html.div [
                prop.className "collapse collapse-arrow bg-base-200/50 mb-4 rounded-lg"
                prop.children [
                    Html.input [ prop.type' "checkbox" ]
                    Html.div [
                        prop.className "collapse-title text-sm font-medium"
                        prop.text "How to get the access token"
                    ]
                    Html.div [
                        prop.className "collapse-content text-sm text-base-content/70"
                        prop.children [
                            Html.ol [
                                prop.className "list-decimal list-inside space-y-1"
                                prop.children [
                                    Html.li [ prop.text "Log into Steam at store.steampowered.com" ]
                                    Html.li [ prop.text "Open browser DevTools (F12) and switch to the Network tab" ]
                                    Html.li [
                                        prop.children [
                                            Html.text "Visit your Family page (Store "
                                            Html.span [ prop.className "mx-1"; prop.text "\u2192" ]
                                            Html.text "Your Store "
                                            Html.span [ prop.className "mx-1"; prop.text "\u2192" ]
                                            Html.text "Family)"
                                        ]
                                    ]
                                    Html.li [
                                        prop.children [
                                            Html.text "Filter network requests for "
                                            Html.code [ prop.className "badge badge-ghost badge-sm"; prop.text "IFamilyGroupsService" ]
                                        ]
                                    ]
                                    Html.li [
                                        prop.children [
                                            Html.text "Copy the "
                                            Html.code [ prop.className "badge badge-ghost badge-sm"; prop.text "access_token=..." ]
                                            Html.text " value from any matching request URL"
                                        ]
                                    ]
                                    Html.li [ prop.text "Note: tokens expire within ~1 hour" ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            // Family access token input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Steam Family Access Token" ]
                        ]
                    ]
                    Daisy.input [
                        prop.type' "password"
                        prop.className "w-full"
                        prop.placeholder "Paste your Steam access token..."
                        prop.value model.SteamFamilyTokenInput
                        prop.onChange (Steam_family_token_input_changed >> dispatch)
                    ]
                ]
            ]

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        if model.IsSavingFamilyToken then button.disabled
                        prop.onClick (fun _ -> dispatch Save_steam_family_token)
                        prop.disabled (model.SteamFamilyTokenInput = "" || model.IsSavingFamilyToken)
                        prop.children [
                            if model.IsSavingFamilyToken then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save Token"
                        ]
                    ]
                ]
            ]

            feedbackAlert model.FamilyTokenSaveResult

            // ── Step 1: Fetch Family Members ──
            Html.div [
                prop.className "border-t border-base-content/10 pt-4 mt-4 mb-4"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-2 mb-3"
                        prop.children [
                            Daisy.badge [ prop.className "badge-neutral badge-sm font-mono"; prop.text "1" ]
                            Html.h4 [
                                prop.className "text-sm font-bold"
                                prop.text "Discover Family Members"
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "flex gap-2 mb-3"
                        prop.children [
                            Daisy.button.button [
                                button.outline
                                if model.IsFetchingFamilyMembers then button.disabled
                                prop.onClick (fun _ -> dispatch Fetch_steam_family_members)
                                prop.disabled (model.SteamFamilyToken = "" || model.IsFetchingFamilyMembers)
                                prop.children [
                                    if model.IsFetchingFamilyMembers then
                                        Daisy.loading [ loading.spinner; loading.sm ]
                                        Html.text "Fetching..."
                                    else
                                        Html.text "Fetch Family Members"
                                ]
                            ]
                        ]
                    ]

                    feedbackAlert model.FetchFamilyMembersResult

                    // Family member mapping table
                    if not (List.isEmpty model.SteamFamilyMembers) then
                        Html.div [
                            prop.className "mb-4"
                            prop.children [
                                Html.h4 [
                                    prop.className "text-sm font-bold mb-2"
                                    prop.text "Map Members to Friends"
                                ]
                                Html.div [
                                    prop.className "space-y-2"
                                    prop.children (
                                        model.SteamFamilyMembers |> List.map (fun m ->
                                            Html.div [
                                                prop.className "flex items-center gap-2"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "text-sm min-w-[120px]"
                                                        prop.text (if m.DisplayName <> "" then m.DisplayName else m.SteamId)
                                                    ]
                                                    Html.span [
                                                        prop.className "text-base-content/40 text-sm"
                                                        prop.text "\u2192"
                                                    ]
                                                    Daisy.select [
                                                        prop.className "select-sm"
                                                        prop.value (m.FriendSlug |> Option.defaultValue "")
                                                        prop.onChange (fun (v: string) ->
                                                            let slug = if v = "" then None else Some v
                                                            dispatch (Update_family_member_friend (m.SteamId, slug)))
                                                        prop.children [
                                                            Html.option [ prop.value ""; prop.text "-- No mapping --" ]
                                                            yield! model.Friends |> List.map (fun f ->
                                                                Html.option [ prop.value f.Slug; prop.text f.Name ])
                                                        ]
                                                    ]
                                                ]
                                            ])
                                    )
                                ]
                                Html.div [
                                    prop.className "mt-3"
                                    prop.children [
                                        Daisy.button.button [
                                            button.outline
                                            button.sm
                                            prop.onClick (fun _ -> dispatch Save_steam_family_members)
                                            prop.text "Save Mappings"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                ]
            ]

            // ── Step 2: Import Family Library ──
            Html.div [
                prop.className "border-t border-base-content/10 pt-4 mb-4"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-2 mb-3"
                        prop.children [
                            Daisy.badge [ prop.className "badge-neutral badge-sm font-mono"; prop.text "2" ]
                            Html.h4 [
                                prop.className "text-sm font-bold"
                                prop.text "Import Family Library"
                            ]
                        ]
                    ]

                    if List.isEmpty model.SteamFamilyMembers then
                        Html.p [
                            prop.className "text-base-content/50 text-sm mb-3"
                            prop.text "Fetch family members first to enable import."
                        ]
                    elif not hasMappedMembers then
                        Html.p [
                            prop.className "text-base-content/50 text-sm mb-3"
                            prop.text "Map at least one family member to a friend to enable import."
                        ]

                    if not model.IsImportingSteamFamily && model.SteamFamilyImportResult.IsNone then
                        Html.div [
                            prop.className "mb-4"
                            prop.children [
                                Daisy.button.button [
                                    button.primary
                                    prop.onClick (fun _ -> dispatch Import_steam_family)
                                    prop.disabled (
                                        model.SteamFamilyToken = ""
                                        || List.isEmpty model.SteamFamilyMembers
                                        || not hasMappedMembers)
                                    prop.text "Import Family Library"
                                ]
                            ]
                        ]

                    // Live progress during import
                    if model.IsImportingSteamFamily then
                        Html.div [
                            prop.className "mb-4 space-y-3"
                            prop.children [
                                match model.ImportProgress with
                                | Some progress ->
                                    // Progress bar
                                    Html.div [
                                        prop.className "space-y-1"
                                        prop.children [
                                            Html.div [
                                                prop.className "flex justify-between text-xs text-base-content/70"
                                                prop.children [
                                                    Html.span [ prop.text (sprintf "%d / %d games" progress.Current progress.Total) ]
                                                    Html.span [ prop.text (sprintf "%d%%" (if progress.Total > 0 then progress.Current * 100 / progress.Total else 0)) ]
                                                ]
                                            ]
                                            Daisy.progress [
                                                prop.className "progress-primary w-full"
                                                prop.value (if progress.Total > 0 then progress.Current * 100 / progress.Total else 0)
                                                prop.max 100
                                            ]
                                        ]
                                    ]
                                    // Current game
                                    Html.div [
                                        prop.className "flex items-center gap-2 text-sm"
                                        prop.children [
                                            Daisy.loading [ loading.spinner; loading.xs ]
                                            Html.span [
                                                prop.className "text-base-content/70"
                                                prop.text (sprintf "Processing: %s..." progress.GameName)
                                            ]
                                        ]
                                    ]
                                | None ->
                                    Html.div [
                                        prop.className "flex items-center gap-2 text-sm"
                                        prop.children [
                                            Daisy.loading [ loading.spinner; loading.sm ]
                                            Html.span [ prop.text "Starting import..." ]
                                        ]
                                    ]

                                // Scrolling log
                                if not (List.isEmpty model.ImportLog) then
                                    Html.div [
                                        prop.className "bg-base-200/50 rounded-lg p-3 max-h-48 overflow-y-auto text-xs font-mono space-y-0.5"
                                        prop.children (
                                            model.ImportLog |> List.map (fun (name, action) ->
                                                let actionColor =
                                                    match action with
                                                    | "Matched" -> "text-base-content/70"
                                                    | "Matched by name" -> "text-info"
                                                    | "Created" -> "text-success"
                                                    | "Skipped" | "Error" -> "text-warning"
                                                    | _ -> "text-base-content/50"
                                                Html.div [
                                                    prop.className "flex gap-2"
                                                    prop.children [
                                                        Html.span [ prop.className "text-base-content/50 truncate max-w-[200px]"; prop.text name ]
                                                        Html.span [ prop.className "text-base-content/30"; prop.text "\u2192" ]
                                                        Html.span [ prop.className actionColor; prop.text action ]
                                                    ]
                                                ]
                                            )
                                        )
                                    ]
                            ]
                        ]

                    // Steam Family import result
                    match model.SteamFamilyImportResult with
                    | Some (Ok result) ->
                        Html.div [
                            prop.className "space-y-3 mb-4"
                            prop.children [
                                Daisy.alert [
                                    alert.success
                                    prop.children [
                                        Html.div [
                                            Html.p [ prop.className "font-bold"; prop.text "Family import completed!" ]
                                            Html.ul [
                                                prop.className "mt-2 text-sm space-y-1"
                                                prop.children [
                                                    Html.li [ prop.text (sprintf "Family members: %d" result.FamilyMembers) ]
                                                    Html.li [ prop.text (sprintf "Games processed: %d" result.GamesProcessed) ]
                                                    Html.li [ prop.text (sprintf "Games created: %d" result.GamesCreated) ]
                                                    Html.li [ prop.text (sprintf "Family owners set: %d" result.FamilyOwnersSet) ]
                                                ]
                                            ]
                                            if not (List.isEmpty result.Errors) then
                                                Html.div [
                                                    prop.className "mt-2"
                                                    prop.children [
                                                        Html.p [ prop.className "font-bold text-warning"; prop.text (sprintf "Warnings (%d):" result.Errors.Length) ]
                                                        Html.ul [
                                                            prop.className "text-sm text-warning"
                                                            prop.children (
                                                                result.Errors |> List.truncate 10 |> List.map (fun err ->
                                                                    Html.li [ prop.text err ])
                                                            )
                                                        ]
                                                    ]
                                                ]
                                        ]
                                    ]
                                ]
                                // Completed log
                                if not (List.isEmpty model.ImportLog) then
                                    Html.div [
                                        prop.className "bg-base-200/50 rounded-lg p-3 max-h-48 overflow-y-auto text-xs font-mono space-y-0.5"
                                        prop.children (
                                            model.ImportLog |> List.map (fun (name, action) ->
                                                let actionColor =
                                                    match action with
                                                    | "Matched" -> "text-base-content/70"
                                                    | "Matched by name" -> "text-info"
                                                    | "Created" -> "text-success"
                                                    | "Skipped" | "Error" -> "text-warning"
                                                    | _ -> "text-base-content/50"
                                                Html.div [
                                                    prop.className "flex gap-2"
                                                    prop.children [
                                                        Html.span [ prop.className "text-base-content/50 truncate max-w-[200px]"; prop.text name ]
                                                        Html.span [ prop.className "text-base-content/30"; prop.text "\u2192" ]
                                                        Html.span [ prop.className actionColor; prop.text action ]
                                                    ]
                                                ]
                                            )
                                        )
                                    ]
                            ]
                        ]
                    | Some (Error msg) ->
                        Daisy.alert [ alert.error; prop.className "mb-4"; prop.text msg ]
                    | None -> Html.none
                ]
            ]
        ]
    ]

let private cinemarcoDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.text "Import your complete Cinemarco library into Mediatheca."
            ]

            Daisy.alert [
                alert.warning
                prop.className "mb-4"
                prop.text "This will only work on a fresh (empty) Mediatheca database. All data from Cinemarco will be migrated as Mediatheca events."
            ]

            // Database path input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Cinemarco Database Path" ]
                        ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "/path/to/cinemarco/cinemarco.db"
                        prop.value model.CinemarcoDbPath
                        prop.onChange (Cinemarco_db_path_changed >> dispatch)
                        prop.disabled model.IsImporting
                    ]
                ]
            ]

            // Images path input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [
                            Html.span [ prop.className "label-text"; prop.text "Cinemarco Images Path" ]
                        ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "/path/to/cinemarco/images/"
                        prop.value model.CinemarcoImagesPath
                        prop.onChange (Cinemarco_images_path_changed >> dispatch)
                        prop.disabled model.IsImporting
                    ]
                ]
            ]

            // Import button
            Html.div [
                prop.className "mb-4"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        if model.IsImporting then button.disabled
                        prop.onClick (fun _ -> dispatch Start_cinemarco_import)
                        prop.disabled (model.CinemarcoDbPath = "" || model.CinemarcoImagesPath = "" || model.IsImporting)
                        prop.children [
                            if model.IsImporting then
                                Daisy.loading [ loading.spinner; loading.sm ]
                                Html.text "Importing..."
                            else
                                Html.text "Import"
                        ]
                    ]
                ]
            ]

            // Import result
            match model.ImportResult with
            | Some (Ok result) ->
                Daisy.alert [
                    alert.success
                    prop.className "mb-4"
                    prop.children [
                        Html.div [
                            Html.p [ prop.className "font-bold"; prop.text "Import completed successfully!" ]
                            Html.ul [
                                prop.className "mt-2 text-sm space-y-1"
                                prop.children [
                                    Html.li [ prop.text (sprintf "Friends: %d" result.FriendsImported) ]
                                    Html.li [ prop.text (sprintf "Movies: %d" result.MoviesImported) ]
                                    Html.li [ prop.text (sprintf "Series: %d" result.SeriesImported) ]
                                    Html.li [ prop.text (sprintf "Episodes watched: %d" result.EpisodesWatched) ]
                                    Html.li [ prop.text (sprintf "Catalogs: %d" result.CatalogsImported) ]
                                    Html.li [ prop.text (sprintf "Content blocks: %d" result.ContentBlocksImported) ]
                                    Html.li [ prop.text (sprintf "Images copied: %d" result.ImagesCopied) ]
                                ]
                            ]
                            if not (List.isEmpty result.Errors) then
                                Html.div [
                                    prop.className "mt-2"
                                    prop.children [
                                        Html.p [ prop.className "font-bold text-warning"; prop.text (sprintf "Warnings (%d):" result.Errors.Length) ]
                                        Html.ul [
                                            prop.className "text-sm text-warning"
                                            prop.children (
                                                result.Errors |> List.map (fun err ->
                                                    Html.li [ prop.text err ])
                                            )
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            | Some (Error msg) ->
                Daisy.alert [ alert.error; prop.className "mb-4"; prop.text msg ]
            | None -> Html.none
        ]
    ]

// ── Jellyfin Detail ──

let private jellyfinDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Connect to your Jellyfin server to sync watch history. Enter your server URL and credentials below."
                ]
            ]

            if model.JellyfinServerUrl <> "" then
                Html.div [
                    prop.className "mb-3 flex items-center gap-2 text-sm text-base-content/60"
                    prop.children [
                        Html.span [ prop.text "Server:" ]
                        Html.span [ prop.className "font-mono"; prop.text model.JellyfinServerUrl ]
                    ]
                ]

            if model.JellyfinUsername <> "" then
                Html.div [
                    prop.className "mb-3 flex items-center gap-2 text-sm text-base-content/60"
                    prop.children [
                        Html.span [ prop.text "User:" ]
                        Html.span [ prop.className "font-mono"; prop.text model.JellyfinUsername ]
                    ]
                ]

            // Server URL input
            Html.div [
                prop.className "form-control mb-3"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [ Html.span [ prop.className "label-text"; prop.text "Server URL" ] ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "http://your-server:8096"
                        prop.value model.JellyfinServerUrlInput
                        prop.onChange (Jellyfin_server_url_input_changed >> dispatch)
                    ]
                ]
            ]

            // Username input
            Html.div [
                prop.className "form-control mb-3"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [ Html.span [ prop.className "label-text"; prop.text "Username" ] ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "admin"
                        prop.value model.JellyfinUsernameInput
                        prop.onChange (Jellyfin_username_input_changed >> dispatch)
                    ]
                ]
            ]

            // Password input
            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [ Html.span [ prop.className "label-text"; prop.text "Password" ] ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.type' "password"
                        prop.placeholder "password"
                        prop.value model.JellyfinPasswordInput
                        prop.onChange (Jellyfin_password_input_changed >> dispatch)
                    ]
                ]
            ]

            feedbackAlert (model.JellyfinTestResult |> Option.map (Result.mapError id))
            feedbackAlert model.JellyfinSaveResult

            // Buttons
            Html.div [
                prop.className "flex gap-2"
                prop.children [
                    Daisy.button.button [
                        button.primary
                        button.sm
                        if model.IsTestingJellyfin then button.disabled
                        prop.onClick (fun _ -> dispatch Test_jellyfin_connection)
                        prop.children [
                            if model.IsTestingJellyfin then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test & Save"
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Main View ──

let view (model: Model) (dispatch: Msg -> unit) =
    let steamConfigured =
        model.SteamApiKey <> "" && model.SteamId <> ""
    let steamPartial =
        (model.SteamApiKey <> "" || model.SteamId <> "") && not steamConfigured

    Html.div [
        prop.className (DesignSystem.pagePadding + " " + DesignSystem.animateFadeIn)
        prop.children [
            // Page header
            Html.div [
                prop.className "flex items-center gap-3 mb-8"
                prop.children [
                    Html.div [
                        prop.className "p-3 rounded-xl bg-base-300/50 text-primary"
                        prop.children [ Icons.settings () ]
                    ]
                    Html.div [
                        prop.children [
                            Html.h1 [
                                prop.className "text-2xl font-bold font-display"
                                prop.text "Settings"
                            ]
                            Html.p [
                                prop.className DesignSystem.secondaryText
                                prop.text "Manage integrations and data imports."
                            ]
                        ]
                    ]
                ]
            ]

            // Integrations section
            Html.h2 [
                prop.className (DesignSystem.subtitle + " text-base-content/50 mb-4")
                prop.text "Integrations"
            ]
            Html.div [
                prop.className ("grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 " + DesignSystem.staggerGrid)
                prop.children [
                    integrationCard
                        Icons.movie
                        "TMDB"
                        "Movie search and metadata"
                        (statusBadge (model.TmdbApiKey <> "") (if model.TmdbApiKey <> "" then "Connected" else "Not configured"))
                        (tmdbDetail model dispatch)

                    integrationCard
                        Icons.gamepad
                        "RAWG"
                        "Game search and metadata"
                        (statusBadge (model.RawgApiKey <> "") (if model.RawgApiKey <> "" then "Connected" else "Not configured"))
                        (rawgDetail model dispatch)

                    integrationCard
                        Icons.trophy
                        "Steam"
                        "Game library and play time import"
                        (statusBadge (steamConfigured || steamPartial) (if steamConfigured then "Connected" elif steamPartial then "Partial" else "Not configured"))
                        (steamDetail model dispatch)

                    integrationCard
                        Icons.tv
                        "Jellyfin"
                        "Watch history sync from media server"
                        (statusBadge (model.JellyfinServerUrl <> "" && model.JellyfinUsername <> "") (if model.JellyfinServerUrl <> "" && model.JellyfinUsername <> "" then "Connected" else "Not configured"))
                        (jellyfinDetail model dispatch)
                ]
            ]

            // Data Imports section
            Html.h2 [
                prop.className (DesignSystem.subtitle + " text-base-content/50 mb-4 mt-8")
                prop.text "Data Imports"
            ]
            Html.div [
                prop.className ("grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 " + DesignSystem.staggerGrid)
                prop.children [
                    let familyBadgeLabel =
                        if model.SteamFamilyMembers |> List.exists (fun m -> m.FriendSlug.IsSome) then "Ready"
                        elif not (List.isEmpty model.SteamFamilyMembers) then "Members fetched"
                        elif model.SteamFamilyToken <> "" then "Token set"
                        else "Not configured"
                    let familyConfigured = familyBadgeLabel = "Ready"

                    integrationCard
                        Icons.friends
                        "Steam Family"
                        "Shared family library import"
                        (statusBadge familyConfigured familyBadgeLabel)
                        (steamFamilyDetail model dispatch)

                    integrationCard
                        Icons.catalog
                        "Cinemarco"
                        "One-time legacy data migration"
                        (Daisy.badge [ badge.info; prop.className "gap-1 text-xs"; prop.text "Available" ])
                        (cinemarcoDetail model dispatch)
                ]
            ]
        ]
    ]
