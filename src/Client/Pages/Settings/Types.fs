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
    // Steam Integration
    SteamApiKey: string
    SteamKeyInput: string
    IsTestingSteam: bool
    IsSavingSteam: bool
    SteamTestResult: Result<string, string> option
    SteamSaveResult: Result<string, string> option
    SteamId: string
    SteamIdInput: string
    IsSavingSteamId: bool
    SteamIdSaveResult: Result<string, string> option
    IsResolvingVanity: bool
    VanityInput: string
    VanityResult: Result<string, string> option
    IsImportingSteam: bool
    SteamImportResult: Result<SteamImportResult, string> option
    // Steam Family
    SteamFamilyToken: string
    SteamFamilyTokenInput: string
    IsSavingFamilyToken: bool
    FamilyTokenSaveResult: Result<string, string> option
    SteamFamilyMembers: SteamFamilyMember list
    Friends: FriendListItem list
    IsFetchingFamilyMembers: bool
    FetchFamilyMembersResult: Result<string, string> option
    IsImportingSteamFamily: bool
    SteamFamilyImportResult: Result<SteamFamilyImportResult, string> option
    ImportProgress: SteamFamilyImportProgress option
    ImportLog: (string * string) list
    // Jellyfin Integration
    JellyfinServerUrl: string
    JellyfinServerUrlInput: string
    JellyfinUsername: string
    JellyfinUsernameInput: string
    JellyfinPasswordInput: string
    IsTestingJellyfin: bool
    IsSavingJellyfin: bool
    JellyfinTestResult: Result<string, string> option
    JellyfinSaveResult: Result<string, string> option
    IsScanningJellyfin: bool
    JellyfinScanResult: Result<JellyfinScanResult, string> option
    IsImportingJellyfin: bool
    JellyfinImportResult: Result<JellyfinImportResult, string> option
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
    // Steam Integration
    | Load_steam_key
    | Steam_key_loaded of string
    | Steam_key_input_changed of string
    | Test_steam_key
    | Steam_test_result of Result<unit, string>
    | Save_steam_key
    | Steam_save_result of Result<unit, string>
    | Load_steam_id
    | Steam_id_loaded of string
    | Steam_id_input_changed of string
    | Save_steam_id
    | Steam_id_save_result of Result<unit, string>
    | Vanity_input_changed of string
    | Resolve_vanity_url
    | Vanity_resolved of Result<string, string>
    | Import_steam_library
    | Steam_import_completed of Result<SteamImportResult, string>
    // Steam Family
    | Load_steam_family_token
    | Steam_family_token_loaded of string
    | Steam_family_token_input_changed of string
    | Save_steam_family_token
    | Steam_family_token_save_result of Result<unit, string>
    | Load_steam_family_members
    | Steam_family_members_loaded of SteamFamilyMember list
    | Fetch_steam_family_members
    | Steam_family_members_fetched of Result<SteamFamilyMember list, string>
    | Load_friends
    | Friends_loaded of FriendListItem list
    | Update_family_member_friend of steamId: string * friendSlug: string option
    | Save_steam_family_members
    | Steam_family_members_save_result of Result<unit, string>
    | Import_steam_family
    | Steam_family_import_progress of SteamFamilyImportProgress
    | Steam_family_import_completed of Result<SteamFamilyImportResult, string>
    // Jellyfin Integration
    | Load_jellyfin_settings
    | Jellyfin_settings_loaded of serverUrl: string * username: string
    | Jellyfin_server_url_input_changed of string
    | Jellyfin_username_input_changed of string
    | Jellyfin_password_input_changed of string
    | Test_jellyfin_connection
    | Jellyfin_test_result of Result<string, string>
    | Save_jellyfin_settings
    | Jellyfin_save_result of Result<unit, string>
    | Scan_jellyfin_library
    | Jellyfin_scan_completed of Result<JellyfinScanResult, string>
    | Import_jellyfin_watch_history
    | Jellyfin_import_completed of Result<JellyfinImportResult, string>
    // Cinemarco Import
    | Cinemarco_db_path_changed of string
    | Cinemarco_images_path_changed of string
    | Start_cinemarco_import
    | Import_completed of Result<ImportResult, string>
