module Mediatheca.Client.Pages.MovieDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.MovieDetail.Types

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
      ConfirmingRemove = false
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

    | Change_content_block_type (blockId, blockType) ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.changeContentBlockType model.Slug blockId blockType)
            ()
            Content_block_result

    | Reorder_content_blocks blockIds ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.reorderContentBlocks model.Slug None blockIds)
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
            MovieSlug = model.Slug
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
                        MovieSlug = model.Slug
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
