module Mediatheca.Client.Pages.Admin.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Admin.Types

/// Constructs the composite's initial model plus the eager Cmd every child
/// page returns from its own `init`. Callers that want lazy, per-section
/// loading (Settings.State, administration-k3vmt) keep only the model and
/// discard this Cmd, re-issuing each child's load message on that section's
/// first expand instead.
let init () : Model * Cmd<Msg> =
    let eventBrowserModel, eventBrowserCmd = Mediatheca.Client.Pages.EventBrowser.State.init ()
    let healthModel, healthCmd = Mediatheca.Client.Pages.AdminHealth.State.init ()
    let projectionsModel, projectionsCmd = Mediatheca.Client.Pages.AdminProjections.State.init ()
    let imagesModel, imagesCmd = Mediatheca.Client.Pages.AdminImages.State.init ()
    let jobsModel, jobsCmd = Mediatheca.Client.Pages.AdminJobs.State.init ()
    let surgeryModel, surgeryCmd = Mediatheca.Client.Pages.AdminSurgery.State.init ()
    { EventBrowserModel = eventBrowserModel
      HealthModel = healthModel
      ProjectionsModel = projectionsModel
      ImagesModel = imagesModel
      JobsModel = jobsModel
      SurgeryModel = surgeryModel },
    Cmd.batch [
        Cmd.map Event_browser_msg eventBrowserCmd
        Cmd.map Health_msg healthCmd
        Cmd.map Projections_msg projectionsCmd
        Cmd.map Images_msg imagesCmd
        Cmd.map Jobs_msg jobsCmd
        Cmd.map Surgery_msg surgeryCmd
    ]

/// Bumps the Event Browser's Follow epoch so any Follow poll already
/// scheduled — or a `getEventsAfter` request already in flight — is stale by
/// the time it (re)dispatches, and is dropped by the existing
/// `Poll_tail`/`Tail_loaded` epoch guard instead of rescheduling. Idempotent:
/// a no-op when Follow was already off. Originally called only from root
/// `State.Url_changed` on navigating away from the Admin page
/// (administration-mtf1f iteration 2); administration-k3vmt re-keys that
/// trigger to "leaving Settings" and adds a second trigger — collapsing the
/// Events section without navigating (`Settings.State`'s
/// `Toggle_events_section`) — both calling this same idempotent function
/// rather than duplicating the epoch-bump logic.
let stopFollowing (model: Model) : Model =
    { model with EventBrowserModel = Mediatheca.Client.Pages.EventBrowser.State.stopFollowing model.EventBrowserModel }

let update (adminApi: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Event_browser_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.EventBrowser.State.update adminApi childMsg model.EventBrowserModel
        { model with EventBrowserModel = childModel }, Cmd.map Event_browser_msg childCmd

    | Health_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminHealth.State.update adminApi childMsg model.HealthModel
        { model with HealthModel = childModel }, Cmd.map Health_msg childCmd

    | Projections_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminProjections.State.update adminApi childMsg model.ProjectionsModel
        { model with ProjectionsModel = childModel }, Cmd.map Projections_msg childCmd

    | Images_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminImages.State.update adminApi childMsg model.ImagesModel
        { model with ImagesModel = childModel }, Cmd.map Images_msg childCmd

    | Jobs_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminJobs.State.update adminApi childMsg model.JobsModel
        { model with JobsModel = childModel }, Cmd.map Jobs_msg childCmd

    | Surgery_msg childMsg ->
        let childModel, childCmd = Mediatheca.Client.Pages.AdminSurgery.State.update adminApi childMsg model.SurgeryModel
        let model = { model with SurgeryModel = childModel }
        // A committed surgery mutation always rewinds every checkpoint-tracked
        // projection's checkpoint (ADR-0034) — reload the Projections tab's
        // stats immediately (not just on next tab visit) so the cross-tab
        // dirty banner (client-derived from getProjectionStats' Lag field,
        // administration-wwc36) reflects it right away, the same way
        // AdminProjections.State reloads its own Stats after every rebuild
        // step and after import.
        match childMsg with
        | Mediatheca.Client.Pages.AdminSurgery.Types.Mutation_completed (Applied _) ->
            model,
            Cmd.batch [
                Cmd.map Surgery_msg childCmd
                Cmd.map Projections_msg (Cmd.ofMsg Mediatheca.Client.Pages.AdminProjections.Types.Load)
            ]
        | _ ->
            model, Cmd.map Surgery_msg childCmd
