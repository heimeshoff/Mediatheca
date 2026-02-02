module Mediatheca.Client.Pages.MovieDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.MovieDetail.Types

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Movie = None
      AllFriends = []
      IsLoading = true
      ShowFriendPicker = None
      Error = None },
    Cmd.batch [
        Cmd.ofMsg (LoadMovie slug)
    ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | LoadMovie slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie slug MovieLoaded
            Cmd.OfAsync.perform api.getFriends () FriendsLoaded
        ]

    | MovieLoaded movie ->
        { model with Movie = movie; IsLoading = false }, Cmd.none

    | FriendsLoaded friends ->
        { model with AllFriends = friends }, Cmd.none

    | RecommendFriend friendSlug ->
        { model with ShowFriendPicker = None },
        Cmd.OfAsync.perform (fun () -> api.recommendMovie model.Slug friendSlug) () CommandResult

    | RemoveRecommendation friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeRecommendation model.Slug friendSlug) () CommandResult

    | WantToWatchWith friendSlug ->
        { model with ShowFriendPicker = None },
        Cmd.OfAsync.perform (fun () -> api.wantToWatchWith model.Slug friendSlug) () CommandResult

    | RemoveWantToWatchWith friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeWantToWatchWith model.Slug friendSlug) () CommandResult

    | CommandResult (Ok ()) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug MovieLoaded

    | CommandResult (Error err) ->
        { model with Error = Some err }, Cmd.none

    | RemoveMovie ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeMovie model.Slug) () MovieRemoved

    | MovieRemoved (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "movies")

    | MovieRemoved (Error err) ->
        { model with Error = Some err }, Cmd.none

    | OpenFriendPicker kind ->
        { model with ShowFriendPicker = Some kind }, Cmd.none

    | CloseFriendPicker ->
        { model with ShowFriendPicker = None }, Cmd.none
