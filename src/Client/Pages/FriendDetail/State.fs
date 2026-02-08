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
    Cmd.ofMsg (Load_friend slug)

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_friend slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.OfAsync.perform api.getFriend slug Friend_loaded

    | Friend_loaded friend ->
        { model with Friend = friend; IsLoading = false }, Cmd.none

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
        model,
        Cmd.OfAsync.either
            api.removeFriend model.Slug
            Remove_result
            (fun ex -> Remove_result (Error ex.Message))

    | Remove_result (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "friends")

    | Remove_result (Error err) ->
        { model with Error = Some err }, Cmd.none
