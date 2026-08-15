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
    /// integration-r8kwd: the standing "Steam Web API key rejected" notice —
    /// distinct from a Steam Family reconnect prompt (`SteamNeedsReconnect`),
    /// this is the *other* Steam credential (the Web API key, `key=`), which
    /// fails independently of the family refresh token.
    SteamApiKeyLastError: string option
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
    // Steam Connect (integration-hebjs): the one-time "Connect Steam" QR
    // login that replaces the manual DevTools token scrape as the primary
    // path — mint/refresh then happens automatically server-side
    // (Steam.withTokenRefresh, ADR-0019/ADR-0011-shaped). Manual paste above
    // stays available as a fallback, demoted to a collapsed section in the view.
    SteamConnected: bool
    IsConnectingSteam: bool
    SteamConnectQrDataUrl: string option
    SteamConnectError: string option
    /// Set when a Steam Family fetch/import fails with a "reconnect
    /// required" error (an expired/revoked refresh token, or none stored) —
    /// drives a dedicated "Reconnect Steam" prompt rather than a silent or
    /// generic failure (acceptance criterion 4).
    SteamNeedsReconnect: bool
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
    /// The danger gate (administration-danger-gate): the six sections below
    /// are not rendered at all until the operator types the word "danger"
    /// into the unlock box. Guards against an accidental click on a
    /// destructive, event-sourced recovery action (rebuild, purge, surgery)
    /// that ADR-0034's per-action confirms only catch one step later.
    /// Deliberately model state, not persisted: `Settings.State.init` runs on
    /// every /settings visit (root `Url_changed`), so leaving the page and
    /// coming back re-locks.
    AdminUnlockInput: string
    AdminUnlocked: bool
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
    | Load_steam_api_key_last_error
    | Steam_api_key_last_error_loaded of string option
    // Steam Family
    | Load_steam_family_token
    | Steam_family_token_loaded of string
    | Steam_family_token_input_changed of string
    | Save_steam_family_token
    | Steam_family_token_save_result of Result<unit, string>
    // Steam Connect (integration-hebjs)
    | Load_steam_connect_status
    | Steam_connect_status_loaded of bool
    | Start_steam_connect
    | Steam_connect_qr_received of string
    | Steam_connect_completed of Result<unit, string>
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
    // Administration (administration-k3vmt)
    | Admin_msg of Mediatheca.Client.Pages.Admin.Types.Msg
    /// Typing in the danger gate's unlock box; unlocks as soon as the value
    /// reads "danger" (trimmed, case-insensitive).
    | Admin_unlock_input_changed of string
    /// Re-locks without leaving the page: hides the six sections again,
    /// collapses them, and stops the Events live-tail poll.
    | Lock_admin_sections
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
