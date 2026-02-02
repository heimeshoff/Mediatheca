module Mediatheca.Client.Pages.FriendDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.FriendDetail.Types

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Friend = None
      IsLoading = true
      IsEditing = false
      EditForm = { Name = ""; ImageRef = None }
      Error = None },
    Cmd.ofMsg (LoadFriend slug)

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadFriend slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.OfAsync.perform api.getFriend slug FriendLoaded

    | FriendLoaded friend ->
        { model with Friend = friend; IsLoading = false }, Cmd.none

    | StartEditing ->
        match model.Friend with
        | Some f ->
            { model with
                IsEditing = true
                EditForm = { Name = f.Name; ImageRef = f.ImageRef }
                Error = None }, Cmd.none
        | None -> model, Cmd.none

    | CancelEditing ->
        { model with IsEditing = false; Error = None }, Cmd.none

    | EditNameChanged name ->
        { model with EditForm = { model.EditForm with Name = name } }, Cmd.none

    | SubmitUpdate ->
        let trimmedName = model.EditForm.Name.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            model,
            Cmd.OfAsync.either
                (fun () -> api.updateFriend model.Slug trimmedName model.EditForm.ImageRef)
                ()
                UpdateResult
                (fun ex -> UpdateResult (Error ex.Message))

    | UpdateResult (Ok ()) ->
        { model with IsEditing = false },
        Cmd.OfAsync.perform api.getFriend model.Slug FriendLoaded

    | UpdateResult (Error err) ->
        { model with Error = Some err }, Cmd.none

    | RemoveFriend ->
        model,
        Cmd.OfAsync.either
            api.removeFriend model.Slug
            RemoveResult
            (fun ex -> RemoveResult (Error ex.Message))

    | RemoveResult (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "friends")

    | RemoveResult (Error err) ->
        { model with Error = Some err }, Cmd.none
