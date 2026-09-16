module Mediatheca.Client.Pages.MovieDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Components
open Mediatheca.Client.Pages.MovieDetail.Types

/// The plain-lambda effects `LocalCopyRemovalDialog.update` needs, built from
/// the real `api` (integration-mqsd3 — see the component for why not the
/// whole `api`).
let private localCopyEffects (api: IMediathecaApi) : LocalCopyRemovalDialog.Effects = {
    Plan = api.planLocalCopyRemoval
    Remove = api.removeLocalCopy
}

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Movie = None
      AllFriends = []
      AllCatalogs = []
      MovieCatalogs = []
      ShowCatalogPicker = false
      IsLoading = true
      ShowFriendPicker = None
      EditingSessionDate = None
      FullCredits = None
      TrailerKey = None
      ShowTrailer = false
      IsRatingOpen = false
      IsFriendsMenuOpen = false
      ConfirmingRemove = false
      ShowEventHistory = false
      JellyfinServerUrl = None
      LocalCopyRemoval = None
      Error = None },
    Cmd.batch [
        Cmd.ofMsg (Load_movie slug)
    ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_movie slug ->
        { model with IsLoading = true; Slug = slug; FullCredits = None },
        Cmd.batch [
            Cmd.OfAsync.perform api.getMovie slug Movie_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
            Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded
            Cmd.OfAsync.perform api.getCatalogsForMovie slug Movie_catalogs_loaded
            Cmd.OfAsync.perform api.getJellyfinServerUrl () Jellyfin_server_url_loaded
        ]

    | Movie_loaded movie ->
        let trailerCmd =
            match movie with
            | Some m -> Cmd.OfAsync.perform api.getMovieTrailer m.TmdbId Trailer_loaded
            | None -> Cmd.none
        { model with Movie = movie; IsLoading = false }, trailerCmd

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

    | Confirm_remove_movie ->
        { model with ConfirmingRemove = true }, Cmd.none

    | Cancel_remove_movie ->
        { model with ConfirmingRemove = false }, Cmd.none

    | Remove_movie ->
        { model with ConfirmingRemove = false },
        Cmd.OfAsync.perform (fun () -> api.removeMovie model.Slug) () Movie_removed

    | Movie_removed (Ok ()) ->
        // design-system-fryq7: the Movies list page is gone — a removed
        // movie's detail page has nowhere of its own to return to, so this
        // lands on the Dashboard (its per-media tabs cover what the list
        // page showed).
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "")

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

    | Cancel_edit_session_date ->
        { model with EditingSessionDate = None }, Cmd.none

    | Update_session_date (sessionId, date) ->
        { model with EditingSessionDate = None },
        Cmd.OfAsync.perform (fun () -> api.updateWatchSessionDate model.Slug sessionId date) () Command_result

    | Add_friend_to_session (sessionId, friendSlug) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addFriendToWatchSession model.Slug sessionId friendSlug) () Command_result

    | Remove_friend_from_session (sessionId, friendSlug) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeFriendFromWatchSession model.Slug sessionId friendSlug) () Command_result

    | Remove_watch_session sessionId ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeWatchSession model.Slug sessionId) () Command_result

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

    | Load_full_credits ->
        match model.Movie with
        | Some movie ->
            model,
            Cmd.OfAsync.either (fun () -> api.getFullCredits movie.TmdbId) () Full_credits_loaded (fun ex -> Full_credits_loaded (Error ex.Message))
        | None ->
            model, Cmd.none

    | Full_credits_loaded (Ok credits) ->
        { model with FullCredits = Some credits }, Cmd.none

    | Full_credits_loaded (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Trailer_loaded key ->
        { model with TrailerKey = key }, Cmd.none

    | Open_trailer ->
        { model with ShowTrailer = true }, Cmd.none

    | Close_trailer ->
        { model with ShowTrailer = false }, Cmd.none

    | Toggle_rating_dropdown ->
        { model with IsRatingOpen = not model.IsRatingOpen }, Cmd.none

    | Toggle_friends_menu ->
        { model with IsFriendsMenuOpen = not model.IsFriendsMenuOpen }, Cmd.none

    | Close_friends_menu ->
        { model with IsFriendsMenuOpen = false }, Cmd.none

    | Set_personal_rating rating ->
        let ratingValue = if rating = 0 then None else Some rating
        { model with IsRatingOpen = false },
        Cmd.OfAsync.either
            (fun () -> api.setPersonalRating model.Slug ratingValue)
            ()
            Personal_rating_result
            (fun ex -> Personal_rating_result (Error ex.Message))

    | Personal_rating_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | Personal_rating_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Set_in_focus inFocus ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.setMovieInFocus model.Slug inFocus)
            ()
            In_focus_result
            (fun ex -> In_focus_result (Error ex.Message))

    | In_focus_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getMovie model.Slug Movie_loaded

    | In_focus_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Catalogs_loaded catalogs ->
        { model with AllCatalogs = catalogs }, Cmd.none

    | Movie_catalogs_loaded catalogs ->
        { model with MovieCatalogs = catalogs }, Cmd.none

    | Open_catalog_picker ->
        { model with ShowCatalogPicker = true }, Cmd.none

    | Close_catalog_picker ->
        { model with ShowCatalogPicker = false }, Cmd.none

    | Add_to_catalog catalogSlug ->
        let request: AddCatalogEntryRequest = {
            MediaSlug = model.Slug
            MediaType = MediaType.Movie
            Note = None
        }
        model,
        Cmd.OfAsync.either
            (fun () -> async {
                match! api.addCatalogEntry catalogSlug request with
                | Ok _ -> return Ok ()
                | Error e -> return Error e
            }) () Catalog_result (fun ex -> Catalog_result (Error ex.Message))

    | Remove_from_catalog (catalogSlug, entryId) ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeCatalogEntry catalogSlug entryId)
            () Catalog_result (fun ex -> Catalog_result (Error ex.Message))

    | Create_catalog_and_add name ->
        let request: CreateCatalogRequest = {
            Name = name
            Description = ""
            IsSorted = false
        }
        model,
        Cmd.OfAsync.either
            (fun () -> async {
                match! api.createCatalog request with
                | Ok slug ->
                    let entryReq: AddCatalogEntryRequest = {
                        MediaSlug = model.Slug
                        MediaType = MediaType.Movie
                        Note = None
                    }
                    match! api.addCatalogEntry slug entryReq with
                    | Ok _ -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Catalog_result (fun ex -> Catalog_result (Error ex.Message))

    | Catalog_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded
            Cmd.OfAsync.perform api.getCatalogsForMovie model.Slug Movie_catalogs_loaded
        ]

    | Catalog_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_event_history ->
        { model with ShowEventHistory = true }, Cmd.none

    | Close_event_history ->
        { model with ShowEventHistory = false }, Cmd.none

    | Jellyfin_server_url_loaded url ->
        let serverUrl = if System.String.IsNullOrWhiteSpace(url) then None else Some url
        { model with JellyfinServerUrl = serverUrl }, Cmd.none

    | Open_local_copy_removal ->
        let childModel, childCmd = LocalCopyRemovalDialog.init (localCopyEffects api) (MovieTarget model.Slug)
        { model with LocalCopyRemoval = Some childModel }, Cmd.map Local_copy_removal_msg childCmd

    | Local_copy_removal_msg childMsg ->
        match model.LocalCopyRemoval with
        | None -> model, Cmd.none
        | Some childModel ->
            let updated, childCmd = LocalCopyRemovalDialog.update (localCopyEffects api) childMsg childModel
            { model with LocalCopyRemoval = Some updated }, Cmd.map Local_copy_removal_msg childCmd

    | Close_local_copy_removal ->
        { model with LocalCopyRemoval = None }, Cmd.none

    | Local_copy_removal_done ->
        // "Reload, don't patch" (integration-mqsd3 What) — the projection is
        // the source of truth for "is it on the server", not a client-side
        // patch of JellyfinId to None.
        { model with LocalCopyRemoval = None }, Cmd.ofMsg (Load_movie model.Slug)
