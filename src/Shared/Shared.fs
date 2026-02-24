namespace Mediatheca.Shared

open System.Text.RegularExpressions

module Slug =
    let slugify (input: string) =
        input.ToLowerInvariant()
        |> fun s -> Regex.Replace(s, @"[^a-z0-9\s-]", "")
        |> fun s -> Regex.Replace(s, @"[\s]+", "-")
        |> fun s -> Regex.Replace(s, @"-+", "-")
        |> fun s -> s.Trim('-')

    let movieSlug (name: string) (year: int) =
        sprintf "%s-%d" (slugify name) year

    let friendSlug (name: string) =
        slugify name

    let catalogSlug (name: string) =
        slugify name

    let seriesSlug (name: string) (year: int) =
        sprintf "%s-%d" (slugify name) year

    let gameSlug (name: string) (year: int) =
        sprintf "%s-%d" (slugify name) year

// Search

type MediaType = Movie | Series | Game

type LibrarySearchResult = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    MediaType: MediaType
}

// DTOs

type TmdbSearchResult = {
    TmdbId: int
    Title: string
    Year: int option
    Overview: string
    PosterPath: string option
    MediaType: MediaType
}

type CastMemberDto = {
    Name: string
    Role: string
    ImageRef: string option
    TmdbId: int
}

type CrewMemberDto = {
    Name: string
    Job: string
    Department: string
    ImageRef: string option
    TmdbId: int
}

type FullCreditsDto = {
    Cast: CastMemberDto list
    Crew: CrewMemberDto list
}

type FriendRef = {
    Slug: string
    Name: string
    ImageRef: string option
}

type FriendListItem = {
    Slug: string
    Name: string
    ImageRef: string option
}

type FriendDetail = {
    Slug: string
    Name: string
    ImageRef: string option
}

type FriendMediaItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    MediaType: MediaType
}

type FriendWatchedItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Dates: string list
    MediaType: MediaType
}

type FriendMedia = {
    Recommended: FriendMediaItem list
    WantToWatch: FriendMediaItem list
    Watched: FriendWatchedItem list
}

// Watch Sessions

type WatchSessionDto = {
    SessionId: string
    Date: string
    Duration: int option
    Friends: FriendRef list
}

type RecordWatchSessionRequest = {
    Date: string
    FriendSlugs: string list
}

// Content Blocks

type ContentBlockType =
    | TextBlock
    | ImageBlock
    | QuoteBlock
    | CalloutBlock
    | CodeBlock

type ContentBlockDto = {
    BlockId: string
    BlockType: string
    Content: string
    ImageRef: string option
    Url: string option
    Caption: string option
    Position: int
    RowGroup: string option
    RowPosition: int option
}

type AddContentBlockRequest = {
    BlockType: string
    Content: string
    ImageRef: string option
    Url: string option
    Caption: string option
}

type UpdateContentBlockRequest = {
    Content: string
    ImageRef: string option
    Url: string option
    Caption: string option
}

// Catalogs

type CatalogEntryDto = {
    EntryId: string
    MovieSlug: string
    MovieName: string
    MovieYear: int
    MoviePosterRef: string option
    Note: string option
    Position: int
    RoutePrefix: string
}

type CatalogListItem = {
    Slug: string
    Name: string
    Description: string
    IsSorted: bool
    EntryCount: int
}

type CatalogDetail = {
    Slug: string
    Name: string
    Description: string
    IsSorted: bool
    Entries: CatalogEntryDto list
}

type CreateCatalogRequest = {
    Name: string
    Description: string
    IsSorted: bool
}

type UpdateCatalogRequest = {
    Name: string
    Description: string
}

type AddCatalogEntryRequest = {
    MovieSlug: string
    Note: string option
}

type UpdateCatalogEntryRequest = {
    Note: string option
}

type CatalogRef = {
    Slug: string
    Name: string
    EntryId: string
    MovieSlug: string
}

