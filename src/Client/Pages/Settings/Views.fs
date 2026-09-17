module Mediatheca.Client.Pages.Settings.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Components
open Mediatheca.Client

// ── Helpers ──

let private formatRelativeTime (isoTime: string) =
    try
        let dt = System.DateTime.Parse(isoTime)
        let now = System.DateTime.UtcNow
        let diff = now - dt
        if diff.TotalSeconds < 60.0 then "just now"
        elif diff.TotalMinutes < 60.0 then sprintf "%d minute%s ago" (int diff.TotalMinutes) (if int diff.TotalMinutes = 1 then "" else "s")
        elif diff.TotalHours < 24.0 then sprintf "%d hour%s ago" (int diff.TotalHours) (if int diff.TotalHours = 1 then "" else "s")
        elif diff.TotalDays < 2.0 then "yesterday"
        else sprintf "%d days ago" (int diff.TotalDays)
    with _ -> isoTime

let private lastSyncLabel (lastSync: string option) =
    match lastSync with
    | None ->
        Html.span [
            prop.className "text-sm text-base-content/50"
            prop.text "Never synced"
        ]
    | Some isoTime ->
        Html.span [
            prop.className "text-sm text-base-content/50"
            prop.title isoTime
            prop.text (sprintf "Last synced: %s" (formatRelativeTime isoTime))
        ]

/// integration-n3vqa: "N new since ..." — the arrivals list an incremental
/// family import (or its persisted last-result, after a reload) reports.
/// Shared by the fresh-completion panel and the "last import" reload panel
/// so the two never drift in shape.
let private arrivalsView (arrivals: SteamFamilyArrival list) (sinceLastSync: string option) =
    if List.isEmpty arrivals then Html.none
    else
        let sinceLabel =
            match sinceLastSync with
            | Some iso -> sprintf "since %s" (formatRelativeTime iso)
            | None -> "on this first import"
        Html.div [
            prop.className "mt-2"
            prop.children [
                Html.p [
                    prop.className "font-bold"
                    prop.text (sprintf "%d new %s:" arrivals.Length sinceLabel)
                ]
                Html.ul [
                    prop.className "mt-1 text-sm space-y-0.5"
                    prop.children (
                        arrivals |> List.map (fun a ->
                            Html.li [
                                prop.children [
                                    Html.span [ prop.className "font-medium"; prop.text a.Name ]
                                    match a.AcquiredDate with
                                    | Some d -> Html.span [ prop.className "text-base-content/50 font-mono text-xs ml-2"; prop.text d ]
                                    | None -> Html.none
                                    match a.AddedBy with
                                    | Some who -> Html.span [ prop.className "text-base-content/50 text-xs ml-2"; prop.text (sprintf "— added by %s" who) ]
                                    | None -> Html.none
                                ]
                            ])
                    )
                ]
            ]
        ]

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

/// Failure panel for a persisted SyncFailed status. Surfaces the persisted
/// error message (already includes per-item counts) plus the last-run time
/// when present. Uses the velvet-card page-chrome surface (DesignSystem.velvetCard)
/// with an error accent.
let private syncFailurePanel (error: string) (lastTime: string option) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " border-error/40 bg-error/10 p-4 mb-4")
        prop.children [
            Html.div [
                prop.className "flex items-center gap-2 mb-1"
                prop.children [
                    Html.span [
                        prop.className "font-display uppercase tracking-wider text-sm text-error"
                        prop.text "Last sync failed"
                    ]
                ]
            ]
            Html.p [
                prop.className "text-sm text-base-content/80 whitespace-pre-wrap break-words"
                prop.text error
            ]
            match lastTime with
            | Some isoTime ->
                Html.span [
                    prop.className (DesignSystem.mutedText + " block mt-2")
                    prop.title isoTime
                    prop.text (sprintf "Failed run: %s" (formatRelativeTime isoTime))
                ]
            | None ->
                Html.span [
                    prop.className (DesignSystem.mutedText + " block mt-2")
                    prop.text "No previous successful sync recorded"
                ]
        ]
    ]

/// Renders the Jellyfin sync status: only SyncFailed produces visible output
/// (the new failure surfacing). All other states defer to the existing
/// last-synced label and stay visually unchanged.
let private jellyfinSyncStatusView (status: JellyfinSyncStatus option) =
    match status with
    | Some (SyncFailed (error, lastTime)) -> syncFailurePanel error lastTime
    | _ -> Html.none

// ── Integration Card Wrapper ──

let private integrationCard (icon: unit -> ReactElement) (title: string) (description: string) (badge: ReactElement) (detail: ReactElement) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " overflow-hidden")
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

