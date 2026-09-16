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
    /// Set when a Steam Family fetch/import fails with a "family token
    /// rejected" error (integration-v0xmv, ADR-0070) — drives a dedicated
    /// "paste a fresh token" warning rather than a silent or generic
    /// failure. Cleared on a successful token save.
    SteamFamilyTokenRejected: bool
    SteamFamilyMembers: SteamFamilyMember list
    Friends: FriendListItem list
    IsFetchingFamilyMembers: bool
    FetchFamilyMembersResult: Result<string, string> option
    IsImportingSteamFamily: bool
    SteamFamilyImportResult: Result<SteamFamilyImportResult, string> option
    ImportProgress: SteamFamilyImportProgress option
    ImportLog: (string * string) list
    /// integration-n3vqa: "Re-enrich all family games" — the explicit
    /// second action that reproduces the old always-fetch-everything
    /// behaviour, kept distinct from `IsImportingSteamFamily` so the two
    /// actions' buttons/progress never get confused for one another.
    IsReenrichingSteamFamily: bool
    /// The last completed import's persisted result, loaded once on mount
    /// (`getSteamFamilyLastResult`) — kept separate from
    /// `SteamFamilyImportResult` (which tracks THIS session's fresh
    /// click) so a reload showing the last result never hides the "Import
    /// Family Library" button behind a stale "already done" state.
    SteamFamilyLastPersistedResult: SteamFamilyImportResult option
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
    // qBittorrent Integration (integration-qb7tk): credentials + a "Test
    // connection" round-trip, ahead of any destructive flow
    // (integration-r4vzm/mqsd3 build "Remove local copy" on the server-side
    // adapter this card's Save button configures). Same Elmish shape as the
    // Jellyfin card above, but Test and Save are two distinct actions/results
    // rather than Jellyfin's combined "Test & Save".
    QbittorrentUrl: string
    QbittorrentUrlInput: string
    QbittorrentUsername: string
    QbittorrentUsernameInput: string
    QbittorrentPasswordInput: string
    IsTestingQbittorrent: bool
    IsSavingQbittorrent: bool
    QbittorrentTestResult: Result<string, string> option
    QbittorrentSaveResult: Result<string, string> option
    // Audible Integration (integration-dhctm, ADR-0074): an imported
    // audible-cli auth file, never a login. `AudibleAuthFileInput` is the
    // paste textarea's own buffer -- never re-populated from a stored file
    // (ADR-0074 point 6, the file is a secret): a save clears it back to "".
    AudibleConfigured: bool
    AudibleCustomerName: string option
    AudibleMarketplace: string
    AudibleLastError: string option
    AudibleAuthFileInput: string
    IsSavingAudible: bool
    IsTestingAudible: bool
    IsClearingAudible: bool
    AudibleSaveResult: Result<string, string> option
    AudibleTestResult: Result<string, string> option
    // Audible library import + daily progress sync (integration-jjvg2,
    // ADR-0074/ADR-0076/ADR-0026). `AudibleLastImportResult`/
    // `AudibleLastSync`/`AudibleLastSyncResult` are the persisted (not
    // in-memory) summaries `getAudibleSyncStatus` reads back after a reload;
    // `AudibleImportResult`/`AudibleProgressSyncResult` are this SESSION's
    // fresh outcome for the success/error alert, the same
    // session-vs-persisted split `GoodreadsSyncResult`/`GoodreadsLastResult`
    // already establish.
    IsImportingAudibleLibrary: bool
    AudibleImportResult: Result<AudibleImportResult, string> option
    IsSyncingAudibleProgress: bool
    AudibleProgressSyncResult: Result<AudibleProgressSyncResult, string> option
    AudibleLastImportResult: string option
    AudibleLastSync: string option
    AudibleLastSyncResult: string option
    // Goodreads Integration (integration-wmqn3, ADR-0075): the user's PUBLIC
    // Goodreads user id (no developer key exists any more, no cookie ever).
    // `GoodreadsUserIdInput` is the profile-URL-or-bare-id text box;
    // `GoodreadsReadShelfOptedIn`/`GoodreadsToReadShelfOptedIn` back the two
    // opt-in shelf checkboxes (`currently-reading` is always on, no state
    // needed for it).
    GoodreadsUserId: string option
    GoodreadsUserIdInput: string
    GoodreadsReadShelfOptedIn: bool
    GoodreadsToReadShelfOptedIn: bool
    GoodreadsLastSync: string option
    GoodreadsLastResult: string option
    GoodreadsLastError: string option
    IsSavingGoodreads: bool
    IsTestingGoodreads: bool
    IsSyncingGoodreads: bool
    GoodreadsSaveResult: Result<string, string> option
    GoodreadsTestResult: Result<string, string> option
    GoodreadsSyncResult: Result<GoodreadsSyncResult, string> option
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
    /// integration-n3vqa: "Re-enrich all family games" — the explicit
    /// second action, streamed the same way as the default import but
    /// against `/api/stream/reenrich-steam-family`.
    | Reenrich_steam_family
    | Steam_family_reenrich_completed of Result<SteamFamilyImportResult, string>
    | Load_steam_family_last_result
    | Steam_family_last_result_loaded of SteamFamilyImportResult option
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
    // qBittorrent Integration (integration-qb7tk)
    | Load_qbittorrent_settings
    | Qbittorrent_settings_loaded of QbittorrentSettings
    | Qbittorrent_url_input_changed of string
    | Qbittorrent_username_input_changed of string
    | Qbittorrent_password_input_changed of string
    | Test_qbittorrent_connection
    | Qbittorrent_test_result of Result<string, string>
    | Save_qbittorrent_settings
    | Qbittorrent_save_result of Result<unit, string>
    // Audible Integration (integration-dhctm, ADR-0074)
    | Load_audible_status
    | Audible_status_loaded of AudibleStatus
    | Audible_auth_file_input_changed of string
    | Audible_marketplace_changed of string
    | Save_audible_auth_file
    | Audible_save_result of Result<AudibleStatus, string>
    | Test_audible_connection
    | Audible_test_result of Result<string, string>
    | Clear_audible_auth_file
    | Audible_cleared
    // Audible library import + daily progress sync (integration-jjvg2,
    // ADR-0074/ADR-0076/ADR-0026)
    | Load_audible_sync_status
    | Audible_sync_status_loaded of AudibleSyncStatus
    | Import_audible_library
    | Audible_import_completed of Result<AudibleImportResult, string>
    | Sync_audible_progress_now
    | Audible_progress_sync_completed of Result<AudibleProgressSyncResult, string>
    // Goodreads Integration (integration-wmqn3, ADR-0075)
    | Load_goodreads_settings
    | Goodreads_settings_loaded of GoodreadsSettings
    | Goodreads_user_id_input_changed of string
    | Save_goodreads_user_id
    | Goodreads_save_result of Result<string, string>
    | Toggle_goodreads_read_shelf
    | Toggle_goodreads_to_read_shelf
    | Test_goodreads_connection
    | Goodreads_test_result of Result<string, string>
    | Sync_goodreads_now
    | Goodreads_sync_completed of Result<GoodreadsSyncResult, string>
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
