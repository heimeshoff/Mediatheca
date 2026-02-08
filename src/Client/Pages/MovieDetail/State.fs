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
        Cmd.ofMsg (Load_movie slug)
    ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_movie slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie slug Movie_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Movie_loaded movie ->
        { model with Movie = movie; IsLoading = false }, Cmd.none

    | Friends_loaded friends ->
        { model with AllFriends = friends }, Cmd.none

    | Recommend_friend friendSlug ->
        { model with ShowFriendPicker = None },
        Cmd.OfAsync.perform (fun () -> api.recommendMovie model.Slug friendSlug) () Command_result

    | Remove_recommendation friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeRecommendation model.Slug friendSlug) () Command_result

    | Want_to_watch_with friendSlug ->
        { model with ShowFriendPicker = None },
        Cmd.OfAsync.perform (fun () -> api.wantToWatchWith model.Slug friendSlug) () Command_result

    | Remove_want_to_watch_with friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeWantToWatchWith model.Slug friendSlug) () Command_result

    | Command_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | Command_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_movie ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeMovie model.Slug) () Movie_removed

    | Movie_removed (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "movies")

    | Movie_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_friend_picker kind ->
        { model with ShowFriendPicker = Some kind }, Cmd.none

    | Close_friend_picker ->
        { model with ShowFriendPicker = None }, Cmd.none
