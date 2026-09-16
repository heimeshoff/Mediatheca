module Mediatheca.Client.Pages.BookDetail.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.Progress
open Mediatheca.Client.Pages.BookDetail.Types

let private today () = System.DateTime.Now.ToString("yyyy-MM-dd")

let private defaultProgressDraft (book: BookDetail option) : ProgressDraft =
    let prefilledTotal =
        book
        |> Option.bind (fun b -> b.PageCount)
        |> Option.map string
        |> Option.defaultValue ""
    { Tab = ByPercentTab
      PercentText = ""
      PageText = ""
      TotalText = prefilledTotal
      DateText = today ()
      Error = None }

let init (slug: string) : Model * Cmd<Msg> =
    { Slug = slug
      Book = None
      AllFriends = []
      AllCatalogs = []
      BookCatalogs = []
      ShowCatalogPicker = false
      IsLoading = true
      IsRatingOpen = false
      IsStatusOpen = false
      ShowFriendPicker = false
      ConfirmingRemove = false
      ShowEventHistory = false
      ShowProgressPopover = false
      ProgressDraft = defaultProgressDraft None
      ConfirmingRemoveObservation = None
      ShowFormatPicker = false
      Error = None },
    Cmd.ofMsg (Load_book slug)

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_book slug ->
        { model with IsLoading = true; Slug = slug },
        Cmd.batch [
            Cmd.OfAsync.perform api.getBook slug Book_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
            Cmd.OfAsync.perform api.getCatalogs () Catalogs_loaded
            Cmd.OfAsync.perform api.getCatalogsForBook slug Book_catalogs_loaded
        ]

    | Book_loaded book ->
        { model with Book = book; IsLoading = false }, Cmd.none

    | Friends_loaded friends ->
        { model with AllFriends = friends }, Cmd.none

    | Catalogs_loaded catalogs ->
        { model with AllCatalogs = catalogs }, Cmd.none

    | Book_catalogs_loaded catalogs ->
        { model with BookCatalogs = catalogs }, Cmd.none

    | Open_catalog_picker ->
        { model with ShowCatalogPicker = true }, Cmd.none

    | Close_catalog_picker ->
        { model with ShowCatalogPicker = false }, Cmd.none

    | Add_to_catalog catalogSlug ->
        let request: AddCatalogEntryRequest = {
            MediaSlug = model.Slug
            MediaType = MediaType.Book
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
                        MediaType = MediaType.Book
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
            Cmd.OfAsync.perform api.getCatalogsForBook model.Slug Book_catalogs_loaded
        ]

    | Catalog_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Command_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Command_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Toggle_rating_dropdown ->
        { model with IsRatingOpen = not model.IsRatingOpen }, Cmd.none

    | Set_personal_rating rating ->
        let ratingValue = if rating = 0 then None else Some rating
        { model with IsRatingOpen = false },
        Cmd.OfAsync.either
            (fun () -> api.setBookPersonalRating model.Slug ratingValue)
            ()
            Personal_rating_result
            (fun ex -> Personal_rating_result (Error ex.Message))

    | Personal_rating_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Personal_rating_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Toggle_status_dropdown ->
        { model with IsStatusOpen = not model.IsStatusOpen }, Cmd.none

    | Set_book_status status ->
        // Manual override from the segmented control — no source date to
        // carry, so `effectiveOn = None` (ADR-0077 §1: "the day this event
        // was appended").
        { model with IsStatusOpen = false },
        Cmd.OfAsync.either
            (fun () -> api.setBookStatus model.Slug status None)
            ()
            Status_result
            (fun ex -> Status_result (Error ex.Message))

    | Status_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Status_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_progress_popover ->
        { model with
            ShowProgressPopover = true
            ProgressDraft = defaultProgressDraft model.Book },
        Cmd.none

    | Close_progress_popover ->
        { model with ShowProgressPopover = false }, Cmd.none

    | Set_progress_tab tab ->
        { model with ProgressDraft = { model.ProgressDraft with Tab = tab; Error = None } }, Cmd.none

    | Set_progress_percent_text text ->
        { model with ProgressDraft = { model.ProgressDraft with PercentText = text; Error = None } }, Cmd.none

    | Set_progress_page_text text ->
        { model with ProgressDraft = { model.ProgressDraft with PageText = text; Error = None } }, Cmd.none

    | Set_progress_total_text text ->
        { model with ProgressDraft = { model.ProgressDraft with TotalText = text; Error = None } }, Cmd.none

    | Set_progress_date_text text ->
        { model with ProgressDraft = { model.ProgressDraft with DateText = text; Error = None } }, Cmd.none

    | Submit_progress ->
        let draft = model.ProgressDraft
        let input =
            match draft.Tab with
            | ByPercentTab -> ByPercent draft.PercentText
            | ByPageTab -> ByPage (draft.PageText, draft.TotalText)
        match buildProgressRequest input with
        | Error message ->
            { model with ProgressDraft = { draft with Error = Some message } }, Cmd.none
        | Ok fields ->
            let observedOn =
                if System.String.IsNullOrWhiteSpace draft.DateText then None else Some draft.DateText
            let request: SetReadingProgressRequest = {
                Slug = model.Slug
                Percent = fields.Percent
                Page = fields.Page
                TotalPages = fields.TotalPages
                ObservedOn = observedOn
            }
            model,
            Cmd.OfAsync.either
                (fun () -> api.setBookProgress request)
                ()
                Progress_result
                (fun ex -> Progress_result (Error ex.Message))

    | Progress_result (Ok ()) ->
        { model with ShowProgressPopover = false },
        Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Progress_result (Error err) ->
        { model with ProgressDraft = { model.ProgressDraft with Error = Some err } }, Cmd.none

    | Confirm_remove_observation (observedOn, source) ->
        { model with ConfirmingRemoveObservation = Some (observedOn, source) }, Cmd.none

    | Cancel_remove_observation ->
        { model with ConfirmingRemoveObservation = None }, Cmd.none

    | Remove_observation (observedOn, source) ->
        { model with ConfirmingRemoveObservation = None },
        Cmd.OfAsync.either
            (fun () -> api.removeBookProgressObservation model.Slug observedOn source)
            ()
            Observation_removed
            (fun ex -> Observation_removed (Error ex.Message))

    | Observation_removed (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Observation_removed (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_friend_picker ->
        { model with ShowFriendPicker = true }, Cmd.none

    | Close_friend_picker ->
        { model with ShowFriendPicker = false }, Cmd.none

    | Recommend_friend friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.recommendBookBy model.Slug friendSlug) () Command_result

    | Remove_recommendation friendSlug ->
        model,
        Cmd.OfAsync.perform (fun () -> api.removeBookRecommendation model.Slug friendSlug) () Command_result

    | Add_friend_and_recommend name ->
        model,
        Cmd.OfAsync.perform (fun () ->
            async {
                match! api.addFriend name with
                | Ok slug ->
                    match! api.recommendBookBy model.Slug slug with
                    | Ok () -> return Ok ()
                    | Error e -> return Error e
                | Error e -> return Error e
            }) () Friend_and_recommend_result

    | Friend_and_recommend_result (Ok ()) ->
        model,
        Cmd.batch [
            Cmd.OfAsync.perform api.getBook model.Slug Book_loaded
            Cmd.OfAsync.perform api.getFriends () Friends_loaded
        ]

    | Friend_and_recommend_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Open_event_history ->
        { model with ShowEventHistory = true }, Cmd.none

    | Close_event_history ->
        { model with ShowEventHistory = false }, Cmd.none

    | Open_format_picker ->
        { model with ShowFormatPicker = true }, Cmd.none

    | Close_format_picker ->
        { model with ShowFormatPicker = false }, Cmd.none

    | Set_format format ->
        { model with ShowFormatPicker = false },
        Cmd.OfAsync.either
            (fun () -> api.setBookFormat model.Slug format)
            ()
            Format_result
            (fun ex -> Format_result (Error ex.Message))

    | Format_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Format_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Refresh_metadata ->
        model,
        Cmd.OfAsync.either
            (fun () -> api.refreshBookFromOpenLibrary model.Slug)
            ()
            Refresh_result
            (fun ex -> Refresh_result (Error ex.Message))

    | Refresh_result (Ok ()) ->
        model, Cmd.OfAsync.perform api.getBook model.Slug Book_loaded

    | Refresh_result (Error err) ->
        { model with Error = Some err }, Cmd.none

    | Confirm_remove_book ->
        { model with ConfirmingRemove = true }, Cmd.none

    | Cancel_remove_book ->
        { model with ConfirmingRemove = false }, Cmd.none

    | Remove_book ->
        { model with ConfirmingRemove = false },
        Cmd.OfAsync.perform (fun () -> api.removeBook model.Slug) () Book_removed

    | Book_removed (Ok ()) ->
        // Mirrors design-system-fryq7's Movie_removed handling — there is no
        // Books list page of its own to return to.
        model,
        Cmd.ofEffect (fun _ -> Feliz.Router.Router.navigate "")

    | Book_removed (Error err) ->
        { model with Error = Some err }, Cmd.none
