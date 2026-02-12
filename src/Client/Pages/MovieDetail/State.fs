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
      EditingSessionDate = None
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
        model,
        Cmd.OfAsync.perform (fun () -> api.recommendMovie model.Slug friendSlug) () Command_result

    | Remove_recommendation friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeRecommendation model.Slug friendSlug) () Command_result

    | Want_to_watch_with friendSlug ->
        model,
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

    | Record_quick_session ->
        let today = System.DateTime.Now.ToString("yyyy-MM-dd")
        let request: RecordWatchSessionRequest = {
            Date = today
            FriendSlugs = []
        }
        model,
        Cmd.OfAsync.perform (fun () -> api.recordWatchSession model.Slug request) () Quick_session_recorded

    | Quick_session_recorded (Ok _) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | Quick_session_recorded (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Edit_session_date sessionId ->
        { model with EditingSessionDate = Some sessionId }, Cmd.none

    | Update_session_date (sessionId, date) ->
        { model with EditingSessionDate = None },
        Cmd.OfAsync.perform (fun () -> api.updateWatchSessionDate model.Slug sessionId date) () Command_result

    | Add_friend_to_session (sessionId, friendSlug) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addFriendToWatchSession model.Slug sessionId friendSlug) () Command_result

    | Remove_friend_from_session (sessionId, friendSlug) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeFriendFromWatchSession model.Slug sessionId friendSlug) () Command_result

    | Add_new_friend_to_session (sessionId, name) ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addFriendToWatchSession model.Slug sessionId slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () New_friend_for_session_result

    | New_friend_for_session_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | New_friend_for_session_result (Error err) ->
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

    | Add_friend_and_recommend name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.recommendMovie model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_recommend_result

    | Friend_and_recommend_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_recommend_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_friend_and_watch_with name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.wantToWatchWith model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_watch_with_result

    | Friend_and_watch_with_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_watch_with_result (Error err) ->
        { model with Error = Some err }, Cmd.none
