module Mediatheca.Client.Pages.Settings.Types

open Mediatheca.Shared

type Model = {
    TmdbApiKey: string
    TmdbKeyInput: string
    IsTesting: bool
    IsSaving: bool
    TestResult: Result<string, string> option
    SaveResult: Result<string, string> option
    // RAWG Integration
    RawgApiKey: string
    RawgKeyInput: string
    IsTestingRawg: bool
    IsSavingRawg: bool
    RawgTestResult: Result<string, string> option
    RawgSaveResult: Result<string, string> option
    // Cinemarco Import
    CinemarcoDbPath: string
    CinemarcoImagesPath: string
    IsImporting: bool
    ImportResult: Result<ImportResult, string> option
}

type Msg =
    | Load_tmdb_key
    | Tmdb_key_loaded of string
    | Tmdb_key_input_changed of string
    | Test_tmdb_key
    | Test_result of Result<unit, string>
    | Save_tmdb_key
    | Save_result of Result<unit, string>
    // RAWG Integration
    | Load_rawg_key
    | Rawg_key_loaded of string
    | Rawg_key_input_changed of string
    | Test_rawg_key
    | Rawg_test_result of Result<unit, string>
    | Save_rawg_key
    | Rawg_save_result of Result<unit, string>
    // Cinemarco Import
    | Cinemarco_db_path_changed of string
    | Cinemarco_images_path_changed of string
    | Start_cinemarco_import
    | Import_completed of Result<ImportResult, string>
