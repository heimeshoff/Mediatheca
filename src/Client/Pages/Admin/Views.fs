module Mediatheca.Client.Pages.Admin.Views

open Feliz
open Feliz.Router
open Mediatheca.Client
open Mediatheca.Client.Router
open Mediatheca.Client.Pages.Admin.Types

let private tabLabel (tab: AdminTab) =
    match tab with
    | AdminEvents -> "Events"
    | AdminProjections -> "Projections"
    | AdminHealth -> "Health"
    | AdminImages -> "Images"
    | AdminJobs -> "Jobs"
    | AdminSurgery -> "Surgery"

let private allTabs = [ AdminEvents; AdminProjections; AdminHealth; AdminImages; AdminJobs; AdminSurgery ]

// URL-addressable tabs — plain anchors + Router.navigate (same pattern as
// Sidebar's nav items) rather than in-page dispatch, so /admin/projections
// etc. are directly linkable/bookmarkable per the task's requirement.
let private tabBar (activeTab: AdminTab) =
    Html.div [
        prop.className "flex gap-6 border-b border-base-300/30 mb-6"
        prop.role "tablist"
        prop.children [
            for tab in allTabs do
                let page = Admin tab
                Html.a [
                    prop.key (tabLabel tab)
                    prop.className (DesignSystem.underlineTabClass (activeTab = tab))
                    prop.role "tab"
                    prop.href (Route.toUrl page)
                    prop.onClick (fun e ->
                        e.preventDefault()
                        Route.navigateTo page)
                    prop.text (tabLabel tab)
                ]
        ]
    ]

/// Cross-tab "projections out of sync — rebuild" banner (administration-wwc36):
/// client-derived from `getProjectionStats`'s `Lag` field — no new API method.
/// Rendered above the tab bar so it's visible on every tab (not just
/// Projections), the same "leave dirty, reuse Rebuild-all" precedent
/// ADR-0029 set for import and ADR-0025 set for image-cache orphan
/// detection. Disappears once every projection's Lag returns to 0 (the
/// Projections tab's own Rebuild-all reloads Stats after every step and on
/// completion; Admin.State's Surgery_msg handler reloads Stats immediately
/// after a committed surgery mutation too).
let private dirtyBanner (projectionsModel: Mediatheca.Client.Pages.AdminProjections.Types.Model) =
    let dirtyNames =
        projectionsModel.Stats
        |> List.filter (fun s -> s.Lag > 0L)
        |> List.map (fun s -> s.Name)
    if List.isEmpty dirtyNames then
        Html.none
    else
        Html.div [
            prop.className "bg-warning/10 border border-warning/30 rounded-lg px-4 py-2 mb-4 flex items-center justify-between gap-3"
            prop.children [
                Html.p [
                    prop.className "text-sm text-warning"
                    prop.text (sprintf "Projections out of sync — rebuild (%s)" (String.concat ", " dirtyNames))
                ]
                Html.a [
                    prop.className "text-sm text-warning underline whitespace-nowrap"
                    prop.href (Route.toUrl (Admin AdminProjections))
                    prop.onClick (fun e ->
                        e.preventDefault()
                        Route.navigateTo (Admin AdminProjections))
                    prop.text "Go to Projections"
                ]
            ]
        ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className DesignSystem.animateFadeIn
        prop.children [
            // Header + tab bar own the page padding; each tab's content manages
            // its own (the Events tab reuses EventBrowser.Views.view as-is).
            Html.div [
                prop.className (DesignSystem.pagePadding + " pb-0")
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary mb-6"
                        prop.text "Administration"
                    ]
                    dirtyBanner model.ProjectionsModel
                    tabBar model.ActiveTab
                ]
            ]
            match model.ActiveTab with
            | AdminEvents ->
                Mediatheca.Client.Pages.EventBrowser.Views.view model.EventBrowserModel (Event_browser_msg >> dispatch)
            | AdminHealth ->
                Mediatheca.Client.Pages.AdminHealth.Views.view model.HealthModel (Health_msg >> dispatch)
            | AdminProjections ->
                Mediatheca.Client.Pages.AdminProjections.Views.view model.ProjectionsModel (Projections_msg >> dispatch)
            | AdminImages ->
                Mediatheca.Client.Pages.AdminImages.Views.view model.ImagesModel (Images_msg >> dispatch)
            | AdminJobs ->
                Mediatheca.Client.Pages.AdminJobs.Views.view model.JobsModel (Jobs_msg >> dispatch)
            | AdminSurgery ->
                Mediatheca.Client.Pages.AdminSurgery.Views.view model.SurgeryModel (Surgery_msg >> dispatch)
        ]
    ]
