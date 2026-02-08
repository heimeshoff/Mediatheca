module Mediatheca.Client.Pages.Friends.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Friends.Types

let init () : Model * Cmd<Msg> =
    { Friends = []
      IsLoading = true
      ShowAddForm = false
      AddForm = { Name = "" }
      Error = None },
    Cmd.ofMsg Load_friends

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_friends ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getFriends () Friends_loaded

    | Friends_loaded friends ->
        { model with Friends = friends; IsLoading = false }, Cmd.none

    | Toggle_add_form ->
        { model with ShowAddForm = not model.ShowAddForm; AddForm = { Name = "" }; Error = None }, Cmd.none

    | Add_form_name_changed name ->
        { model with AddForm = { model.AddForm with Name = name } }, Cmd.none

    | Submit_add_friend ->
        let trimmedName = model.AddForm.Name.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            model,
            Cmd.OfAsync.either
                api.addFriend trimmedName
                Friend_added
                (fun ex -> Friend_added (Error ex.Message))

    | Friend_added (Ok slug) ->
        { model with ShowAddForm = false; AddForm = { Name = "" } },
        Cmd.batch [
            Cmd.ofMsg Load_friends
            Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate ("friends", slug))
        ]

    | Friend_added (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_friend slug ->
        model,
        Cmd.OfAsync.either
            api.removeFriend slug
            Friend_removed
            (fun ex -> Friend_removed (Error ex.Message))

    | Friend_removed (Ok ()) ->
        model, Cmd.ofMsg Load_friends

    | Friend_removed (Error err) ->
        { model with Error = Some err }, Cmd.none
