module Mediatheca.Client.Pages.SeriesDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.SeriesDetail.Types

let private activeRewatchId (model: Model) : string =
    match model.SelectedRewatchId with
    | Some id -> id
    | None ->
        model.Detail
        |> Option.bind (fun d -> d.RewatchSessions |> List.tryFind (fun s -> s.IsDefault))
        |> Option.map (fun s -> s.RewatchId)
        |> Option.defaultValue "default"

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Detail = None
      IsLoading = true
      ActiveTab = Overview
      SelectedSeason = 1
      SelectedRewatchId = None
      IsRatingOpen = false
      IsFriendsMenuOpen = false
      ShowFriendPicker = None
      Friends = []
      AllCatalogs = []
      SeriesCatalogs = []
      ShowCatalogPicker = None
      EditingEpisodeDate = None
      TrailerKey = None
      SeasonTrailerKeys = Map.empty
      ShowTrailer = None
      SessionMenuOpen = None
      ConfirmingRemove = false
      Error = None },
    Cmd.batch [
        Cmd.ofMsg Load_detail
    ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_detail ->
        { model with IsLoading = true },
        Cmd.batch [
            Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
            Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded
            Cmd.OfAsync.perform api.getCatalogsForSeries model.Slug Series_catalogs_loaded
        ]

    | Detail_loaded detail ->
        let trailerCmd =
            match detail with
            | Some series ->
                Cmd.batch [
                    Cmd.OfAsync.perform api.getSeriesTrailer series.TmdbId Trailer_loaded
                    for season in series.Seasons do
                        Cmd.OfAsync.perform
                            (fun () -> api.getSeasonTrailer series.TmdbId season.SeasonNumber)
                            ()
                            (fun key -> Season_trailer_loaded (season.SeasonNumber, key))
                ]
            | None -> Cmd.none
        { model with Detail = detail; IsLoading = false }, trailerCmd

    | Set_tab tab ->
        { model with ActiveTab = tab }, Cmd.none

    | Select_season seasonNumber ->
        { model with SelectedSeason = seasonNumber }, Cmd.none

    | Toggle_session_menu rewatchId ->
        let newMenu =
            if model.SessionMenuOpen = Some rewatchId then None
            else Some rewatchId
        { model with SessionMenuOpen = newMenu }, Cmd.none

    | Close_session_menu ->
        { model with SessionMenuOpen = None }, Cmd.none

    | Select_rewatch rewatchId ->
        { model with SelectedRewatchId = Some rewatchId; SessionMenuOpen = None }, Cmd.ofMsg Load_detail

    | Toggle_episode_watched (seasonNumber, episodeNumber, isCurrentlyWatched) ->
        let rewatchId = activeRewatchId model
        if isCurrentlyWatched then
            let request: MarkEpisodeUnwatchedRequest = {
                RewatchId = rewatchId
                SeasonNumber = seasonNumber
                EpisodeNumber = episodeNumber
            }
            model,
            Cmd.OfAsync.either
                (fun () -> api.markEpisodeUnwatched model.Slug request)
                () Episode_toggled (fun ex -> Episode_toggled (Error ex.Message))
        else
            let today = System.DateTime.Now.ToString("yyyy-MM-dd")
            let request: MarkEpisodeWatchedRequest = {
                RewatchId = rewatchId
                SeasonNumber = seasonNumber
                EpisodeNumber = episodeNumber
                Date = today
            }
            model,
            Cmd.OfAsync.either
                (fun () -> api.markEpisodeWatched model.Slug request)
                () Episode_toggled (fun ex -> Episode_toggled (Error ex.Message))

    | Episode_toggled (Ok ()) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded

    | Episode_toggled (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Mark_season_watched seasonNumber ->
        let today = System.DateTime.Now.ToString("yyyy-MM-dd")
        let request: MarkSeasonWatchedRequest = {
            RewatchId = activeRewatchId model
            SeasonNumber = seasonNumber
            Date = today
        }
        model,
        Cmd.OfAsync.either
            (fun () -> api.markSeasonWatched model.Slug request)
            () Season_marked (fun ex -> Season_marked (Error ex.Message))

    | Season_marked (Ok ()) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded

    | Season_marked (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Mark_season_unwatched seasonNumber ->
        let request: MarkSeasonUnwatchedRequest = {
            RewatchId = activeRewatchId model
            SeasonNumber = seasonNumber
        }
        model,
        Cmd.OfAsync.either
            (fun () -> api.markSeasonUnwatched model.Slug request)
            () Season_unmarked (fun ex -> Season_unmarked (Error ex.Message))

    | Season_unmarked (Ok ()) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded

    | Season_unmarked (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Edit_episode_date (seasonNumber, episodeNumber) ->
        { model with EditingEpisodeDate = Some (seasonNumber, episodeNumber) }, Cmd.none

    | Cancel_edit_episode_date ->
        { model with EditingEpisodeDate = None }, Cmd.none

    | Update_episode_date (seasonNumber, episodeNumber, date) ->
        let request: UpdateEpisodeWatchedDateRequest = {
            RewatchId = activeRewatchId model
            SeasonNumber = seasonNumber
            EpisodeNumber = episodeNumber
            Date = date
        }
        { model with EditingEpisodeDate = None },
        Cmd.OfAsync.either
            (fun () -> api.updateEpisodeWatchedDate model.Slug request)
            () Episode_date_updated (fun ex -> Episode_date_updated (Error ex.Message))

    | Episode_date_updated (Ok ()) ->
        model,
        Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded

    | Episode_date_updated (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Create_rewatch_session ->
        let request: CreateRewatchSessionRequest = {
            Name = None
            FriendSlugs = []
        }
        model,
        Cmd.OfAsync.either
            (fun () -> api.createRewatchSession model.Slug request)
            () Rewatch_session_created (fun ex -> Rewatch_session_created (Error ex.Message))

    | Rewatch_session_created (Ok rewatchId) ->
        { model with SelectedRewatchId = Some rewatchId }, Cmd.ofMsg Load_detail

    | Rewatch_session_created (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Remove_rewatch_session rewatchId ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeRewatchSession model.Slug rewatchId)
            () Rewatch_session_removed (fun ex -> Rewatch_session_removed (Error ex.Message))

    | Rewatch_session_removed (Ok ()) ->
        { model with SelectedRewatchId = None }, Cmd.ofMsg Load_detail

    | Rewatch_session_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Set_default_rewatch_session rewatchId ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.setDefaultRewatchSession model.Slug rewatchId)
            () Default_rewatch_session_set (fun ex -> Default_rewatch_session_set (Error ex.Message))

    | Default_rewatch_session_set (Ok ()) ->
        model, Cmd.ofMsg Load_detail

    | Default_rewatch_session_set (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_rewatch_friend (rewatchId, friendSlug) ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.addFriendToRewatchSession model.Slug rewatchId friendSlug)
            () Rewatch_friend_result (fun ex -> Rewatch_friend_result (Error ex.Message))

    | Remove_rewatch_friend (rewatchId, friendSlug) ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeFriendFromRewatchSession model.Slug rewatchId friendSlug)
            () Rewatch_friend_result (fun ex -> Rewatch_friend_result (Error ex.Message))

    | Add_friend_and_add_to_session (rewatchId, name) ->
        model,
        Cmd.OfAsync.either (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addFriendToRewatchSession model.Slug rewatchId slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Rewatch_friend_result (fun ex -> Rewatch_friend_result (Error ex.Message))

    | Rewatch_friend_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform (fun () -> api.getSeriesDetail model.Slug model.SelectedRewatchId) () Detail_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Rewatch_friend_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Toggle_rating_dropdown ->
        { model with IsRatingOpen = not model.IsRatingOpen }, Cmd.none

    | Toggle_friends_menu ->
        { model with IsFriendsMenuOpen = not model.IsFriendsMenuOpen }, Cmd.none

    | Close_friends_menu ->
        { model with IsFriendsMenuOpen = false }, Cmd.none

    | Set_rating rating ->
        let ratingValue = if rating = 0 then None else Some rating
        { model with IsRatingOpen = false },
        Cmd.OfAsync.either
            (fun () -> api.setSeriesPersonalRating model.Slug ratingValue)
            () Rating_set (fun ex -> Rating_set (Error ex.Message))

    | Rating_set (Ok ()) ->
        model, Cmd.ofMsg Load_detail

    | Rating_set (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_friend_picker kind ->
        { model with ShowFriendPicker = Some kind }, Cmd.none

    | Close_friend_picker ->
        { model with ShowFriendPicker = None }, Cmd.none

    | Friends_loaded friends ->
        { model with Friends = friends }, Cmd.none

    | Add_recommendation friendSlug ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.addSeriesRecommendation model.Slug friendSlug)
            () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Remove_recommendation friendSlug ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeSeriesRecommendation model.Slug friendSlug)
            () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Add_watch_with friendSlug ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.addSeriesWantToWatchWith model.Slug friendSlug)
            () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Remove_watch_with friendSlug ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.removeSeriesWantToWatchWith model.Slug friendSlug)
            () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Add_friend_and_recommend name ->
        model,
        Cmd.OfAsync.either (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addSeriesRecommendation model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Add_friend_and_watch_with name ->
        model,
        Cmd.OfAsync.either (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addSeriesWantToWatchWith model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Social_updated (fun ex -> Social_updated (Error ex.Message))

    | Social_updated (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.ofMsg Load_detail
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Social_updated (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_content_block request ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.addSeriesContentBlock model.Slug request)
            ()
            (fun result -> Content_block_result (result |> Result.map ignore))

    | Update_content_block (blockId, request) ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.updateSeriesContentBlock model.Slug blockId request)
            ()
            Content_block_result

    | Remove_content_block blockId ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.removeSeriesContentBlock model.Slug blockId)
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

    | Upload_screenshot (data, filename, insertBefore) ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.uploadContentImage data filename)
            ()
            (fun r -> Screenshot_uploaded (r, insertBefore))
            (fun ex -> Screenshot_uploaded (Error ex.Message, insertBefore))

    | Screenshot_uploaded (Ok imageRef, insertBefore) ->
        let req : AddContentBlockRequest = {
            BlockType = "screenshot"
            Content = ""
            ImageRef = Some imageRef
            Url = None
            Caption = None
        }
        model,
        Cmd.OfAsync.either
            (fun () -> async {
                match! api.addSeriesContentBlock model.Slug req with
                | Ok newBlockId ->
                    match insertBefore with
                    | Some targetId ->
                        match! api.getSeriesDetail model.Slug model.SelectedRewatchId with
                        | Some s ->
                            let sorted = s.ContentBlocks |> List.sortBy (fun b -> b.Position)
                            let ids = sorted |> List.map (fun b -> b.BlockId)
                            let withoutNew = ids |> List.filter (fun id -> id <> newBlockId)
                            let newOrder =
                                withoutNew
                                |> List.collect (fun id ->
                                    if id = targetId then [newBlockId; id]
                                    else [id])
                            let! _ = api.reorderContentBlocks model.Slug None newOrder
                            return Ok ()
                        | None -> return Ok ()
                    | None -> return Ok ()
                | Error e -> return Error e
            }) () Content_block_result (fun ex -> Content_block_result (Error ex.Message))

    | Screenshot_uploaded (Error err, _) ->
        { model with Error = Some err }, Cmd.none

    | Group_content_blocks (leftId, rightId) ->
        let rowGroup = System.Guid.NewGuid().ToString("N")
        model,
        Cmd.OfAsync.perform
            (fun () -> api.groupContentBlocksInRow model.Slug leftId rightId rowGroup)
            ()
            Content_block_result

    | Ungroup_content_block blockId ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.ungroupContentBlock model.Slug blockId)
            ()
            Content_block_result

    | Content_block_result (Ok _) ->
        model, Cmd.ofMsg Load_detail

    | Content_block_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Catalogs_loaded catalogs ->
        { model with AllCatalogs = catalogs }, Cmd.none

    | Series_catalogs_loaded catalogs ->
        { model with SeriesCatalogs = catalogs }, Cmd.none

    | Open_catalog_picker target ->
        { model with ShowCatalogPicker = Some target }, Cmd.none

    | Close_catalog_picker ->
        { model with ShowCatalogPicker = None }, Cmd.none

    | Add_to_catalog catalogSlug ->
        let entrySlug =
            match model.ShowCatalogPicker with
            | Some (Season_catalog sn) -> $"{model.Slug}:s%02d{sn}"
            | Some (Episode_catalog (sn, en)) -> $"{model.Slug}:s%02d{sn}e%02d{en}"
            | _ -> model.Slug
        let request: AddCatalogEntryRequest = {
            MovieSlug = entrySlug
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
        let entrySlug =
            match model.ShowCatalogPicker with
            | Some (Season_catalog sn) -> $"{model.Slug}:s%02d{sn}"
            | Some (Episode_catalog (sn, en)) -> $"{model.Slug}:s%02d{sn}e%02d{en}"
            | _ -> model.Slug
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
                        MovieSlug = entrySlug
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
            Cmd.OfAsync.perform api.getCatalogsForSeries model.Slug Series_catalogs_loaded
        ]

    | Catalog_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Trailer_loaded key ->
        { model with TrailerKey = key }, Cmd.none

    | Season_trailer_loaded (seasonNumber, key) ->
        match key with
        | Some k ->
            { model with SeasonTrailerKeys = model.SeasonTrailerKeys |> Map.add seasonNumber k }, Cmd.none
        | None -> model, Cmd.none

    | Open_trailer key ->
        { model with ShowTrailer = Some key }, Cmd.none

    | Close_trailer ->
        { model with ShowTrailer = None }, Cmd.none

    | Toggle_abandon_series ->
        let isAbandoned =
            model.Detail |> Option.map (fun d -> d.IsAbandoned) |> Option.defaultValue false
        let apiCall =
            if isAbandoned then api.unabandonSeries model.Slug
            else api.abandonSeries model.Slug
        model,
        Cmd.OfAsync.either
            (fun () -> apiCall)
            () Series_abandon_toggled (fun ex -> Series_abandon_toggled (Error ex.Message))

    | Series_abandon_toggled (Ok ()) ->
        model, Cmd.ofMsg Load_detail

    | Series_abandon_toggled (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Confirm_remove_series ->
        { model with ConfirmingRemove = true }, Cmd.none

    | Cancel_remove_series ->
        { model with ConfirmingRemove = false }, Cmd.none

    | Remove_series ->
        { model with ConfirmingRemove = false },
        Cmd.OfAsync.either
            (fun () -> api.removeSeries model.Slug)
            () Series_removed (fun ex -> Series_removed (Error ex.Message))

    | Series_removed (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "series")

    | Series_removed (Error err) ->
        { model with Error = Some err }, Cmd.none
