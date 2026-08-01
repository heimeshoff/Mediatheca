module Mediatheca.Client.Pages.Admin.Views

open Feliz
open Mediatheca.Client.Pages.Admin.Types

// The tabbed shell (h1 "Administration" + underline tab bar switching
// `ActiveTab`) is gone — administration-k3vmt dissolves the /admin console
// into Settings, where all six sections render as independent, always-
// mounted collapsible cards rather than a single switched tab. What's left
// here is exactly what a headless composite child needs: one render
// function per section (Settings/Views.fs wraps each in its own controlled
// collapse and decides layout/order) and the cross-section dirty banner.

/// Events section — delegates to the existing EventBrowser page as-is.
let eventsSection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.EventBrowser.Views.view model.EventBrowserModel (Event_browser_msg >> dispatch)

/// Health section — delegates to AdminHealth (administration-hw74a).
let healthSection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.AdminHealth.Views.view model.HealthModel (Health_msg >> dispatch)

/// Projections section — delegates to AdminProjections (administration-qjcp4).
let projectionsSection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.AdminProjections.Views.view model.ProjectionsModel (Projections_msg >> dispatch)

/// Images section — delegates to AdminImages (administration-xx3mw).
let imagesSection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.AdminImages.Views.view model.ImagesModel (Images_msg >> dispatch)

/// Jobs section — delegates to AdminJobs (administration-yamm5).
let jobsSection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.AdminJobs.Views.view model.JobsModel (Jobs_msg >> dispatch)

/// Surgery section — delegates to AdminSurgery (administration-wwc36).
let surgerySection (model: Model) (dispatch: Msg -> unit) =
    Mediatheca.Client.Pages.AdminSurgery.Views.view model.SurgeryModel (Surgery_msg >> dispatch)

/// Cross-section "projections out of sync — rebuild" banner (administration-
/// wwc36, ADR-0034): client-derived from `getProjectionStats`'s `Lag` field —
/// no new API method. Rendered above the six sections on Settings so it's
/// visible regardless of which are expanded/collapsed (previously above the
/// tab bar, visible on every tab). Disappears once every projection's Lag
/// returns to 0. Its "Go to Projections" affordance used to be a real
/// navigation to `/admin/projections`; per-section deep-linkability is gone
/// (administration-k3vmt), so the caller now supplies an in-page
/// expand+scroll callback instead of a `Page` to navigate to.
let dirtyBanner (projectionsModel: Mediatheca.Client.Pages.AdminProjections.Types.Model) (onGoToProjections: unit -> unit) =
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
                    prop.href "#"
                    prop.onClick (fun e ->
                        e.preventDefault()
                        onGoToProjections ())
                    prop.text "Go to Projections"
                ]
            ]
        ]
