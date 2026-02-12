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
      ShowRecordSession = false
      SessionForm = { Date = ""; SelectedFriends = Set.empty }
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

    | Open_record_session ->
        let today = System.DateTime.Now.ToString("yyyy-MM-dd")
        { model with
            ShowRecordSession = true
            SessionForm = { Date = today; SelectedFriends = Set.empty } }, Cmd.none

    | Close_record_session ->
        { model with ShowRecordSession = false }, Cmd.none

    | Session_date_changed d ->
        { model with SessionForm = { model.SessionForm with Date = d } }, Cmd.none

    | Toggle_session_friend slug ->
        let friends =
            if model.SessionForm.SelectedFriends.Contains slug then
                model.SessionForm.SelectedFriends.Remove slug
            else
                model.SessionForm.SelectedFriends.Add slug
        { model with SessionForm = { model.SessionForm with SelectedFriends = friends } }, Cmd.none

    | Submit_record_session ->
        let request: RecordWatchSessionRequest = {
            Date = model.SessionForm.Date
            FriendSlugs = model.SessionForm.SelectedFriends |> Set.toList
        }
        model,
        Cmd.OfAsync.perform (fun () -> api.recordWatchSession model.Slug request) () Session_recorded

    | Session_recorded (Ok _) ->
        { model with ShowRecordSession = false },
        Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | Session_recorded (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_content_block request ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.addContentBlock model.Slug None request)
            ()
            (fun result -> Content_block_result (result |> Result.map ignore))

    | Update_content_block (blockId, request) ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.updateContentBlock model.Slug blockId request)
            ()
            Content_block_result

    | Remove_content_block blockId ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.removeContentBlock model.Slug blockId)
            ()
            Content_block_result

    | Content_block_result (Ok _) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | Content_block_result (Error err) ->
        { model with Error = Some err }, Cmd.none
