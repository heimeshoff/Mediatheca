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
    Cmd.ofMsg LoadFriends

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadFriends ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getFriends () FriendsLoaded

    | FriendsLoaded friends ->
        { model with Friends = friends; IsLoading = false }, Cmd.none

    | ToggleAddForm ->
        { model with ShowAddForm = not model.ShowAddForm; AddForm = { Name = "" }; Error = None }, Cmd.none

    | AddFormNameChanged name ->
        { model with AddForm = { model.AddForm with Name = name } }, Cmd.none

    | SubmitAddFriend ->
        let trimmedName = model.AddForm.Name.Trim()
        if trimmedName = "" then
            { model with Error = Some "Name is required" }, Cmd.none
        else
            model,
            Cmd.OfAsync.either
                api.addFriend trimmedName
                FriendAdded
                (fun ex -> FriendAdded (Error ex.Message))

    | FriendAdded (Ok slug) ->
        { model with ShowAddForm = false; AddForm = { Name = "" } },
        Cmd.batch [
            Cmd.ofMsg LoadFriends
            Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate ("friends", slug))
        ]

    | FriendAdded (Error err) ->
        { model with Error = Some err }, Cmd.none

    | RemoveFriend slug ->
        model,
        Cmd.OfAsync.either
            api.removeFriend slug
            FriendRemoved
            (fun ex -> FriendRemoved (Error ex.Message))

    | FriendRemoved (Ok ()) ->
        model, Cmd.ofMsg LoadFriends

    | FriendRemoved (Error err) ->
        { model with Error = Some err }, Cmd.none