// Dashboard

type DashboardStats = {
    MovieCount: int
    SeriesCount: int
    GameCount: int
    FriendCount: int
    CatalogCount: int
    WatchSessionCount: int
    TotalWatchTimeMinutes: int
    SeriesWatchTimeMinutes: int
    TotalPlayTimeMinutes: int
}

type RecentActivityItem = {
    Timestamp: string
    StreamId: string
    EventType: string
    Description: string
}

// Dashboard Tabs

type DashboardSeriesNextUp = {
    Slug: string
    Name: string
    PosterRef: string option
    BackdropRef: string option
    EpisodeStillRef: string option
    EpisodeOverview: string option
    NextUpSeason: int
    NextUpEpisode: int
    NextUpTitle: string
    WatchWithFriends: FriendRef list
    InFocus: bool
    IsFinished: bool
    IsAbandoned: bool
    LastWatchedDate: string option
    JellyfinEpisodeId: string option
    EpisodeCount: int
    WatchedEpisodeCount: int
    AverageRuntimeMinutes: int option
}

type DashboardMovieInFocus = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    JellyfinId: string option
}

type DashboardGameInFocus = {
    Slug: string
    Name: string
    Year: int
    CoverRef: string option
}

type DashboardGameRecentlyPlayed = {
    Slug: string
    Name: string
    CoverRef: string option
    TotalPlayTimeMinutes: int
    LastPlayedDate: string
    HltbHours: float option
}

type DashboardPlaySession = {
    GameSlug: string
    GameName: string
    CoverRef: string option
    Date: string
    MinutesPlayed: int
}

type DashboardNewGame = {
    Slug: string
    Name: string
    Year: int
    CoverRef: string option
    AddedDate: string
    FamilyOwners: FriendRef list
}

type DashboardAllTab = {
    SeriesNextUp: DashboardSeriesNextUp list
    MoviesInFocus: DashboardMovieInFocus list
    GamesInFocus: DashboardGameInFocus list
    GamesRecentlyPlayed: DashboardGameRecentlyPlayed list
    PlaySessions: DashboardPlaySession list
    NewGames: DashboardNewGame list
    JellyfinServerUrl: string option
}

type DashboardMovieStats = {
    TotalMovies: int
    TotalWatchSessions: int
    TotalWatchTimeMinutes: int
    AverageRating: float option
    WatchlistCount: int
    RatingDistribution: (int * int) list
    GenreDistribution: (string * int) list
    MonthlyActivity: (string * int * int) list
    CountryDistribution: (string * int) list
}

type DashboardRecentlyWatched = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    WatchDate: string
    Friends: string list
}

type DashboardPersonStats = {
    Name: string
    ImageRef: string option
    MovieCount: int
}

type DashboardWatchedWithStats = {
    Slug: string
    Name: string
    ImageRef: string option
    SessionCount: int
}

// Event Store Browser

type EventDto = {
    GlobalPosition: int64
    StreamId: string
    StreamPosition: int64
    EventType: string
    Data: string
    Timestamp: string
}

type EventQuery = {
    StreamFilter: string option
    EventTypeFilter: string option
    Limit: int
    Offset: int
}

// Movie DTOs (after WatchSession and ContentBlock since they reference those types)

type MovieListItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Genres: string list
    TmdbRating: float option
    InFocus: bool
}

type MovieDetail = {
    Slug: string
    Name: string
    Year: int
    Runtime: int option
    Overview: string
    Genres: string list
    PosterRef: string option
    BackdropRef: string option
    TmdbId: int
    TmdbRating: float option
    PersonalRating: int option
    InFocus: bool
    JellyfinId: string option
    Cast: CastMemberDto list
    RecommendedBy: FriendRef list
    WantToWatchWith: FriendRef list
    WatchSessions: WatchSessionDto list
    ContentBlocks: ContentBlockDto list
}

// TV Series