// ── Administration Section Wrapper ──
//
// Same `.collapse.collapse-arrow` shell + classes as `integrationCard`
// above, but CONTROLLED rather than uncontrolled (administration-k3vmt):
// lazy loading and "collapsing stops the live-tail poll" both need the
// open/closed state to live in the model, which DaisyUI's bare
// `<input type="checkbox">` idiom can't give us. `sectionId` is the scroll
// target the dirty banner's "Go to Projections" affordance targets.
//
// Deliberately NOT `DesignSystem.velvetCard` (`.velvet-card`) here, even
// though that's the class every other page-chrome card in this file uses:
// the Surgery section's own content nests three more `.velvet-card` panels
// (`AdminSurgery/Views.fs`'s `sectionCard`, e.g. "Edit event") inside this
// wrapper, and `tests/e2e/admin-surgery.spec.ts`'s `panelCard` helper
// locates them via `.velvet-card` + a heading filter — stacking the same
// class two levels deep make that locator ambiguous (it would match both
// the outer Surgery wrapper and each inner panel, since `.filter({has})`
// matches any ancestor whose subtree contains the heading). The card look
// is reproduced with the same underlying design tokens instead of the class
// name, so it stays visually identical.
let private adminSectionCard (sectionId: string) (title: string) (description: string) (isOpen: bool) (onToggle: unit -> unit) (content: ReactElement) =
    Html.div [
        prop.id sectionId
        prop.className ("bg-base-100 rounded-[var(--radius-card)] shadow-[var(--shadow-card)] overflow-hidden")
        prop.children [
            Html.div [
                prop.className "collapse collapse-arrow"
                prop.children [
                    Html.input [
                        prop.type' "checkbox"
                        prop.isChecked isOpen
                        prop.onChange (fun (_: bool) -> onToggle ())
                    ]
                    Html.div [
                        prop.className "collapse-title p-5"
                        prop.children [
                            Html.h3 [
                                prop.className "font-bold font-display"
                                prop.text title
                            ]
                            Html.p [
                                prop.className DesignSystem.secondaryText
                                prop.text description
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "collapse-content px-5 pb-5"
                        prop.children [
                            Html.div [
                                prop.className "border-t border-base-content/10 pt-4"
                                prop.children [ content ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Administration Danger Gate ──
//
// The six administration sections carry the app's destructive, event-sourced
// recovery actions (projection rebuild, image purge, raw event surgery).
// ADR-0034 guards each of those with its own typed confirm, but that only
// fires once the operator has already clicked — and the sections sat one
// stray click away on a page visited for ordinary things like pasting a TMDB
// key. This gate moves the deliberateness one step earlier: nothing below
// renders at all until the word "danger" is typed. It is a speed bump, not a
// secret — no auth, no persistence, no server involvement (this is a
// single-user app; the threat model is the operator's own misclick).
let private adminUnlockGate (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-5 max-w-md")
        prop.children [
            Html.p [
                prop.className (DesignSystem.secondaryText + " mb-3")
                prop.text "These controls rebuild projections, purge caches and rewrite the raw event log. Type danger to reveal them."
            ]
            Html.div [
                prop.className "form-control"
                prop.children [
                    Daisy.input [
                        prop.id Mediatheca.Client.Pages.Settings.State.adminUnlockInputElementId
                        prop.type' "text"
                        prop.className "w-full"
                        prop.autoComplete "off"
                        prop.placeholder "danger"
                        prop.ariaLabel "Type danger to reveal the administration sections"
                        prop.value model.AdminUnlockInput
                        prop.onChange (Admin_unlock_input_changed >> dispatch)
                    ]
                ]
            ]
        ]
    ]

// The six administration sections themselves — rendered only once the
// danger gate above is unlocked (`adminUnlockGate`), in the /admin
// console's former tab order.
let private adminSections (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className ("flex flex-col gap-4 " + DesignSystem.animateFadeIn)
        prop.children [
            adminSectionCard
                "settings-admin-events"
                "Events"
                "Search and inspect the event store, including live-tail follow."
                model.EventsSectionOpen
                (fun () -> dispatch Toggle_events_section)
                (Mediatheca.Client.Pages.Admin.Views.eventsSection model.AdminModel (Admin_msg >> dispatch))

            adminSectionCard
                Mediatheca.Client.Pages.Settings.State.projectionsSectionElementId
                "Projections"
                "Checkpoint/lag/row counts per projection, rebuild controls, drift check, event log backup."
                model.ProjectionsSectionOpen
                (fun () -> dispatch Toggle_projections_section)
                (Mediatheca.Client.Pages.Admin.Views.projectionsSection model.AdminModel (Admin_msg >> dispatch))

            adminSectionCard
                "settings-admin-health"
                "Health"
                "Store-wide diagnostics: event counts, activity, storage sizes, unknown-event report."
                model.HealthSectionOpen
                (fun () -> dispatch Toggle_health_section)
                (Mediatheca.Client.Pages.Admin.Views.healthSection model.AdminModel (Admin_msg >> dispatch))

            adminSectionCard
                "settings-admin-images"
                "Images"
                "Image cache stats, orphan detection, and purge."
                model.ImagesSectionOpen
                (fun () -> dispatch Toggle_images_section)
                (Mediatheca.Client.Pages.Admin.Views.imagesSection model.AdminModel (Admin_msg >> dispatch))

            adminSectionCard
                "settings-admin-jobs"
                "Jobs"
                "Scheduled job history and manual triggers."
                model.JobsSectionOpen
                (fun () -> dispatch Toggle_jobs_section)
                (Mediatheca.Client.Pages.Admin.Views.jobsSection model.AdminModel (Admin_msg >> dispatch))

            adminSectionCard
                "settings-admin-surgery"
                "Surgery"
                "Raw event-log escape hatch: edit, delete, rename event types."
                model.SurgerySectionOpen
                (fun () -> dispatch Toggle_surgery_section)
                (Mediatheca.Client.Pages.Admin.Views.surgerySection model.AdminModel (Admin_msg >> dispatch))
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
            Html.div [
                prop.className "mb-4"
                prop.children [
                    lastSyncLabel (model.PlaytimeSyncStatus |> Option.bind (fun s -> s.LastSyncTime))
                ]
            ]

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

            // Web API key rejected notice (integration-r8kwd): a standing
            // alert distinct from the Steam Family "token rejected" prompt
            // below (steamFamilyDetail) -- this is the *other* Steam
            // credential (the Web API key, `key=`), which Valve can revoke
            // independently of the pasted family token (e.g. as part of an
            // "account possibly compromised" flag). Cleared once the key is
            // saved or tested successfully.
            match model.SteamApiKeyLastError with
            | Some lastError ->
                Daisy.alert [
                    alert.warning
                    prop.className "mb-4"
                    prop.children [
                        Html.span [
                            prop.className "text-sm"
                            prop.text lastError
                        ]
                    ]
                ]
            | None -> Html.none

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
            Html.div [
                prop.className "mb-4"
                prop.children [
                    lastSyncLabel model.SteamFamilyLastSync
                ]
            ]

            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.text "Import shared library from your Steam Family group. First discover members, map them to friends, then import."
            ]

            // Token-rejected warning (integration-v0xmv, ADR-0070): shown
            // whenever a family fetch/import surfaced a "family token
            // rejected" error (expired/rejected pasted token) -- never a
            // silent failure. No button here performs any Steam call; the
            // only remedy is pasting a fresh token below.
            if model.SteamFamilyTokenRejected then
                Daisy.alert [
                    alert.warning
                    prop.className "mb-4"
                    prop.children [
                        Html.span [
                            prop.className "text-sm"
                            prop.text "Your Steam Family token has expired — paste a fresh one below"
                        ]
                    ]
                ]

            // How to get the access token (primary step, integration-v0xmv):
            // the browser's own ajaxgetasyncconfig endpoint, no DevTools
            // Network-tab ritual, no Steam login performed by the app.
            Html.div [
                prop.className "collapse collapse-arrow bg-base-200/50 mb-3 rounded-lg"
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
                                    Html.li [
                                        prop.children [
                                            Html.text "In the browser where you are logged into Steam, open "
                                            Html.a [
                                                prop.href "https://store.steampowered.com/pointssummary/ajaxgetasyncconfig"
                                                prop.target "_blank"
                                                prop.rel "noopener noreferrer"
                                                prop.className "link link-primary font-mono"
                                                prop.text "store.steampowered.com/pointssummary/ajaxgetasyncconfig"
                                            ]
                                        ]
                                    ]
                                    Html.li [
                                        prop.children [
                                            Html.text "Copy the value of "
                                            Html.code [ prop.className "badge badge-ghost badge-sm"; prop.text "webapi_token" ]
                                            Html.text " from the JSON shown"
                                        ]
                                    ]
                                    Html.li [ prop.text "Paste it here and Save. The token is valid for roughly a day; when an import reports it rejected, repeat these steps." ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]

            // Family access token input -- the primary (and only) step in
            // (integration-v0xmv): no Connect/Reconnect, no QR ceremony.
            Html.div [
                prop.className "form-control mb-2"
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
                prop.className "flex gap-2 mb-2"
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

            Html.p [
                prop.className "text-xs text-base-content/50 mb-4"
                prop.text "The Steam credential this stores lives only in your local Mediatheca database (single-user, self-hosted)."
            ]

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
                                                    if m.IsMe then
                                                        Daisy.badge [
                                                            badge.primary
                                                            badge.sm
                                                            prop.className "font-semibold"
                                                            prop.text "Me (you)"
                                                        ]
                                                    else
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

                    // integration-n3vqa: "Re-enrich all family games" — the
                    // explicit second action reproducing today's (pre-n3vqa)
                    // always-fetch-everything behaviour. Deliberately NOT
                    // gated on `SteamFamilyImportResult.IsNone` like the
                    // primary button above — it stays available even after a
                    // default import has completed this session, since
                    // "I want a full refresh" is a legitimate follow-up ask.
                    if not model.IsImportingSteamFamily && not model.IsReenrichingSteamFamily then
                        Html.div [
                            prop.className "mb-4"
                            prop.children [
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    prop.onClick (fun _ -> dispatch Reenrich_steam_family)
                                    prop.disabled (
                                        model.SteamFamilyToken = ""
                                        || List.isEmpty model.SteamFamilyMembers
                                        || not hasMappedMembers)
                                    prop.text "Re-enrich all family games (full refresh, slower)"
                                ]
                            ]
                        ]

                    if model.IsReenrichingSteamFamily then
                        Html.div [
                            prop.className "flex items-center gap-2 text-sm mb-4"
                            prop.children [
                                Daisy.loading [ loading.spinner; loading.sm ]
                                Html.span [ prop.text "Re-enriching all family games..." ]
                            ]
                        ]

                    // Last import's persisted result (survives a reload) —
                    // only shown before anything has completed THIS session,
                    // since a fresh completion below always supersedes it.
                    if model.SteamFamilyImportResult.IsNone && not model.IsImportingSteamFamily && not model.IsReenrichingSteamFamily then
                        match model.SteamFamilyLastPersistedResult with
                        | Some persisted ->
                            Html.div [
                                prop.className "mb-4"
                                prop.children [
                                    Daisy.alert [
                                        prop.children [
                                            Html.div [
                                                Html.p [ prop.className "font-bold text-sm text-base-content/70"; prop.text "Last import" ]
                                                arrivalsView persisted.Arrivals persisted.SinceLastSync
                                                if List.isEmpty persisted.Arrivals then
                                                    Html.p [ prop.className "text-sm text-base-content/50 mt-1"; prop.text "No new games since last time." ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        | None -> Html.none

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
                                            arrivalsView result.Arrivals result.SinceLastSync
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

// ── Jellyfin Detail ──

let private jellyfinDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.div [
                prop.className "mb-4"
                prop.children [
                    lastSyncLabel model.JellyfinLastSyncTime
                ]
            ]

            jellyfinSyncStatusView model.JellyfinSyncStatus

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
                prop.className "flex gap-2 mb-4"
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

            // ── Watch History Sync ──
            let jellyfinConnected = model.JellyfinServerUrl <> "" && model.JellyfinUsername <> ""
            if jellyfinConnected then
                Html.div [
                    prop.className "border-t border-base-content/10 pt-4 mt-2"
                    prop.children [
                        Html.h4 [
                            prop.className "text-sm font-bold mb-3"
                            prop.text "Watch History Sync"
                        ]
                        Html.p [
                            prop.className "text-base-content/60 text-sm mb-3"
                            prop.text "Scan your Jellyfin library to preview matches, then import watch history. Only adds data \u2014 never removes existing watches."
                        ]

                        // Scan button
                        Html.div [
                            prop.className "flex gap-2 mb-4"
                            prop.children [
                                Daisy.button.button [
                                    button.outline
                                    button.sm
                                    if model.IsScanningJellyfin then button.disabled
                                    prop.onClick (fun _ -> dispatch Scan_jellyfin_library)
                                    prop.disabled model.IsScanningJellyfin
                                    prop.children [
                                        if model.IsScanningJellyfin then
                                            Daisy.loading [ loading.spinner; loading.sm ]
                                            Html.text "Scanning..."
                                        else
                                            Html.text "Scan Library"
                                    ]
                                ]
                            ]
                        ]

                        // Scan result preview
                        match model.JellyfinScanResult with
                        | Some (Ok scanResult) ->
                            let playedMovies = scanResult.MatchedMovies |> List.filter (fun m -> m.JellyfinItem.Played)
                            let playedSeries = scanResult.MatchedSeries |> List.filter (fun m -> m.JellyfinItem.Played)
                            Html.div [
                                prop.className "mb-4 space-y-3"
                                prop.children [
                                    // Summary stats
                                    Html.div [
                                        prop.className "grid grid-cols-2 gap-2 text-sm"
                                        prop.children [
                                            Html.div [
                                                prop.className "bg-base-200/50 rounded-lg p-3"
                                                prop.children [
                                                    Html.div [ prop.className "text-base-content/50 text-xs"; prop.text "Matched Movies" ]
                                                    Html.div [ prop.className "font-bold"; prop.text (sprintf "%d (%d played)" scanResult.MatchedMovies.Length playedMovies.Length) ]
                                                ]
                                            ]
                                            Html.div [
                                                prop.className "bg-base-200/50 rounded-lg p-3"
                                                prop.children [
                                                    Html.div [ prop.className "text-base-content/50 text-xs"; prop.text "Matched Series" ]
                                                    Html.div [ prop.className "font-bold"; prop.text (sprintf "%d (%d played)" scanResult.MatchedSeries.Length playedSeries.Length) ]
                                                ]
                                            ]
                                            Html.div [
                                                prop.className "bg-base-200/50 rounded-lg p-3"
                                                prop.children [
                                                    Html.div [ prop.className "text-base-content/50 text-xs"; prop.text "Unmatched Movies" ]
                                                    let withTmdb = scanResult.UnmatchedMovies |> List.filter (fun m -> m.TmdbId.IsSome) |> List.length
                                                    Html.div [ prop.className "font-bold"; prop.text (sprintf "%d (%d will auto-add)" scanResult.UnmatchedMovies.Length withTmdb) ]
                                                ]
                                            ]
                                            Html.div [
                                                prop.className "bg-base-200/50 rounded-lg p-3"
                                                prop.children [
                                                    Html.div [ prop.className "text-base-content/50 text-xs"; prop.text "Unmatched Series" ]
                                                    let withTmdb = scanResult.UnmatchedSeries |> List.filter (fun s -> s.TmdbId.IsSome) |> List.length
                                                    Html.div [ prop.className "font-bold"; prop.text (sprintf "%d (%d will auto-add)" scanResult.UnmatchedSeries.Length withTmdb) ]
                                                ]
                                            ]
                                        ]
                                    ]

                                    // Played movies list
                                    if not (List.isEmpty playedMovies) then
                                        Html.div [
                                            prop.className "bg-base-200/50 rounded-lg p-3 max-h-48 overflow-y-auto text-xs font-mono space-y-0.5"
                                            prop.children (
                                                playedMovies |> List.map (fun m ->
                                                    Html.div [
                                                        prop.className "flex gap-2"
                                                        prop.children [
                                                            Html.span [ prop.className "text-base-content/50 truncate max-w-[250px]"; prop.text m.MediathecaName ]
                                                            Html.span [ prop.className "text-base-content/30"; prop.text "\u2192" ]
                                                            Html.span [
                                                                prop.className (if m.HasExistingWatchData then "text-base-content/40" else "text-success")
                                                                prop.text (if m.HasExistingWatchData then "has watch data" else "will add watch session")
                                                            ]
                                                        ]
                                                    ])
                                            )
                                        ]

                                    // Import button
                                    let unmatchedWithTmdb =
                                        (scanResult.UnmatchedMovies |> List.filter (fun m -> m.TmdbId.IsSome) |> List.length)
                                        + (scanResult.UnmatchedSeries |> List.filter (fun s -> s.TmdbId.IsSome) |> List.length)
                                    if playedMovies.Length > 0 || playedSeries.Length > 0 || unmatchedWithTmdb > 0 then
                                        Html.div [
                                            prop.children [
                                                Daisy.button.button [
                                                    button.primary
                                                    button.sm
                                                    if model.IsImportingJellyfin then button.disabled
                                                    prop.onClick (fun _ -> dispatch Import_jellyfin_watch_history)
                                                    prop.disabled model.IsImportingJellyfin
                                                    prop.children [
                                                        if model.IsImportingJellyfin then
                                                            Daisy.loading [ loading.spinner; loading.sm ]
                                                            Html.text "Importing..."
                                                        else
                                                            Html.text "Import Watch History"
                                                    ]
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                        | Some (Error msg) ->
                            Daisy.alert [ alert.error; prop.className "mb-4"; prop.text msg ]
                        | None -> Html.none

                        // Import result
                        match model.JellyfinImportResult with
                        | Some (Ok result) ->
                            Daisy.alert [
                                alert.success
                                prop.className "mb-4"
                                prop.children [
                                    Html.div [
                                        Html.p [ prop.className "font-bold"; prop.text "Jellyfin import completed!" ]
                                        Html.ul [
                                            prop.className "mt-2 text-sm space-y-1"
                                            prop.children [
                                                Html.li [ prop.text (sprintf "Movies auto-added to library: %d" result.MoviesAutoAdded) ]
                                                Html.li [ prop.text (sprintf "Series auto-added to library: %d" result.SeriesAutoAdded) ]
                                                Html.li [ prop.text (sprintf "Movie watch sessions added: %d" result.MoviesAdded) ]
                                                Html.li [ prop.text (sprintf "Episodes marked watched: %d" result.EpisodesAdded) ]
                                                Html.li [ prop.text (sprintf "Items skipped: %d" result.ItemsSkipped) ]
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
        ]
    ]

// ── qBittorrent Detail ──

/// integration-qb7tk: credentials + a "Test connection" round-trip, ahead
/// of any destructive flow. Same Elmish shape as the Jellyfin card above
/// (*Input fields, IsTesting*/IsSaving*, *TestResult/*SaveResult), but Test
/// and Save are two distinct buttons/results rather than Jellyfin's
/// combined "Test & Save" -- the setter here never runs implicitly as a
/// side effect of testing.
let private qbittorrentDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Connect to qBittorrent's WebUI so \"Remove local copy\" can find and delete torrents. Enter the WebUI URL and credentials below."
                ]
            ]

            if model.QbittorrentUrl <> "" then
                Html.div [
                    prop.className "mb-3 flex items-center gap-2 text-sm text-base-content/60"
                    prop.children [
                        Html.span [ prop.text "URL:" ]
                        Html.span [ prop.className "font-mono"; prop.text model.QbittorrentUrl ]
                    ]
                ]

            if model.QbittorrentUsername <> "" then
                Html.div [
                    prop.className "mb-3 flex items-center gap-2 text-sm text-base-content/60"
                    prop.children [
                        Html.span [ prop.text "User:" ]
                        Html.span [ prop.className "font-mono"; prop.text model.QbittorrentUsername ]
                    ]
                ]

            // URL input
            Html.div [
                prop.className "form-control mb-3"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [ Html.span [ prop.className "label-text"; prop.text "WebUI URL" ] ]
                    ]
                    Daisy.input [
                        prop.className "w-full"
                        prop.placeholder "http://your-server:8080"
                        prop.value model.QbittorrentUrlInput
                        prop.onChange (Qbittorrent_url_input_changed >> dispatch)
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
                        prop.value model.QbittorrentUsernameInput
                        prop.onChange (Qbittorrent_username_input_changed >> dispatch)
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
                        prop.value model.QbittorrentPasswordInput
                        prop.onChange (Qbittorrent_password_input_changed >> dispatch)
                    ]
                ]
            ]

            feedbackAlert model.QbittorrentTestResult
            feedbackAlert model.QbittorrentSaveResult

            // Buttons
            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.outline
                        button.sm
                        if model.IsTestingQbittorrent then button.disabled
                        prop.onClick (fun _ -> dispatch Test_qbittorrent_connection)
                        prop.disabled model.IsTestingQbittorrent
                        prop.children [
                            if model.IsTestingQbittorrent then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test connection"
                        ]
                    ]
                    Daisy.button.button [
                        button.primary
                        button.sm
                        if model.IsSavingQbittorrent then button.disabled
                        prop.onClick (fun _ -> dispatch Save_qbittorrent_settings)
                        prop.disabled model.IsSavingQbittorrent
                        prop.children [
                            if model.IsSavingQbittorrent then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save"
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Audible (integration-dhctm, ADR-0074): an imported `audible-cli` auth
/// file, never a login or device registration. The textarea never
/// re-displays a stored file (ADR-0074 point 6 -- it's a secret); once
/// configured, its placeholder says so and the user pastes a NEW file to
/// replace it. Save/Test/Clear are three distinct buttons/results, the same
/// shape `qbittorrentDetail` above uses.
let private audibleDetail (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.children [
            // Installer choice: pipx or uv. Swaps only the install command
            // below; everything after it is the same for both.
            Html.div [
                prop.className "mb-3 flex flex-wrap items-center gap-2"
                prop.children [
                    Html.span [ prop.className (DesignSystem.mutedText + " text-sm"); prop.text "Install with" ]
                    DesignSystem.filterPill "pipx" (model.AudibleInstaller = Pipx) (fun () -> dispatch (Audible_installer_changed Pipx))
                    DesignSystem.filterPill "uv" (model.AudibleInstaller = Uv) (fun () -> dispatch (Audible_installer_changed Uv))
                ]
            ]

            Html.p [
                prop.className "text-base-content/70 mb-4 text-sm"
                prop.children [
                    Html.text "Run "
                    Html.code [ prop.className "text-xs"; prop.text (State.audibleInstallCommand model.AudibleInstaller) ]
                    Html.text ", then "
                    Html.code [ prop.className "text-xs"; prop.text "audible quickstart" ]
                    Html.text " on your own machine, and paste the resulting auth file below: "
                    Html.code [ prop.className "text-xs"; prop.text "~/.audible/<profile>.json" ]
                    Html.text " on Linux/macOS, "
                    Html.code [ prop.className "text-xs"; prop.text "%LOCALAPPDATA%\\audible\\<profile>.json" ]
                    Html.text " on Windows. Mediatheca never logs in or registers a device itself."
                ]
            ]

            match model.AudibleCustomerName with
            | Some name ->
                Html.div [
                    prop.className "mb-3 flex items-center gap-2 text-sm text-base-content/60"
                    prop.children [
                        Html.span [ prop.text "Connected as:" ]
                        Html.span [ prop.className "font-mono"; prop.text (sprintf "%s (%s)" name model.AudibleMarketplace) ]
                    ]
                ]
            | None -> Html.none

            // Standing "auth file rejected" notice (ADR-0074 point 4) --
            // same warning style as Steam's Web API key / Family token
            // rejection notices, distinct wording so the remedy is
            // unambiguous.
            match model.AudibleLastError with
            | Some lastError ->
                Daisy.alert [
                    alert.warning
                    prop.className "mb-4"
                    prop.children [
                        Html.span [ prop.className "text-sm"; prop.text lastError ]
                    ]
                ]
            | None -> Html.none

            if not model.AudibleConfigured then
                Html.div [
                    prop.className "form-control mb-3"
                    prop.children [
                        Daisy.label [
                            prop.className "label"
                            prop.children [ Html.span [ prop.className "label-text"; prop.text "Marketplace (used for search until an auth file is pasted)" ] ]
                        ]
                        Daisy.select [
                            prop.className "w-full"
                            prop.value model.AudibleMarketplace
                            prop.onChange (Audible_marketplace_changed >> dispatch)
                            prop.children [
                                for code in [ "de"; "us"; "uk"; "fr"; "ca"; "au"; "it"; "es"; "jp"; "in" ] ->
                                    Html.option [ prop.value code; prop.text code ]
                            ]
                        ]
                    ]
                ]

            Html.div [
                prop.className "form-control mb-4"
                prop.children [
                    Daisy.label [
                        prop.className "label"
                        prop.children [ Html.span [ prop.className "label-text"; prop.text "Auth file (JSON)" ] ]
                    ]
                    Daisy.textarea [
                        prop.className "w-full font-mono text-xs"
                        prop.rows 4
                        prop.placeholder (if model.AudibleConfigured then "auth file stored — paste a new one to replace" else "paste the contents of <profile>.json here (~/.audible/ or %LOCALAPPDATA%\\audible\\)")
                        prop.value model.AudibleAuthFileInput
                        prop.onChange (Audible_auth_file_input_changed >> dispatch)
                    ]
                ]
            ]

            feedbackAlert model.AudibleTestResult
            feedbackAlert model.AudibleSaveResult

            Html.div [
                prop.className "flex gap-2 mb-4"
                prop.children [
                    Daisy.button.button [
                        button.outline
                        button.sm
                        if not model.AudibleConfigured || model.IsTestingAudible then button.disabled
                        prop.onClick (fun _ -> dispatch Test_audible_connection)
                        prop.disabled (not model.AudibleConfigured || model.IsTestingAudible)
                        prop.children [
                            if model.IsTestingAudible then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Test connection"
                        ]
                    ]
                    Daisy.button.button [
                        button.primary
                        button.sm
                        if model.AudibleAuthFileInput = "" || model.IsSavingAudible then button.disabled
                        prop.onClick (fun _ -> dispatch Save_audible_auth_file)
                        prop.disabled (model.AudibleAuthFileInput = "" || model.IsSavingAudible)
                        prop.children [
                            if model.IsSavingAudible then
                                Daisy.loading [ loading.spinner; loading.sm ]
                            Html.text "Save"
                        ]
                    ]
                    if model.AudibleConfigured then
                        Daisy.button.button [
                            button.ghost
                            button.sm
                            if model.IsClearingAudible then button.disabled
                            prop.onClick (fun _ -> dispatch Clear_audible_auth_file)
                            prop.disabled model.IsClearingAudible
                            prop.children [
                                if model.IsClearingAudible then
                                    Daisy.loading [ loading.spinner; loading.sm ]
                                Html.text "Clear"
                            ]
                        ]
                ]
            ]

            // Library import + daily progress sync (integration-jjvg2,
            // ADR-0074/ADR-0076/ADR-0026) -- only meaningful once an auth
            // file is stored; the catalog-search half of Audible needs none.
            if model.AudibleConfigured then
                Html.div [
                    prop.className "border-t border-base-content/10 pt-4"
                    prop.children [
                        match model.AudibleImportResult with
                        | Some (Ok result) ->
                            Daisy.alert [
                                alert.success
                                prop.className "mb-4"
                                prop.children [
                                    Html.span [
                                        prop.className "text-sm"
                                        prop.text (sprintf "Imported: %d total, %d created, %d already known, %d progress observed" result.Total result.Created result.AlreadyKnown result.ProgressObserved)
                                    ]
                                ]
                            ]
                        | Some (Error e) ->
                            Daisy.alert [
                                alert.error
                                prop.className "mb-4"
                                prop.children [ Html.span [ prop.className "text-sm"; prop.text e ] ]
                            ]
                        | None -> Html.none

                        match model.AudibleProgressSyncResult with
                        | Some (Ok result) ->
                            Daisy.alert [
                                alert.success
                                prop.className "mb-4"
                                prop.children [
                                    Html.span [
                                        prop.className "text-sm"
                                        prop.text (sprintf "Synced: %d observed, %d unmatched" result.Observed result.Unmatched)
                                    ]
                                ]
                            ]
                        | Some (Error e) ->
                            Daisy.alert [
                                alert.error
                                prop.className "mb-4"
                                prop.children [ Html.span [ prop.className "text-sm"; prop.text e ] ]
                            ]
                        | None -> Html.none

                        match model.AudibleLastImportResult with
                        | Some lastImport ->
                            Html.div [
                                prop.className "mb-2 text-sm text-base-content/60"
                                prop.children [ Html.div [ prop.className "font-mono text-xs"; prop.text (sprintf "Last import: %s" lastImport) ] ]
                            ]
                        | None -> Html.none

                        match model.AudibleLastSync, model.AudibleLastSyncResult with
                        | Some lastSync, Some lastResult ->
                            Html.div [
                                prop.className "mb-4 text-sm text-base-content/60"
                                prop.children [
                                    Html.div [ prop.text (sprintf "Last sync: %s" lastSync) ]
                                    Html.div [ prop.className "font-mono text-xs"; prop.text lastResult ]
                                ]
                            ]
                        | _ -> Html.none

                        Html.div [
                            prop.className "flex gap-2"
                            prop.children [
                                Daisy.button.button [
                                    button.outline
                                    button.sm
                                    if model.IsImportingAudibleLibrary then button.disabled
                                    prop.onClick (fun _ -> dispatch Import_audible_library)
                                    prop.disabled model.IsImportingAudibleLibrary
                                    prop.children [
                                        if model.IsImportingAudibleLibrary then
                                            Daisy.loading [ loading.spinner; loading.sm ]
                                        Html.text "Import library"
                                    ]
                                ]
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    if model.IsSyncingAudibleProgress then button.disabled
                                    prop.onClick (fun _ -> dispatch Sync_audible_progress_now)
                                    prop.disabled model.IsSyncingAudibleProgress
                                    prop.children [
                                        if model.IsSyncingAudibleProgress then
                                            Daisy.loading [ loading.spinner; loading.sm ]
                                        Html.text "Sync progress now"
                                    ]
                                ]
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

                    integrationCard
                        Icons.bolt
                        "qBittorrent"
                        "Torrent client for \"Remove local copy\""
                        (statusBadge (model.QbittorrentUrl <> "" && model.QbittorrentUsername <> "") (if model.QbittorrentUrl <> "" && model.QbittorrentUsername <> "" then "Connected" else "Not configured"))
                        (qbittorrentDetail model dispatch)

                    // integration-dhctm (ADR-0074): appended after qBittorrent.
                    integrationCard
                        Icons.book
                        "Audible"
                        "Audiobook catalog search and listening progress"
                        (statusBadge model.AudibleConfigured (if model.AudibleConfigured then "Connected" else "Not configured"))
                        (audibleDetail model dispatch)
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
                ]
            ]

            // Administration section (administration-k3vmt): the former
            // /admin console's six tabs, now inline collapsible sections
            // below Data Imports, in the tabs' former order. The dirty
            // banner sits above all six so it's visible regardless of which
            // (if any) are expanded — the same "visible on every tab"
            // guarantee ADR-0034 established, just moved. It also stays
            // visible above the danger gate while locked: knowing a
            // projection is stale is read-only information, so hiding it
            // would cost the operator awareness without buying any safety
            // (its affordance leads to the unlock box, not past it).
            Html.div [
                prop.className "flex items-baseline justify-between gap-4 mb-4 mt-8"
                prop.children [
                    Html.h2 [
                        prop.className (DesignSystem.subtitle + " text-base-content/50")
                        prop.text "Administration"
                    ]
                    if model.AdminUnlocked then
                        Html.button [
                            prop.className (DesignSystem.mutedText + " underline hover:text-base-content transition-colors duration-200")
                            prop.onClick (fun _ -> dispatch Lock_admin_sections)
                            prop.text "Lock"
                        ]
                ]
            ]
            Mediatheca.Client.Pages.Admin.Views.dirtyBanner model.AdminModel.ProjectionsModel (fun () -> dispatch Go_to_projections_section)
            if model.AdminUnlocked then adminSections model dispatch
            else adminUnlockGate model dispatch
        ]
    ]
