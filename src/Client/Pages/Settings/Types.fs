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
    // Sync Status
    PlaytimeSyncStatus: PlaytimeSyncStatus option
    JellyfinLastSyncTime: string option
    JellyfinSyncStatus: JellyfinSyncStatus option
    SteamFamilyLastSync: string option
    // Cinemarco Import
    CinemarcoDbPath: string
    CinemarcoImagesPath: string
    IsImporting: bool
    ImportResult: Result<ImportResult, string> option
    // Administration (administration-k3vmt): the former /admin console's six
    // tabs, dissolved into inline collapsible sections below Data Imports.
    // `AdminModel` is the headless composite child (Pages/Admin) unchanged
    // in shape; the twelve Open/Loaded pairs below are Settings' own state —
    // each section starts collapsed and unloaded, and issues its one load
    // message on first expand only (never on re-expand). Projections is the
    // one deliberate exception: `Url_changed`'s Settings branch (not this
    // page's own `init`) fires its load unconditionally on every /settings
    // visit regardless of collapse state, since the ADR-0034 dirty banner is
    // client-derived from it and must react even if the operator never opens
    // that section.
    AdminModel: Mediatheca.Client.Pages.Admin.Types.Model
    EventsSectionOpen: bool
    EventsSectionLoaded: bool
    ProjectionsSectionOpen: bool
    ProjectionsSectionLoaded: bool
    HealthSectionOpen: bool
    HealthSectionLoaded: bool
    ImagesSectionOpen: bool
    ImagesSectionLoaded: bool
    JobsSectionOpen: bool
    JobsSectionLoaded: bool
    SurgerySectionOpen: bool
    SurgerySectionLoaded: bool
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
    // Sync Status
    | Load_playtime_sync_status
    | Playtime_sync_status_loaded of PlaytimeSyncStatus
    | Load_jellyfin_sync_status
    | Jellyfin_sync_status_loaded of JellyfinSyncStatus
    | Load_steam_family_last_sync
    | Steam_family_last_sync_loaded of string option
    // Cinemarco Import
    | Cinemarco_db_path_changed of string
    | Cinemarco_images_path_changed of string
    | Start_cinemarco_import
    | Import_completed of Result<ImportResult, string>
    // Administration (administration-k3vmt)
    | Admin_msg of Mediatheca.Client.Pages.Admin.Types.Msg
    | Toggle_events_section
    | Toggle_projections_section
    | Toggle_health_section
    | Toggle_images_section
    | Toggle_jobs_section
    | Toggle_surgery_section
    /// The dirty banner's "Go to Projections" affordance (in-page, replacing
    /// the old `/admin/projections` navigation): expands the Projections
    /// section (a no-op if already open) and scrolls it into view.
    | Go_to_projections_section