type SeriesStatus =
    | Returning
    | Ended
    | Canceled
    | InProduction
    | Planned
    | UnknownStatus

type EpisodeDto = {
    EpisodeNumber: int
    Name: string
    Overview: string
    Runtime: int option
    AirDate: string option
    StillRef: string option
    TmdbRating: float option
    IsWatched: bool
    WatchedDate: string option
}

type SeasonDto = {
    SeasonNumber: int
    Name: string
    Overview: string
    PosterRef: string option
    AirDate: string option
    Episodes: EpisodeDto list
    WatchedCount: int
    OverallWatchedCount: int
}

type RewatchSessionDto = {
    RewatchId: string
    Name: string option
    IsDefault: bool
    Friends: FriendRef list
    WatchedCount: int
    TotalEpisodes: int
    CompletionPercentage: float
}

type NextUpDto = {
    SeasonNumber: int
    EpisodeNumber: int
    EpisodeName: string
}

type RecentSeriesItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    NextUp: NextUpDto option
    WatchedEpisodeCount: int
    EpisodeCount: int
}

type SeriesListItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Genres: string list
    TmdbRating: float option
    Status: SeriesStatus
    SeasonCount: int
    EpisodeCount: int
    WatchedEpisodeCount: int
    NextUp: NextUpDto option
    IsAbandoned: bool
    InFocus: bool
}

type SeriesDetail = {
    Slug: string
    Name: string
    Year: int
    Overview: string
    Genres: string list
    Status: SeriesStatus
    PosterRef: string option
    BackdropRef: string option
    TmdbId: int
    TmdbRating: float option
    EpisodeRuntime: int option
    PersonalRating: int option
    IsAbandoned: bool
    InFocus: bool
    Cast: CastMemberDto list
    RecommendedBy: FriendRef list
    WantToWatchWith: FriendRef list
    Seasons: SeasonDto list
    RewatchSessions: RewatchSessionDto list
    ContentBlocks: ContentBlockDto list
}

// Series Request Types

type CreateRewatchSessionRequest = {
    Name: string option
    FriendSlugs: string list
}

type MarkEpisodeWatchedRequest = {
    RewatchId: string
    SeasonNumber: int
    EpisodeNumber: int
    Date: string
}

type MarkEpisodeUnwatchedRequest = {
    RewatchId: string
    SeasonNumber: int
    EpisodeNumber: int
}

type MarkSeasonWatchedRequest = {
    RewatchId: string
    SeasonNumber: int
    Date: string
}

type MarkEpisodesUpToRequest = {
    RewatchId: string
    SeasonNumber: int
    EpisodeNumber: int
    Date: string
}

type MarkSeasonUnwatchedRequest = {
    RewatchId: string
    SeasonNumber: int
}

type UpdateEpisodeWatchedDateRequest = {
    RewatchId: string
    SeasonNumber: int
    EpisodeNumber: int
    Date: string
}

// Games

type GameStatus =
    | Backlog
    | InFocus
    | Playing
    | Completed
    | Abandoned
    | OnHold
    | Dismissed

type RawgSearchResult = {
    RawgId: int
    Name: string
    Year: int option
    BackgroundImage: string option
    Rating: float option
    Genres: string list
}

type GameListItem = {
    Slug: string
    Name: string
    Year: int
    CoverRef: string option
    Genres: string list
    Status: GameStatus
    TotalPlayTimeMinutes: int
    HltbHours: float option
    PersonalRating: int option
    RawgRating: float option
}

type GameDetail = {
    Slug: string
    Name: string
    Year: int
    Description: string
    ShortDescription: string
    WebsiteUrl: string option
    CoverRef: string option
    BackdropRef: string option
    Genres: string list
    Status: GameStatus
    RawgId: int option
    RawgRating: float option
    HltbHours: float option
    HltbMainPlusHours: float option
    HltbCompletionistHours: float option
    PersonalRating: int option
    SteamAppId: int option
    SteamLibraryDate: string option
    SteamLastPlayed: string option
    TotalPlayTimeMinutes: int
    PlayModes: string list
    IsOwnedByMe: bool
    FamilyOwners: FriendRef list
    RecommendedBy: FriendRef list
    WantToPlayWith: FriendRef list
    PlayedWith: FriendRef list
    ContentBlocks: ContentBlockDto list
}

