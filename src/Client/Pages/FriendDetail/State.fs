module Mediatheca.Client.Pages.FriendDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.FriendDetail.Types

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Friend = None
      FriendMedia = None
      IsLoading = true
      IsEditing = false
      EditForm = { Name = ""; ImageRef = None }
      Error = None
      ShowRemoveConfirm = false
      ShowEventHistory = false
      CollapsedSections = Set.empty
      SectionSettings = Map.empty },
    Cmd.ofMsg (Load_friend slug)

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_friend slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getFriend slug Friend_loaded
            Cmd.OfAsync.perform api.getFriendMedia slug Friend_media_loaded
            Cmd.OfAsync.perform api.getCollapsedSections ("friend:" + slug) Collapsed_loaded
            Cmd.OfAsync.perform api.getViewSettings ("friend:" + slug + ":recommended") (fun s -> Section_settings_loaded ("Recommended", s))
            Cmd.OfAsync.perform api.getViewSettings ("friend:" + slug + ":pending") (fun s -> Section_settings_loaded ("Pending", s))
            Cmd.OfAsync.perform api.getViewSettings ("friend:" + slug + ":watched") (fun s -> Section_settings_loaded ("Watched", s))
        ]

    | Friend_loaded friend ->
        { model with Friend = friend; IsLoading = false }, Cmd.none

    | Friend_media_loaded media ->
        { model with FriendMedia = Some media }, Cmd.none

    | Start_editing ->
        match model.Friend with
        | Some f ->
            { model with
                IsEditing = true
                EditForm = { Name = f.Name; ImageRef = f.ImageRef }
                Error = None }, Cmd.none
        | None -> model, Cmd.none

    | Cancel_editing ->
        { model with IsEditing = false; Error = None }, Cmd.none

    | Edit_name_changed name ->
        { model with EditForm = { model.EditForm with Name = name } }, Cmd.none

    | Submit_update ->
        let trimmedName = model.EditForm.Name.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            model,
            Cmd.OfAsync.either
                (fun () -> api.updateFriend model.Slug trimmedName model.EditForm.ImageRef)
                ()
                Update_result
                (fun ex -> Update_result (Error ex.Message))

    | Update_result (Ok ()) ->
        { model with IsEditing = false },
        Cmd.OfAsync.perform api.getFriend model.Slug Friend_loaded

    | Update_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_friend ->
        { model with ShowRemoveConfirm = true }, Cmd.none

    | Cancel_remove_friend ->
        { model with ShowRemoveConfirm = false }, Cmd.none

    | Confirm_remove_friend ->
        { model with ShowRemoveConfirm = false },
        Cmd.OfAsync.either
            api.removeFriend model.Slug
            Remove_result
            (fun ex -> Remove_result (Error ex.Message))

    | Remove_result (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "friends")

    | Remove_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Upload_friend_image (data, filename) ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.uploadFriendImage model.Slug data filename) ()
            Image_uploaded
            (fun ex -> Image_uploaded (Error ex.Message))

    | Image_uploaded (Ok _) ->
        model, Cmd.OfAsync.perform api.getFriend model.Slug Friend_loaded

    | Image_uploaded (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_from_recommended (mediaSlug, routePrefix) ->
        let apiCall =
            match routePrefix with
            | "series" -> api.removeSeriesRecommendation mediaSlug model.Slug
            | "games" -> api.removeGameRecommendation mediaSlug model.Slug
            | _ -> api.removeRecommendation mediaSlug model.Slug
        model,
        Cmd.OfAsync.either (fun () -> apiCall) () Media_remove_result (fun ex -> Media_remove_result (Error ex.Message))

    | Remove_from_pending (mediaSlug, routePrefix) ->
        let apiCall =
            match routePrefix with
            | "series" -> api.removeSeriesWantToWatchWith mediaSlug model.Slug
            | "games" -> api.removeGameWantToPlayWith mediaSlug model.Slug
            | _ -> api.removeWantToWatchWith mediaSlug model.Slug
        model,
        Cmd.OfAsync.either (fun () -> apiCall) () Media_remove_result (fun ex -> Media_remove_result (Error ex.Message))

    | Media_remove_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getFriendMedia model.Slug Friend_media_loaded

    | Media_remove_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Toggle_section section ->
        let collapsed =
            if Set.contains section model.CollapsedSections then
                Set.remove section model.CollapsedSections
            else
                Set.add section model.CollapsedSections
        { model with CollapsedSections = collapsed },
        Cmd.OfAsync.either
            (fun () -> api.saveCollapsedSections ("friend:" + model.Slug) (Set.toList collapsed)) ()
            (fun () -> Settings_saved)
            (fun _ -> Settings_saved)

    | Collapsed_loaded sections ->
        { model with CollapsedSections = Set.ofList sections }, Cmd.none

    | Section_settings_loaded (section, settings) ->
        match settings with
        | Some s -> { model with SectionSettings = Map.add section s model.SectionSettings }, Cmd.none
        | None -> model, Cmd.none

    | Save_section_settings (section, settings) ->
        let key = section.ToLowerInvariant()
        { model with SectionSettings = Map.add section settings model.SectionSettings },
        Cmd.OfAsync.either
            (fun () -> api.saveViewSettings ("friend:" + model.Slug + ":" + key) settings) ()
            (fun () -> Settings_saved)
            (fun _ -> Settings_saved)

    | Settings_saved ->
        model, Cmd.none

    | Open_event_history ->
        { model with ShowEventHistory = true }, Cmd.none

    | Close_event_history ->
        { model with ShowEventHistory = false }, Cmd.none
