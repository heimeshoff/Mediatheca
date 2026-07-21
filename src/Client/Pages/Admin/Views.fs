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
    | AdminJobs -> "Jobs"
    | AdminSurgery -> "Surgery"

let private allTabs = [ AdminEvents; AdminProjections; AdminHealth; AdminJobs; AdminSurgery ]

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

let private placeholderPanel (tab: AdminTab) =
    let blurb =
        match tab with
        | AdminJobs -> "Scheduled jobs — background job status lands here."
        | AdminSurgery -> "Event surgery — corrective tooling lands here."
        | AdminEvents | AdminHealth | AdminProjections -> ""
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-8 text-center")
        prop.children [
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text blurb
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
            | other ->
                Html.div [
                    prop.className DesignSystem.pagePadding
                    prop.children [ placeholderPanel other ]
                ]
        ]
    ]