type AddGameRequest = {
    Name: string
    Year: int
    Genres: string list
    Description: string
    CoverRef: string option
    BackdropRef: string option
    RawgId: int option
    RawgRating: float option
}

// Dashboard Tabs (continued — types that reference MovieListItem / SeriesListItem / GameListItem)

type DashboardMoviesTab = {
    RecentlyAdded: MovieListItem list
    Stats: DashboardMovieStats
    RecentlyWatched: DashboardRecentlyWatched list
    TopActors: DashboardPersonStats list
    TopDirectors: DashboardPersonStats list
    TopWatchedWith: DashboardWatchedWithStats list
}

type DashboardEpisodeActivity = {
    Date: string
    SeriesName: string
    SeriesSlug: string
    EpisodeCount: int
}

type DashboardSeriesWatchedWith = {
    Slug: string
    Name: string
    ImageRef: string option
    EpisodeCount: int
}

type DashboardSeriesStats = {
    TotalSeries: int
    TotalEpisodesWatched: int
    TotalWatchTimeMinutes: int
    CurrentlyWatching: int
    AverageRating: float option
    CompletionRate: float option
    RatingDistribution: (int * int) list
    GenreDistribution: (string * int) list
    MonthlyActivity: (string * int) list
}

type DashboardSeriesTab = {
    NextUp: DashboardSeriesNextUp list
    RecentlyFinished: SeriesListItem list
    RecentlyAbandoned: SeriesListItem list
    Stats: DashboardSeriesStats
    EpisodeActivity: DashboardEpisodeActivity list
    TopWatchedWith: DashboardSeriesWatchedWith list
}

type DashboardGameStats = {
    TotalGames: int
    TotalPlayTimeMinutes: int
    GamesCompleted: int
    GamesInProgress: int
    BacklogSize: int
    CompletionRate: float option
    AverageRating: float option
    BacklogTimeHours: float
    BacklogGameCount: int
    BacklogGamesWithoutHltb: int
    StatusDistribution: (string * int) list
    RatingDistribution: (int * int) list
    GenreDistribution: (string * int) list
    MonthlyPlayTime: (string * int) list
    CompletedPerYear: (int * int) list
}

type DashboardHltbComparison = {
    Slug: string
    Name: string
    CoverRef: string option
    PlayMinutes: int
    HltbMainHours: float
}

type DashboardGamesTab = {
    RecentlyAdded: GameListItem list
    RecentlyPlayed: DashboardGameRecentlyPlayed list
    Stats: DashboardGameStats
    HltbComparisons: DashboardHltbComparison list
}

// Steam Integration

type SteamAchievement = {
    GameName: string
    GameAppId: int
    AchievementName: string
    AchievementDescription: string
    IconUrl: string option
    UnlockTime: string
}

type SteamOwnedGame = {
    AppId: int
    Name: string
    PlaytimeMinutes: int
    ImgIconUrl: string
    RtimeLastPlayed: int
}

type SteamImportResult = {
    GamesMatched: int
    GamesCreated: int
    PlayTimeUpdated: int
    Errors: string list
}

type SteamFamilyMember = {
    SteamId: string
    DisplayName: string
    FriendSlug: string option
    IsMe: bool
}

type SteamFamilyImportResult = {
    FamilyMembers: int
    GamesProcessed: int
    GamesCreated: int
    FamilyOwnersSet: int
    Errors: string list
}

type SteamFamilyImportProgress = {
    Current: int
    Total: int
    GameName: string
    Action: string
}

