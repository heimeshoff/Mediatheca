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

// Search

type MediaType = Movie | Series

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
    FriendCount: int
    CatalogCount: int
    WatchSessionCount: int
    TotalWatchTimeMinutes: int
    SeriesWatchTimeMinutes: int
}

type RecentActivityItem = {
    Timestamp: string
    StreamId: string
    EventType: string
    Description: string
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

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type IMediathecaApi = {
    healthCheck: unit -> Async<string>
    searchLibrary: string -> Async<LibrarySearchResult list>
    searchTmdb: string -> Async<TmdbSearchResult list>
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
    searchTvSeries: string -> Async<TmdbSearchResult list>
    addSeries: int -> Async<Result<string, string>>
    removeSeries: string -> Async<Result<unit, string>>
    getSeries: unit -> Async<SeriesListItem list>
    getSeriesDetail: string -> string option -> Async<SeriesDetail option>
    setSeriesPersonalRating: string -> int option -> Async<Result<unit, string>>
    addSeriesRecommendation: string -> string -> Async<Result<unit, string>>
    removeSeriesRecommendation: string -> string -> Async<Result<unit, string>>
    addSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>
    removeSeriesWantToWatchWith: string -> string -> Async<Result<unit, string>>
    // Series Rewatch Sessions
    createRewatchSession: string -> CreateRewatchSessionRequest -> Async<Result<string, string>>
    removeRewatchSession: string -> string -> Async<Result<unit, string>>
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
}
