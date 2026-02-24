module Mediatheca.Client.Pages.GameDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.GameDetail.Types

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Game = None
      AllFriends = []
      AllCatalogs = []
      GameCatalogs = []
      ShowCatalogPicker = false
      IsLoading = true
      ShowFriendPicker = None
      IsRatingOpen = false
      IsStatusOpen = false
      ShowPlayModePicker = false
      AllPlayModes = []
      IsDescriptionExpanded = false
      IsFriendsMenuOpen = false
      ConfirmingRemove = false
      ShowImagePicker = None
      ImageCandidates = []
      IsLoadingImages = false
      IsSelectingImage = false
      ImageVersion = 0
      ActiveTab = Overview
      PlaySessions = []
      HltbFetching = false
      HltbNoData = false
      TrailerInfo = None
      ShowTrailer = false
      IsLoadingTrailer = false
      ShowEventHistory = false
      Error = None },
    Cmd.batch [
        Cmd.ofMsg (Load_game slug)
    ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Set_tab tab ->
        { model with ActiveTab = tab }, Cmd.none

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
                match! api.addGameContentBlock model.Slug req with
                | Ok newBlockId ->
                    match insertBefore with
                    | Some targetId ->
                        match! api.getGameDetail model.Slug with
                        | Some g ->
                            let sorted = g.ContentBlocks |> List.sortBy (fun b -> b.Position)
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

    | Load_game slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getGameDetail slug Game_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
            Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded
            Cmd.OfAsync.perform api.getCatalogsForGame slug Game_catalogs_loaded
            Cmd.OfAsync.perform api.getAllPlayModes () Play_modes_loaded
            Cmd.OfAsync.perform api.getGamePlaySessions slug Play_sessions_loaded
        ]

    | Game_loaded game ->
        let trailerCmd =
            match game with
            | Some _ -> Cmd.ofMsg Load_trailer
            | None -> Cmd.none
        { model with Game = game; IsLoading = false }, trailerCmd

    | Friends_loaded friends ->
        { model with AllFriends = friends }, Cmd.none

    | Recommend_friend friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addGameRecommendation model.Slug friendSlug) () Command_result

    | Remove_recommendation friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeGameRecommendation model.Slug friendSlug) () Command_result

    | Want_to_play_with friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addGameWantToPlayWith model.Slug friendSlug) () Command_result

    | Remove_want_to_play_with friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeGameWantToPlayWith model.Slug friendSlug) () Command_result

    | Add_played_with friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addGamePlayedWith model.Slug friendSlug) () Command_result

    | Remove_played_with friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeGamePlayedWith model.Slug friendSlug) () Command_result

    | Add_family_owner friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.addGameFamilyOwner model.Slug friendSlug) () Command_result

    | Remove_family_owner friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeGameFamilyOwner model.Slug friendSlug) () Command_result

    | Toggle_ownership ->
        match model.Game with
        | Some game ->
            if game.IsOwnedByMe then
                model,
                Cmd.OfAsync.perform (fun () -> api.removeGameOwnership model.Slug) () Command_result
            else
                model,
                Cmd.OfAsync.perform (fun () -> api.markGameAsOwned model.Slug) () Command_result
        | None -> model, Cmd.none

    | Command_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded
            Cmd.OfAsync.perform api.getAllPlayModes () Play_modes_loaded
        ]

    | Command_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_friend_picker kind ->
        { model with ShowFriendPicker = Some kind }, Cmd.none

    | Close_friend_picker ->
        { model with ShowFriendPicker = None }, Cmd.none

    | Set_game_status status ->
        { model with IsStatusOpen = false },
        Cmd.OfAsync.perform (fun () -> api.setGameStatus model.Slug status) () Command_result

    | Toggle_status_dropdown ->
        { model with IsStatusOpen = not model.IsStatusOpen }, Cmd.none

    | Toggle_rating_dropdown ->
        { model with IsRatingOpen = not model.IsRatingOpen }, Cmd.none

    | Set_personal_rating rating ->
        let ratingValue = if rating = 0 then None else Some rating
        { model with IsRatingOpen = false },
        Cmd.OfAsync.either
            (fun () -> api.setGamePersonalRating model.Slug ratingValue)
            ()
            Personal_rating_result
            (fun ex -> Personal_rating_result (Error ex.Message))

    | Personal_rating_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded

    | Personal_rating_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Toggle_friends_menu ->
        { model with IsFriendsMenuOpen = not model.IsFriendsMenuOpen }, Cmd.none

    | Close_friends_menu ->
        { model with IsFriendsMenuOpen = false }, Cmd.none

    | Add_friend_and_recommend name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addGameRecommendation model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_recommend_result

    | Friend_and_recommend_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_recommend_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_friend_and_play_with name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addGameWantToPlayWith model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_play_with_result

    | Friend_and_play_with_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_play_with_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_friend_and_played_with name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.addGamePlayedWith model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_played_with_result

    | Friend_and_played_with_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_played_with_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Add_content_block request ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.addGameContentBlock model.Slug request)
            ()
            (fun result -> Content_block_result (result |> Result.map ignore))

    | Update_content_block (blockId, request) ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.updateGameContentBlock model.Slug blockId request)
            ()
            Content_block_result

    | Remove_content_block blockId ->
        model,
        Cmd.OfAsync.perform
            (fun () -> api.removeGameContentBlock model.Slug blockId)
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
        model, Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded

    | Content_block_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Catalogs_loaded catalogs ->
        { model with AllCatalogs = catalogs }, Cmd.none

    | Game_catalogs_loaded catalogs ->
        { model with GameCatalogs = catalogs }, Cmd.none

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
            Cmd.OfAsync.perform api.getCatalogsForGame model.Slug Game_catalogs_loaded
        ]

    | Catalog_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_image_picker kind ->
        { model with ShowImagePicker = Some kind; IsLoadingImages = true; ImageCandidates = [] },
        Cmd.OfAsync.either
            (fun () -> api.getGameImageCandidates model.Slug)
            ()
            Image_candidates_loaded
            (fun _ -> Image_candidates_loaded [])

    | Close_image_picker ->
        { model with ShowImagePicker = None; ImageCandidates = []; IsLoadingImages = false; IsSelectingImage = false }, Cmd.none

    | Image_candidates_loaded candidates ->
        { model with ImageCandidates = candidates; IsLoadingImages = false }, Cmd.none

    | Select_image url ->
        let imageKind =
            match model.ShowImagePicker with
            | Some Cover_picker -> "cover"
            | _ -> "backdrop"
        { model with IsSelectingImage = true },
        Cmd.OfAsync.either
            (fun () -> api.selectGameImage model.Slug url imageKind)
            ()
            Image_selected
            (fun ex -> Image_selected (Error ex.Message))

    | Image_selected (Ok ()) ->
        { model with ShowImagePicker = None; ImageCandidates = []; IsSelectingImage = false; ImageVersion = model.ImageVersion + 1 },
        Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded

    | Image_selected (Error err) ->
        { model with IsSelectingImage = false; Error = Some err }, Cmd.none

    | Toggle_description_expanded ->
        { model with IsDescriptionExpanded = not model.IsDescriptionExpanded }, Cmd.none

    | Add_play_mode playMode ->
        { model with ShowPlayModePicker = false },
        Cmd.OfAsync.perform (fun () -> api.addGamePlayMode model.Slug playMode) () Command_result

    | Remove_play_mode playMode ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeGamePlayMode model.Slug playMode) () Command_result

    | Toggle_play_mode_picker ->
        { model with ShowPlayModePicker = not model.ShowPlayModePicker }, Cmd.none

    | Play_modes_loaded playModes ->
        { model with AllPlayModes = playModes }, Cmd.none

    | Confirm_remove_game ->
        { model with ConfirmingRemove = true }, Cmd.none

    | Cancel_remove_game ->
        { model with ConfirmingRemove = false }, Cmd.none

    | Remove_game ->
        { model with ConfirmingRemove = false },
        Cmd.OfAsync.perform (fun () -> api.removeGame model.Slug) () Game_removed

    | Play_sessions_loaded sessions ->
        { model with PlaySessions = sessions }, Cmd.none

    | Fetch_hltb ->
        { model with HltbFetching = true; HltbNoData = false },
        Cmd.OfAsync.either
            (fun () -> api.fetchHltbData model.Slug)
            ()
            Hltb_fetched
            (fun ex -> Hltb_fetched (Error ex.Message))

    | Hltb_fetched (Ok (Some _hours)) ->
        { model with HltbFetching = false; HltbNoData = false },
        Cmd.OfAsync.perform api.getGameDetail model.Slug Game_loaded

    | Hltb_fetched (Ok None) ->
        { model with HltbFetching = false; HltbNoData = true }, Cmd.none

    | Hltb_fetched (Error err) ->
        { model with HltbFetching = false; Error = Some err }, Cmd.none

    | Game_removed (Ok ()) ->
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "games")

    | Game_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Load_trailer ->
        { model with IsLoadingTrailer = true },
        Cmd.OfAsync.either
            (fun () -> api.getGameTrailer model.Slug)
            ()
            Trailer_loaded
            (fun _ -> Trailer_loaded None)

    | Trailer_loaded info ->
        { model with TrailerInfo = info; IsLoadingTrailer = false }, Cmd.none

    | Open_trailer ->
        { model with ShowTrailer = true }, Cmd.none

    | Close_trailer ->
        { model with ShowTrailer = false }, Cmd.none

    | Open_event_history ->
        { model with ShowEventHistory = true }, Cmd.none

    | Close_event_history ->
        { model with ShowEventHistory = false }, Cmd.none