// Playtime Tracking

type PlaySessionDto = {
    GameSlug: string
    Date: string
    MinutesPlayed: int
}

type PlaytimeSummaryItem = {
    GameSlug: string
    GameName: string
    CoverRef: string option
    TotalMinutes: int
    SessionCount: int
}

type PlaytimeSyncResult = {
    SessionsRecorded: int
    SnapshotsUpdated: int
}

type PlaytimeSyncStatus = {
    LastSyncTime: string option
    NextSyncTime: string option
    IsEnabled: bool
    SyncHourUtc: int
}

type GameImageCandidate = {
    Url: string
    Source: string
    Label: string
    IsCover: bool
    IsCurrent: bool
}

type GameTrailerInfo = {
    VideoUrl: string
    ThumbnailUrl: string option
    Title: string option
}

// Jellyfin Integration

type JellyfinItemType =
    | JellyfinMovie
    | JellyfinSeries

type JellyfinItem = {
    JellyfinId: string
    Name: string
    Year: int option
    ItemType: JellyfinItemType
    TmdbId: int option
    Played: bool
    PlayCount: int
    LastPlayedDate: string option
}

type JellyfinMatchedItem = {
    JellyfinItem: JellyfinItem
    MediathecaSlug: string
    MediathecaName: string
    HasExistingWatchData: bool
}

type JellyfinScanResult = {
    MatchedMovies: JellyfinMatchedItem list
    MatchedSeries: JellyfinMatchedItem list
    UnmatchedMovies: JellyfinItem list
    UnmatchedSeries: JellyfinItem list
}

type JellyfinImportResult = {
    MoviesAdded: int
    EpisodesAdded: int
    MoviesAutoAdded: int
    SeriesAutoAdded: int
    ItemsSkipped: int
    Errors: string list
}

// View Settings

type ViewSortField = ByReleaseDate | ByName | ByRating | ByWatchOrder
type ViewSortDirection = Ascending | Descending
type ViewLayout = Gallery | List
type ViewGallerySize = Normal | Medium

type ViewSettings = {
    SortField: ViewSortField
    SortDirection: ViewSortDirection
    Layout: ViewLayout
    GallerySize: ViewGallerySize
}

// Import

type ImportFromCinemarcoRequest = {
    DatabasePath: string
    ImagesPath: string
}

type ImportResult = {
    FriendsImported: int
    MoviesImported: int
    SeriesImported: int
    EpisodesWatched: int
    CatalogsImported: int
    ContentBlocksImported: int
    ImagesCopied: int
    Errors: string list
}

// Event History

type EventHistoryEntry = {
    Timestamp: string
    Label: string
    Details: string list
}

// Preview Data Types (for search hover preview)

type TmdbPreviewData = {
    Title: string
    Year: int option
    Overview: string
    Genres: string list
    PosterPath: string option
    BackdropPath: string option
    Cast: string list
    Runtime: int option
    SeasonCount: int option
    Rating: float option
}

