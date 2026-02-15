module Mediatheca.Client.Pages.Settings.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types

let init () : Model * Cmd<Msg> =
    { TmdbApiKey = ""
      TmdbKeyInput = ""
      IsTesting = false
      IsSaving = false
      TestResult = None
      SaveResult = None
      RawgApiKey = ""
      RawgKeyInput = ""
      IsTestingRawg = false
      IsSavingRawg = false
      RawgTestResult = None
      RawgSaveResult = None
      CinemarcoDbPath = ""
      CinemarcoImagesPath = ""
      IsImporting = false
      ImportResult = None },
    Cmd.batch [ Cmd.ofMsg Load_tmdb_key; Cmd.ofMsg Load_rawg_key ]

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load_tmdb_key ->
        model, Cmd.OfAsync.perform api.getTmdbApiKey () Tmdb_key_loaded

    | Tmdb_key_loaded key ->
        { model with TmdbApiKey = key }, Cmd.none

    | Tmdb_key_input_changed value ->
        { model with TmdbKeyInput = value; TestResult = None; SaveResult = None }, Cmd.none

    | Test_tmdb_key ->
        { model with IsTesting = true; TestResult = None },
        Cmd.OfAsync.either api.testTmdbApiKey model.TmdbKeyInput
            Test_result
            (fun ex -> Test_result (Error ex.Message))

    | Test_result result ->
        let testResult =
            match result with
            | Ok () -> Ok "Connection successful"
            | Error e -> Error e
        { model with IsTesting = false; TestResult = Some testResult }, Cmd.none

    | Save_tmdb_key ->
        { model with IsSaving = true; SaveResult = None },
        Cmd.OfAsync.either api.setTmdbApiKey model.TmdbKeyInput
            Save_result
            (fun ex -> Save_result (Error ex.Message))

    | Save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "API key saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_tmdb_key
            | Error _ -> Cmd.none
        { model with IsSaving = false; SaveResult = Some saveResult }, cmd

    | Load_rawg_key ->
        model, Cmd.OfAsync.perform api.getRawgApiKey () Rawg_key_loaded

    | Rawg_key_loaded key ->
        { model with RawgApiKey = key }, Cmd.none

    | Rawg_key_input_changed value ->
        { model with RawgKeyInput = value; RawgTestResult = None; RawgSaveResult = None }, Cmd.none

    | Test_rawg_key ->
        { model with IsTestingRawg = true; RawgTestResult = None },
        Cmd.OfAsync.either api.testRawgApiKey model.RawgKeyInput
            Rawg_test_result
            (fun ex -> Rawg_test_result (Error ex.Message))

    | Rawg_test_result result ->
        let testResult =
            match result with
            | Ok () -> Ok "Connection successful"
            | Error e -> Error e
        { model with IsTestingRawg = false; RawgTestResult = Some testResult }, Cmd.none

    | Save_rawg_key ->
        { model with IsSavingRawg = true; RawgSaveResult = None },
        Cmd.OfAsync.either api.setRawgApiKey model.RawgKeyInput
            Rawg_save_result
            (fun ex -> Rawg_save_result (Error ex.Message))

    | Rawg_save_result result ->
        let saveResult =
            match result with
            | Ok () -> Ok "API key saved"
            | Error e -> Error e
        let cmd =
            match result with
            | Ok () -> Cmd.ofMsg Load_rawg_key
            | Error _ -> Cmd.none
        { model with IsSavingRawg = false; RawgSaveResult = Some saveResult }, cmd

    | Cinemarco_db_path_changed value ->
        { model with CinemarcoDbPath = value; ImportResult = None }, Cmd.none

    | Cinemarco_images_path_changed value ->
        { model with CinemarcoImagesPath = value; ImportResult = None }, Cmd.none

    | Start_cinemarco_import ->
        let request: ImportFromCinemarcoRequest = {
            DatabasePath = model.CinemarcoDbPath
            ImagesPath = model.CinemarcoImagesPath
        }
        { model with IsImporting = true; ImportResult = None },
        Cmd.OfAsync.either api.importFromCinemarco request
            Import_completed
            (fun ex -> Import_completed (Error ex.Message))

    | Import_completed result ->
        { model with IsImporting = false; ImportResult = Some result }, Cmd.none