type RawgPreviewData = {
    Name: string
    Year: int option
    Description: string
    Genres: string list
    BackgroundImage: string option
    Screenshots: string list
    Rating: float option
    Metacritic: int option
    Platforms: string list
}

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type IMediathecaApi = {
    healthCheck: unit -> Async<string>
    searchLibrary: string -> Async<LibrarySearchResult list>
    searchTmdb: string * int option -> Async<TmdbSearchResult list>
    addMovie: int -> Async<Result<string, string>>
    removeMovie: string -> Async<Result<unit, string>>
    getMovie: string -> Async<MovieDetail option>
    getMovies: unit -> Async<MovieListItem list>
    categorizeMovie: string -> string list -> Async<Result<unit, string>>
    replacePoster: string -> string -> Async<Result<unit, string>>
    replaceBackdrop: string -> string -> Async<Result<unit, string>>
    recommendMovie: string -> string -> Async<Result<unit, string>>
    removeRecommendation: string -> string -> Async<Result<unit, string>>
    wantToWatchWith: string -> string -> Async<Result<unit, string>>
    removeWantToWatchWith: string -> string -> Async<Result<unit, string>>
    setPersonalRating: string -> int option -> Async<Result<unit, string>>
    setMovieInFocus: string -> bool -> Async<Result<unit, string>>
    addFriend: string -> Async<Result<string, string>>
    updateFriend: string -> string -> string option -> Async<Result<unit, string>>
    removeFriend: string -> Async<Result<unit, string>>
    getFriend: string -> Async<FriendDetail option>
    getFriendMedia: string -> Async<FriendMedia>
    getFriends: unit -> Async<FriendListItem list>
    uploadFriendImage: string -> byte array -> string -> Async<Result<string, string>>
    // Watch Sessions
    recordWatchSession: string -> RecordWatchSessionRequest -> Async<Result<string, string>>
    updateWatchSessionDate: string -> string -> string -> Async<Result<unit, string>>
    addFriendToWatchSession: string -> string -> string -> Async<Result<unit, string>>
    removeFriendFromWatchSession: string -> string -> string -> Async<Result<unit, string>>
    removeWatchSession: string -> string -> Async<Result<unit, string>>
    getWatchSessions: string -> Async<WatchSessionDto list>
    // Content Blocks
    addContentBlock: string -> string option -> AddContentBlockRequest -> Async<Result<string, string>>
    updateContentBlock: string -> string -> UpdateContentBlockRequest -> Async<Result<unit, string>>
    removeContentBlock: string -> string -> Async<Result<unit, string>>
    changeContentBlockType: string -> string -> string -> Async<Result<unit, string>>
    reorderContentBlocks: string -> string option -> string list -> Async<Result<unit, string>>
    getContentBlocks: string -> string option -> Async<ContentBlockDto list>
    groupContentBlocksInRow: string -> string -> string -> string -> Async<Result<unit, string>>
    ungroupContentBlock: string -> string -> Async<Result<unit, string>>
    uploadContentImage: byte array -> string -> Async<Result<string, string>>
    // Catalogs
    createCatalog: CreateCatalogRequest -> Async<Result<string, string>>
    updateCatalog: string -> UpdateCatalogRequest -> Async<Result<unit, string>>
    removeCatalog: string -> Async<Result<unit, string>>
    getCatalog: string -> Async<CatalogDetail option>
    getCatalogs: unit -> Async<CatalogListItem list>
    addCatalogEntry: string -> AddCatalogEntryRequest -> Async<Result<string, string>>
    updateCatalogEntry: string -> string -> UpdateCatalogEntryRequest -> Async<Result<unit, string>>
    removeCatalogEntry: string -> string -> Async<Result<unit, string>>
    reorderCatalogEntries: string -> string list -> Async<Result<unit, string>>
    getCatalogsForMovie: string -> Async<CatalogRef list>
    // Dashboard
    getDashboardStats: unit -> Async<DashboardStats>
    getRecentSeries: int -> Async<RecentSeriesItem list>
    getRecentActivity: int -> Async<RecentActivityItem list>
    getDashboardAllTab: unit -> Async<DashboardAllTab>
    getDashboardMoviesTab: unit -> Async<DashboardMoviesTab>
    getDashboardSeriesTab: unit -> Async<DashboardSeriesTab>
    getDashboardGamesTab: unit -> Async<DashboardGamesTab>
    // Event Store Browser
    getEvents: EventQuery -> Async<EventDto list>
    getEventStreams: unit -> Async<string list>
    getEventTypes: unit -> Async<string list>
    // Settings
    getTmdbApiKey: unit -> Async<string>
    setTmdbApiKey: string -> Async<Result<unit, string>>
    testTmdbApiKey: string -> Async<Result<unit, string>>
    getFullCredits: int -> Async<Result<FullCreditsDto, string>>
    getMovieTrailer: int -> Async<string option>
    getSeriesTrailer: int -> Async<string option>
    getSeasonTrailer: int -> int -> Async<string option>
    // TV Series
    searchTvSeries: string * int option -> Async<TmdbSearchResult list>
    addSeries: int -> Async<Result<string, string>>
    removeSeries: string -> Async<Result<unit, string>>
    abandonSeries: string -> Async<Result<unit, string>>
    unabandonSeries: string -> Async<Result<unit, string>>
    getSeries: unit -> Async<SeriesListItem list>
    getSeriesDetail: string -> string option -> Async<SeriesDetail option>
    setSeriesPersonalRating: string -> int option -> Async<Result<unit, string>>
    setSeriesInFocus: string -> bool -> Async<Result<unit, string>>
    addSeriesRecommendation: string -> string -> Async<Result<unit, string>>
    removeSeriesRecommendation: string -> string -> Async<Result<unit, string>>
    addSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>
    removeSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>
    // Series Rewatch Sessions
    createRewatchSession: string -> CreateRewatchSessionRequest -> Async<Result<string, string>>
    removeRewatchSession: string -> string -> Async<Result<unit, string>>
    setDefaultRewatchSession: string -> string -> Async<Result<unit, string>>
    addFriendToRewatchSession: string -> string -> string -> Async<Result<unit, string>>
    removeFriendFromRewatchSession: string -> string -> string -> Async<Result<unit, string>>
    // Series Episode Progress
    markEpisodeWatched: string -> MarkEpisodeWatchedRequest -> Async<Result<unit, string>>
    markEpisodeUnwatched: string -> MarkEpisodeUnwatchedRequest -> Async<Result<unit, string>>
    markSeasonWatched: string -> MarkSeasonWatchedRequest -> Async<Result<unit, string>>
    markEpisodesWatchedUpTo: string -> MarkEpisodesUpToRequest -> Async<Result<unit, string>>
    markSeasonUnwatched: string -> MarkSeasonUnwatchedRequest -> Async<Result<unit, string>>
    updateEpisodeWatchedDate: string -> UpdateEpisodeWatchedDateRequest -> Async<Result<unit, string>>
    // Series Content Blocks + Catalogs
    getSeriesContentBlocks: string -> Async<ContentBlockDto list>
    addSeriesContentBlock: string -> AddContentBlockRequest -> Async<Result<string, string>>
    updateSeriesContentBlock: string -> string -> UpdateContentBlockRequest -> Async<Result<unit, string>>
    removeSeriesContentBlock: string -> string -> Async<Result<unit, string>>
    getCatalogsForSeries: string -> Async<CatalogRef list>
    // Games
    searchRawgGames: string * int option -> Async<RawgSearchResult list>
    addGame: AddGameRequest -> Async<Result<string, string>>
    removeGame: string -> Async<Result<unit, string>>
    getGames: unit -> Async<GameListItem list>
    getGameDetail: string -> Async<GameDetail option>
    setGameStatus: string -> GameStatus -> Async<Result<unit, string>>
    setGamePersonalRating: string -> int option -> Async<Result<unit, string>>
    setGameHltbHours: string -> float option -> Async<Result<unit, string>>
    addGameRecommendation: string -> string -> Async<Result<unit, string>>
    removeGameRecommendation: string -> string -> Async<Result<unit, string>>
    addGameWantToPlayWith: string -> string -> Async<Result<unit, string>>
    removeGameWantToPlayWith: string -> string -> Async<Result<unit, string>>
    markGameAsOwned: string -> Async<Result<unit, string>>
    removeGameOwnership: string -> Async<Result<unit, string>>
    addGameFamilyOwner: string -> string -> Async<Result<unit, string>>
    removeGameFamilyOwner: string -> string -> Async<Result<unit, string>>
    addGamePlayedWith: string -> string -> Async<Result<unit, string>>
    removeGamePlayedWith: string -> string -> Async<Result<unit, string>>
    addGamePlayMode: string -> string -> Async<Result<unit, string>>
    removeGamePlayMode: string -> string -> Async<Result<unit, string>>
    getAllPlayModes: unit -> Async<string list>
    getGameContentBlocks: string -> Async<ContentBlockDto list>
    addGameContentBlock: string -> AddContentBlockRequest -> Async<Result<string, string>>
    updateGameContentBlock: string -> string -> UpdateContentBlockRequest -> Async<Result<unit, string>>
    removeGameContentBlock: string -> string -> Async<Result<unit, string>>
    getCatalogsForGame: string -> Async<CatalogRef list>
    getGameImageCandidates: string -> Async<GameImageCandidate list>
    selectGameImage: string -> string -> string -> Async<Result<unit, string>>
    getGameTrailer: string -> Async<GameTrailerInfo option>
    // Games Settings
    getRawgApiKey: unit -> Async<string>
    setRawgApiKey: string -> Async<Result<unit, string>>
    testRawgApiKey: string -> Async<Result<unit, string>>
    // Steam Integration
    getSteamApiKey: unit -> Async<string>
    setSteamApiKey: string -> Async<Result<unit, string>>
    testSteamApiKey: string -> Async<Result<unit, string>>
    getSteamId: unit -> Async<string>
    setSteamId: string -> Async<Result<unit, string>>
    resolveSteamVanityUrl: string -> Async<Result<string, string>>
    importSteamLibrary: unit -> Async<Result<SteamImportResult, string>>
    getSteamFamilyToken: unit -> Async<string>
    setSteamFamilyToken: string -> Async<Result<unit, string>>
    getSteamFamilyMembers: unit -> Async<SteamFamilyMember list>
    setSteamFamilyMembers: SteamFamilyMember list -> Async<Result<unit, string>>
    fetchSteamFamilyMembers: unit -> Async<Result<SteamFamilyMember list, string>>
    importSteamFamily: unit -> Async<Result<SteamFamilyImportResult, string>>
    // Jellyfin Integration
    getJellyfinServerUrl: unit -> Async<string>
    setJellyfinServerUrl: string -> Async<Result<unit, string>>
    getJellyfinUsername: unit -> Async<string>
    setJellyfinCredentials: string * string -> Async<Result<unit, string>>
    testJellyfinConnection: string * string * string -> Async<Result<string, string>>
    scanJellyfinLibrary: unit -> Async<Result<JellyfinScanResult, string>>
    importJellyfinWatchHistory: unit -> Async<Result<JellyfinImportResult, string>>
    // Import
    importFromCinemarco: ImportFromCinemarcoRequest -> Async<Result<ImportResult, string>>
    // View Settings
    getViewSettings: string -> Async<ViewSettings option>
    saveViewSettings: string -> ViewSettings -> Async<unit>
    getCollapsedSections: string -> Async<string list>
    saveCollapsedSections: string -> string list -> Async<unit>
    // Playtime Tracking
    getGamePlaySessions: string -> Async<PlaySessionDto list>
    getPlaytimeSummary: string -> string -> Async<PlaytimeSummaryItem list>
    getPlaytimeSyncStatus: unit -> Async<PlaytimeSyncStatus>
    triggerPlaytimeSync: unit -> Async<Result<PlaytimeSyncResult, string>>
    // Steam Achievements
    getSteamRecentAchievements: unit -> Async<Result<SteamAchievement list, string>>
    // HowLongToBeat
    fetchHltbData: string -> Async<Result<float option, string>>
    // Event History
    getStreamEvents: string -> Async<EventHistoryEntry list>
    // Search Preview
    previewTmdbMovie: int -> Async<TmdbPreviewData option>
    previewTmdbSeries: int -> Async<TmdbPreviewData option>
    previewRawgGame: int -> Async<RawgPreviewData option>
}
