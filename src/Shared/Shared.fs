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

type CropSettings = {
    OffsetX: float
    OffsetY: float
    Zoom: float
}

type FriendDetail = {
    Slug: string
    Name: string
    ImageRef: string option
    CropSettings: CropSettings option
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

// Game Journal (Notion-style block document; plain storage, not event-sourced)
//
// The document is a flat list of blocks forming a tree via ParentId:
//   - root blocks (ParentId = None) stack vertically
//   - "columnList" blocks contain "column" blocks (side-by-side, Width = flex ratio)
//   - "column" blocks contain content blocks
//   - "toggle" blocks contain their collapsible children
// Position orders siblings within one parent. The whole document is saved at once.

module JournalBlockTypes =
    let text = "text"
    let heading1 = "h1"
    let heading2 = "h2"
    let heading3 = "h3"
    let heading4 = "h4"
    let bullet = "bullet"
    let numbered = "numbered"
    let todo = "todo"
    let toggle = "toggle"
    let quote = "quote"
    let callout = "callout"
    let code = "code"
    let link = "link"
    let image = "image"
    let columnList = "columnList"
    let column = "column"

type JournalBlockDto = {
    Id: string
    ParentId: string option
    BlockType: string
    Content: string
    Checked: bool
    Collapsed: bool
    Language: string option
    Url: string option
    ImageRef: string option
    Caption: string option
    Position: int
    Width: float
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

type DashboardMovieToWatch = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    JellyfinId: string option
    InFocus: bool
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

type DashboardCrossMediaStats = {
    TotalMovieMinutes: int
    TotalSeriesMinutes: int
    TotalGameMinutes: int
    MoviesWatchedThisYear: int
    EpisodesWatchedThisYear: int
    GamesBeatenThisYear: int
    MoviesWatchedThisMonth: int
    EpisodesWatchedThisMonth: int
    GamesPlayedThisMonth: int
    ActiveSeriesCount: int
    ActiveGamesCount: int
    WeekMovieCount: int
    WeekEpisodeCount: int
    WeekGameMinutes: int
}

type DashboardActivityDay = {
    Date: string
    MovieSessions: int
    EpisodesWatched: int
    GameSessions: int
}

type DashboardMonthlyBreakdown = {
    Month: string
    MovieMinutes: int
    SeriesMinutes: int
    GameMinutes: int
}

type DashboardAllTab = {
    SeriesNextUp: DashboardSeriesNextUp list
    MoviesToWatch: DashboardMovieToWatch list
    GamesInFocus: DashboardGameInFocus list
    GamesRecentlyPlayed: DashboardGameRecentlyPlayed list
    PlaySessions: DashboardPlaySession list
    NewGames: DashboardNewGame list
    JellyfinServerUrl: string option
    CrossMediaStats: DashboardCrossMediaStats
    ActivityDays: DashboardActivityDay list
    MonthlyBreakdown: DashboardMonthlyBreakdown list
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

/// Composable filter over the event log (administration-g5dfy). Shared, as-is,
/// by the paged event-explorer query below and — going forward — by a live-tail
/// "everything after global position N matching these filters" query
/// (administration-mtf1f): both consume this exact filter shape and only differ
/// in pagination direction.
type EventFilter = {
    /// Full-text search over the event payload (events.data), via a server-side
    /// FTS5 index. Free text — the server treats the whole string as a literal
    /// phrase, not an FTS5 query expression.
    Search: string option
    /// Substring match on stream_id.
    StreamFilter: string option
    /// Substring match on event_type.
    EventTypeFilter: string option
    /// One of the bounded-context names returned by IAdminApi.getBoundedContexts
    /// (e.g. "Movies"); resolved server-side to a stream_id prefix.
    BoundedContext: string option
    /// ISO-8601 timestamp, inclusive lower bound.
    TimestampFrom: string option
    /// ISO-8601 timestamp, inclusive upper bound.
    TimestampTo: string option
}

module EventFilter =
    let empty : EventFilter = {
        Search = None
        StreamFilter = None
        EventTypeFilter = None
        BoundedContext = None
        TimestampFrom = None
        TimestampTo = None
    }

/// Keyset-paginated event query, newest-first. `Before = None` asks for the
/// first (newest) page; `Before = Some p` asks for events strictly older than
/// global position `p` (i.e. the page immediately after whatever page ended at
/// `p`). There is no server-side "after" direction — callers page backward by
/// remembering the cursor that produced each page they've already seen.
type EventPageQuery = {
    Filter: EventFilter
    Before: int64 option
    PageSize: int
}

type EventPage = {
    Events: EventDto list
    /// True if there is at least one more (older) event beyond this page.
    HasMore: bool
    /// Total number of events matching Filter, ignoring pagination — for a
    /// "X of Y matches" indicator.
    TotalMatches: int
}

// Stream drill-in (administration-v4y9g): full history of one aggregate's
// stream plus what the projection currently says about it — see ADR-0002
// (event sourcing + CQRS) for why this juxtaposition matters. Deliberately
// separate from EventHistoryEntry/getStreamEvents (IMediathecaApi), which
// backs the per-media detail page's history modal and is out of scope here.

/// One reference to another stream, extracted from a known payload field
/// (friendSlug, movieSlug, seriesSlug, gameSlug) while formatting a stream's
/// timeline. The target may not correspond to an existing stream (e.g. the
/// referenced friend was later removed) — navigating there is not an error,
/// it just shows an empty timeline, so a dangling cross-link is safe to render.
type StreamCrossLink = {
    /// Human-readable kind of the reference, e.g. "Friend", "Movie".
    Kind: string
    TargetStreamId: string
}

/// One event in a stream drill-in timeline. Carries both the formatted view
/// (via EventFormatting.formatEvent, when a formatter recognizes this event
/// type) and the raw data/metadata/positions for the per-event raw-JSON
/// toggle. FormattedLabel = None marks an event type no formatter knows yet —
/// rendered as raw JSON with an "unformatted" marker (feeds the future drift
/// report, administration-btvqa) rather than disappearing.
type StreamTimelineEntry = {
    GlobalPosition: int64
    StreamPosition: int64
    EventType: string
    Timestamp: string
    Data: string
    Metadata: string
    FormattedLabel: string option
    FormattedDetails: string list
    CrossLinks: StreamCrossLink list
}

/// Current read-model state for a stream, when its prefix maps to a known
/// per-BC projection (Movie/Series/Game/Friend/Catalog). Fields are loose
/// label/value pairs rather than a typed DTO because projection schemas vary
/// per BC and this panel exists to answer "what does the projection say right
/// now", not to duplicate each BC's typed detail contract.
type ProjectionStateRow = {
    Kind: string
    Fields: (string * string) list
    /// (route segment, slug) for a link to the media detail page, e.g. ("movies", slug).
    DetailLink: (string * string) option
}

/// Full stream drill-in payload: one stream's entire event history plus its
/// current projected state.
type StreamDetailDto = {
    StreamId: string
    Entries: StreamTimelineEntry list
    ProjectionRows: ProjectionStateRow list
}

// Compensating-event composer (administration-xjmda, ADR-0032): the stream
// drill-in's "Append corrective event" action. The idiomatic event-sourcing
// fix for bad data is a corrective event, not mutating history (ADR-0002).
// The operator picks a real event type seen under the stream's bounded
// context, clones its most recent payload as a starting point, edits it,
// then previews and confirms the exact canonicalized (round-tripped)
// payload that will be appended.

/// Pre-fill for the composer's payload editor: the cloned event's raw JSON.
/// `FromOtherStream` is true when no instance of the chosen event type
/// exists on this stream itself and the template was cloned from a sibling
/// stream sharing the same bounded-context prefix instead — the composer UI
/// surfaces this so the operator knows the payload may need edits (e.g. IDs)
/// specific to this stream.
type CompensatingEventTemplate = {
    Data: string
    FromOtherStream: bool
}

/// Result of `previewCompensatingEvent`: the canonicalized (post-round-trip,
/// not the operator's raw edit) payload that would actually be written, plus
/// the stream position the eventual `appendCompensatingEvent` call must
/// still match. `ExpectedPosition` is captured here (not re-read at commit
/// time) so a concurrent append landing on this stream between preview and
/// commit surfaces as a concurrency conflict rather than a silent overwrite.
type CompensatingEventPreview = {
    CanonicalEventType: string
    CanonicalData: string
    ExpectedPosition: int64
}

/// Live-tail query for the event explorer's Follow mode (administration-mtf1f):
/// "everything after global position `After`, matching `Filter`" — the
/// ascending direction ADR-0020 deliberately left off `EventPageQuery`.
/// Reuses `EventFilter` as-is (see its doc comment). `Limit` bounds a single
/// poll response so a burst of writes between polls can't return an unbounded
/// batch — see ADR-0023.
type EventTailQuery = {
    Filter: EventFilter
    After: int64
    Limit: int
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
    /// True while this episode's metadata was materialized from Jellyfin and TMDB
    /// has not yet enriched it (integration-m4k7p). Drives the "metadata pending"
    /// badge. A semantic flag, not the raw provider string — the client carries no
    /// provider knowledge.
    MetadataPending: bool
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
    /// Earliest future air date across this series (YYYY-MM-DD, local).
    /// Only populated for returning / in-production series that have an
    /// upcoming episode or season with a known air date.
    NextAirDate: string option
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
    /// Earliest known future air date (episode air_date) for this series.
    /// None if the series has no announced upcoming episode.
    NextEpisodeAirDate: string option
    /// Earliest known future air date for an entire season where no
    /// individual episode air date is known yet. Used as a fallback when
    /// NextEpisodeAirDate is None (e.g. TMDB has announced season 4 returns
    /// on a date but no episodes posted yet).
    NextSeasonAirDate: string option
}

type ReturningSoonItem = {
    Slug: string
    Name: string
    PosterRef: string option
    /// The earliest future air date (YYYY-MM-DD) associated with this series.
    NextAirDate: string
    /// True if the date came from a season air_date (less precise than an
    /// episode air_date).
    IsSeasonLevel: bool
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
    | Retired
    | Abandoned
    | Dismissed

type RawgSearchResult = {
    RawgId: int
    Name: string
    Year: int option
    BackgroundImage: string option
    Rating: float option
    Genres: string list
}

/// ADR-0053: three-state VR support. `NoVr` and `VrSupported` are both
/// distinct from "unknown" — the cache-derived `PlayFacets.Vr` field is
/// always total (never optional); only `PlayFacetsOverride.Vr` needs the
/// `option` wrapper, and even there `Some NoVr` is a real, distinct
/// statement ("Steam says VR-supported, I say no").
type VrSupport =
    | NoVr
    | VrSupported
    | VrOnly

/// ADR-0053: the cache-derived default — always total, never carried by an
/// event, written by `game_metadata_cache`'s facet columns (games-a7dqx) and
/// derived from Steam's numeric category ids. Wired into `GameListItem`/
/// `GameDetail` by games-v4nqe, superseding the old `PlayModes: string list`
/// free-text field.
type PlayFacets = {
    Solo: bool
    CoopCouch: bool
    CoopOnline: bool
    VersusCouch: bool
    VersusOnline: bool
    RemotePlayTogether: bool
    Vr: VrSupport
}

/// ADR-0053: the aggregate-held, event-sourced correction — one event
/// (`Game_play_facets_overridden`) carries the whole record. `None` on any
/// field means "not overridden, defer to the cache"; `Some v` — including
/// `Some false`/`Some NoVr` — overrules whatever the cache says.
type PlayFacetsOverride = {
    Solo: bool option
    CoopCouch: bool option
    CoopOnline: bool option
    VersusCouch: bool option
    VersusOnline: bool option
    RemotePlayTogether: bool option
    Vr: VrSupport option
}

/// games-j6wkr: the ADR-0053 correctness-trap guard. Each `withX` changes
/// exactly one field of an existing `PlayFacetsOverride`, leaving the other
/// six byte-identical — the GameDetail Auto/On/Off segmented controls call
/// these exclusively, applied to `GameDetail.PlayFacetsOverride`, and must
/// never derive the POST payload from the merged `PlayFacets` (that would
/// silently freeze every currently-cache-derived field as a permanent
/// manual override). Pure record `with`-update, so the guarantee is
/// structural, not just tested — `PlayFacetsOverrideTests.fs` covers it
/// anyway since this is the machine-checkable half of the trap.
module PlayFacetsOverride =
    let withSolo (v: bool option) (o: PlayFacetsOverride) = { o with Solo = v }
    let withCoopCouch (v: bool option) (o: PlayFacetsOverride) = { o with CoopCouch = v }
    let withCoopOnline (v: bool option) (o: PlayFacetsOverride) = { o with CoopOnline = v }
    let withVersusCouch (v: bool option) (o: PlayFacetsOverride) = { o with VersusCouch = v }
    let withVersusOnline (v: bool option) (o: PlayFacetsOverride) = { o with VersusOnline = v }
    let withRemotePlayTogether (v: bool option) (o: PlayFacetsOverride) = { o with RemotePlayTogether = v }
    let withVr (v: VrSupport option) (o: PlayFacetsOverride) = { o with Vr = v }

/// games-b8xnw: Steam's own Deck-compatibility verdict — cache-tier only
/// (`game_metadata_cache.deck_compat`, ADR-0045), no event, no override
/// (ADR-0043: a third party's re-fetchable description of the work, exactly
/// like `PlayFacets`, not an observation of Marco's own engagement). `Unknown`
/// covers both "Steam has never tested this title" and "never fetched yet" —
/// a caller cannot distinguish those two cases from this DU alone, which is
/// the correct honest-degradation stance (ADR-0048): neither case has
/// anything more specific to say.
type DeckCompatibility =
    | Verified
    | Playable
    | Unsupported
    | Unknown

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
    /// games-v4nqe (ADR-0053): the merged, display-ready facets — cache
    /// default with any manual override applied. See `PlayFacets`'s own doc
    /// comment; `GameDetail` additionally carries the raw
    /// `PlayFacetsOverride` for editing.
    PlayFacets: PlayFacets
    /// games-b8xnw: Steam's Deck-compatibility verdict, cache-derived only.
    DeckCompat: DeckCompatibility
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
    /// Playtime accumulated before session tracking began (games-p6vkz) —
    /// dateless, never contributes to the diary, only to the total above.
    /// GameDetail shows the breakdown ("512h before tracking + 12h tracked")
    /// when this is > 0, a single number otherwise.
    PriorPlayTimeMinutes: int
    /// games-v4nqe (ADR-0053): the merged, display-ready facets — superseded
    /// `PlayModes: string list`. `PlayFacetsOverride` (below) is the raw
    /// aggregate-held correction the client must send back on the next
    /// `overrideGamePlayFacets` call — the client must NOT round-trip the
    /// merged `PlayFacets` value as an override (that would freeze every
    /// currently-cache-derived field as a permanent manual override).
    PlayFacets: PlayFacets
    PlayFacetsOverride: PlayFacetsOverride
    /// games-b8xnw: Steam's Deck-compatibility verdict, cache-derived only —
    /// no override counterpart (see `DeckCompatibility`'s own doc comment).
    DeckCompat: DeckCompatibility
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
    SkipDuplicateCheck: bool
}

type AddGameOutcome =
    | Created of slug: string
    | Duplicate_found of existingSlug: string * existingName: string

// Dashboard Tabs (continued — types that reference MovieListItem / SeriesListItem / GameListItem)

type DashboardMoviesTab = {
    RecentlyAdded: MovieListItem list
    Stats: DashboardMovieStats
    RecentlyWatched: DashboardRecentlyWatched list
    MoviesToWatch: DashboardMovieToWatch list
    JellyfinServerUrl: string option
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
    JellyfinServerUrl: string option
    /// Up to 5 returning series sorted ascending by next air date.
    ReturningSoon: ReturningSoonItem list
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

type InFocusEstimate = {
    TotalRemainingMinutes: int
    GameCount: int
    GamesWithoutHltb: int
}

/// Per-game monthly play time for color-coded stacked bars
type GameMonthlyPlayTime = {
    Month: string
    GameSlug: string
    GameName: string
    MinutesPlayed: int
}

type DashboardGamesTab = {
    RecentlyAdded: GameListItem list
    RecentlyPlayed: DashboardGameRecentlyPlayed list
    Stats: DashboardGameStats
    HltbComparisons: DashboardHltbComparison list
    InFocusEstimate: InFocusEstimate
    MonthlyPlayTimePerGame: GameMonthlyPlayTime list
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

/// Candidate for attaching a Steam App ID to a game that was added without one.
/// Returned by `searchSteamForGame` and rendered by the client's candidate picker.
type SteamSearchResult = {
    AppId: int
    Name: string
    ReleaseYear: int option
    HeaderImageUrl: string option
    Score: float
}

// Playtime Tracking

type PlaySessionSource =
    | SteamSync
    | Manual

type PlaySessionDto = {
    GameSlug: string
    Date: string
    MinutesPlayed: int
    Source: PlaySessionSource
}

/// Editing a session (games-p6vkz): natural-key identity is `(GameSlug, Date)`
/// — no synthetic id (see the ADR's drift-detector argument) — so an edit
/// must name the session it's changing (`GameSlug`/`Date`) separately from
/// its new values (`NewDate`/`NewMinutes`).
type PlaySessionEdit = {
    GameSlug: string
    Date: string
    NewDate: string
    NewMinutes: int
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
    GamesCreated: int
    GamesPromotedToFocus: int
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

// Jellyfin Auto-Sync

type JellyfinSyncTriggerResult =
    | SyncStarted
    | SyncAlreadyInProgress
    | SyncCooldownActive of lastSyncTime: string
    | SyncNotConfigured

type JellyfinSyncStatus =
    | SyncIdle of lastSyncTime: string option
    | SyncInProgress
    | SyncCompleted of result: JellyfinImportResult * lastSyncTime: string
    | SyncFailed of error: string * lastSyncTime: string option

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
    // Library search is intentionally client-side: the Ctrl+K modal filters a
    // snapshot of the movie/series/game lists it already holds for excluding
    // owned items from TMDB/RAWG results (Client/Components/SearchModal.fs,
    // `filterLibrary`), and that filter does fuzzy matching, relevance ranking,
    // and year extraction. A `searchLibrary` endpoint existed here but was
    // never called — task 021-fuzzy-search flagged it as unused and upgraded
    // only the client — so it was removed rather than left to rot as a worse
    // duplicate. Reinstating server-side search means FTS5 with proper
    // ranking, not the `LIKE` + `ORDER BY name` it used to be.
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
    saveFriendCropSettings: string -> CropSettings -> Async<Result<unit, string>>
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
    /// Manually trigger a TMDB refresh for a single series.
    refreshSeriesFromTmdb: string -> Async<Result<unit, string>>
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
    addGame: AddGameRequest -> Async<Result<AddGameOutcome, string>>
    removeGame: string -> Async<Result<unit, string>>
    getGames: unit -> Async<GameListItem list>
    getGameDetail: string -> Async<GameDetail option>
    setGameStatus: string -> GameStatus -> Async<Result<unit, string>>
    setGamePersonalRating: string -> int option -> Async<Result<unit, string>>
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
    /// ADR-0053 — dispatches `Override_play_facets`. Supersedes the deleted
    /// `addGamePlayMode`/`removeGamePlayMode`/`getAllPlayModes` (games-v4nqe).
    overrideGamePlayFacets: string -> PlayFacetsOverride -> Async<Result<unit, string>>
    getGameContentBlocks: string -> Async<ContentBlockDto list>
    addGameContentBlock: string -> AddContentBlockRequest -> Async<Result<string, string>>
    updateGameContentBlock: string -> string -> UpdateContentBlockRequest -> Async<Result<unit, string>>
    removeGameContentBlock: string -> string -> Async<Result<unit, string>>
    // Game Journal (Notion-style block document)
    getGameJournal: string -> Async<JournalBlockDto list>
    saveGameJournal: string -> JournalBlockDto list -> Async<Result<unit, string>>
    getCatalogsForGame: string -> Async<CatalogRef list>
    getGameImageCandidates: string -> Async<GameImageCandidate list>
    selectGameImage: string -> string -> string -> Async<Result<unit, string>>
    getGameTrailers: string -> Async<GameTrailerInfo list>
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
    // Steam Attach (Connect with Steam)
    searchSteamForGame: string -> Async<SteamSearchResult list>
    attachSteamToGame: string * int -> Async<Result<unit, string>>
    // RAWG Re-link (correct a wrong RAWG association)
    searchRawgForGame: string -> Async<RawgSearchResult list>
    attachRawgToGame: string * int -> Async<Result<unit, string>>
    // Jellyfin Integration
    getJellyfinServerUrl: unit -> Async<string>
    setJellyfinServerUrl: string -> Async<Result<unit, string>>
    getJellyfinUsername: unit -> Async<string>
    setJellyfinCredentials: string * string -> Async<Result<unit, string>>
    testJellyfinConnection: string * string * string -> Async<Result<string, string>>
    scanJellyfinLibrary: unit -> Async<Result<JellyfinScanResult, string>>
    importJellyfinWatchHistory: unit -> Async<Result<JellyfinImportResult, string>>
    // Jellyfin Auto-Sync
    triggerJellyfinSync: unit -> Async<JellyfinSyncTriggerResult>
    getJellyfinSyncStatus: unit -> Async<JellyfinSyncStatus>
    // Steam Family Last Sync
    getSteamFamilyLastSync: unit -> Async<string option>
    // Import
    importFromCinemarco: ImportFromCinemarcoRequest -> Async<Result<ImportResult, string>>
    // View Settings
    getViewSettings: string -> Async<ViewSettings option>
    saveViewSettings: string -> ViewSettings -> Async<unit>
    getCollapsedSections: string -> Async<string list>
    saveCollapsedSections: string -> string list -> Async<unit>
    // Playtime Tracking
    getGamePlaySessions: string -> Async<PlaySessionDto list>
    addManualPlaySession: string * string * int -> Async<Result<PlaySessionDto, string>>
    updatePlaySession: PlaySessionEdit -> Async<Result<PlaySessionDto, string>>
    deletePlaySession: string * string -> Async<Result<unit, string>>
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

// Administration console — a separate Remoting contract (ADR-0004 allows multiple
// APIs) so admin plumbing (event store browser, and future projection/health/jobs/
// surgery tooling) doesn't bloat IMediathecaApi. Routed under /api/admin/{Method}.
module AdminRoute =
    let builder (_typeName: string) (methodName: string) =
        sprintf "/api/admin/%s" methodName

// Health tab (administration-hw74a) — one aggregate DTO for the whole tab so
// it loads in a single round trip. See ADR-0021 for the query-cost reasoning
// behind what's aggregated server-side vs. what's a raw top-N list.
type BoundedContextEventCount = { BoundedContext: string; Count: int }
type DailyEventCount = { Date: string; Count: int }
type StreamEventCount = { StreamId: string; Count: int }
type EventTypeCount = { EventType: string; Count: int }

type StorageStats = {
    DbSizeBytes: int64
    WalSizeBytes: int64
    ImagesSizeBytes: int64
    ImagesFileCount: int
}

/// One row of the Health tab's unknown-event report (administration-gxd6e):
/// a distinct event type flagged by either the unhandled or unformattable
/// check, with its total count and one representative sample event's raw
/// JSON payload (display-only, not persisted).
type UnknownEventTypeRow = {
    EventType: string
    Count: int
    SampleData: string
}

type HealthStats = {
    TotalEventCount: int
    /// Per bounded context, by stream-id prefix (Administration.boundedContextPrefixes).
    /// Includes an "Other" entry when streams don't match a known prefix, so
    /// this list's counts always sum to TotalEventCount.
    BoundedContextCounts: BoundedContextEventCount list
    /// One entry per day for the last ~90 days, oldest first, zero-filled for
    /// days with no events (so the sparkline gets an even series).
    DailyCounts: DailyEventCount list
    /// Largest streams by event count, descending, top 10.
    TopStreams: StreamEventCount list
    DistinctEventTypeCount: int
    /// Most frequent event types, descending, top 10.
    TopEventTypes: EventTypeCount list
    Storage: StorageStats
    /// Event types whose owning bounded context (resolved via stream prefix
    /// on a sample event) doesn't list them in its `handledEventTypes`
    /// registry, or whose stream prefix matches no known bounded context at
    /// all (administration-gxd6e).
    UnhandledEventTypes: UnknownEventTypeRow list
    /// Event types whose sample stored event, run through
    /// `EventFormatting.formatEvent`, returns None (administration-gxd6e).
    /// Independent of `UnhandledEventTypes` — a type can be handled by its
    /// BC's deserializer yet still have no formatter case.
    UnformattableEventTypes: UnknownEventTypeRow list
}

// Projection dashboard (administration-qjcp4): checkpoint/lag overview per
// registered Projection.ProjectionHandler, plus a rebuild command whose
// progress streams over SSE (not through Remoting — see
// Administration.projectionRebuildStreamHandler, wired as a raw Giraffe
// route). See ADR-0002 for why projections are disposable/rebuildable in the
// first place.

/// One table a projection owns, with its current row count. A projection may
/// own several tables of different shapes (e.g. SeriesProjection's list/
/// detail/season/episode tables) — reported per-table rather than summed so
/// an operator can tell them apart.
type ProjectionTableCount = { TableName: string; RowCount: int }

type ProjectionStatRow = {
    Name: string
    /// `projection_checkpoints.last_position` — how far this projection has
    /// caught up to.
    CheckpointPosition: int64
    /// Store head (MAX(global_position)) minus CheckpointPosition. 0 when
    /// fully caught up.
    Lag: int64
    /// `projection_checkpoints.updated_at`, None if this projection has
    /// never checkpointed.
    UpdatedAt: string option
    TableCounts: ProjectionTableCount list
    /// True while a rebuild of this projection is in flight on the server —
    /// drives the "Rebuild" button's disabled state after a page reload.
    IsRebuilding: bool
}

/// The `Cache`/`Imperative` half of `Administration.tableRegistry`
/// (administration-t9bzx): row counts for every durable table that is
/// explicitly NOT checkpoint-tracked, NOT rebuildable, and NOT drift-checked
/// — the counterpart to `ProjectionStatRow` for tables `checkProjectionDrift`
/// never touches. `Classification` is `"Cache"` (re-derivable from an
/// external system's own state) or `"Imperative"` (written directly, with no
/// replay or re-fetch path back if lost); `Detail` names the module that
/// refreshes/writes it (`Administration.TableClass`'s `refreshedBy` /
/// `writtenBy` payload).
type UnrebuildableTableStat = {
    TableName: string
    Classification: string
    Detail: string
    RowCount: int
}

// Image cache admin (administration-xx3mw): stats/orphan-detection/purge for
// the images/ cache. Live refs come from the fifteen typed ref-bearing
// projection columns (Administration.imageRefColumns), never event replay —
// see ADR-0025.

/// One images/ subfolder's footprint (posters/backdrops/stills/cast/content/
/// friends, plus "(root)" for stray loose files). Rows are derived by
/// grouping the actual on-disk walk, so they always sum exactly to the
/// aggregate total/count.
type ImageSubfolderStat = { Subfolder: string; FileCount: int; SizeBytes: int64 }

/// Always available (pure disk footprint, no not-dirty guard needed).
type ImageCacheStats = { TotalBytes: int64; TotalFileCount: int; Subfolders: ImageSubfolderStat list }

type OrphanImage = { RelativePath: string; Subfolder: string; SizeBytes: int64 }

/// `OrphanScanBlocked` fires while any of the six checkpoint-tracked
/// projections is dirty or mid-rebuild (ADR-0025's not-dirty guard) — an
/// operator-facing reason string, not an exception, so the client renders it
/// as a DU case rather than an error state.
type OrphanScan =
    | OrphanScanBlocked of reason: string
    | OrphanScanReady of orphans: OrphanImage list * totalBytes: int64

type PurgeSelection =
    | PurgeAll
    | PurgeSpecific of relativePaths: string list

/// `skipped` lists requested paths the server declined to delete because
/// they were no longer genuinely orphan at commit time (re-referenced or
/// already gone) — the TOCTOU-safe re-derivation ADR-0025 requires.
type PurgeResult =
    | PurgeBlocked of reason: string
    | PurgeDone of deletedCount: int * bytesFreed: int64 * skipped: string list

// Job runs console (administration-yamm5): durable history for the two
// ScheduledJobs.JobSpec entries, plus a fire-and-forget "run now" (ADR-0026).
// Terminal statuses map 1:1 to job_runs.status; case names are deliberately
// NOT `Ok`/`Error` (those are FSharp.Core's Result cases, and Shared.fs is
// `open`ed by nearly every server module — reusing them here would shadow
// the Result cases everywhere this module is opened).
type JobRunStatus =
    | RunStatusRunning
    | RunStatusOk
    | RunStatusError
    | RunStatusSkipped
    | RunStatusInterrupted

/// One row of `job_runs`. `Summary` is None only while `Status` is
/// `RunStatusRunning`. `StartedAt`/`FinishedAt` are ISO-8601 UTC (stored as
/// such); the Jobs tab must label them explicitly against `NextFireAt`
/// (local time) rather than rendering both in one zone.
type JobRunDto = {
    Id: int64
    JobName: string
    Trigger: string
    Status: JobRunStatus
    Summary: string option
    StartedAt: string
    FinishedAt: string option
}

/// Per-job status for the Jobs tab: next scheduled fire (local time, from
/// `ScheduledJobs.nextRun`), the most recent run (if any), and recent-run
/// history (no pruning — ADR-0026 keeps all rows).
type JobStatusDto = {
    JobName: string
    NextFireAt: string
    LastRun: JobRunDto option
    RecentRuns: JobRunDto list
}

/// Result of an operator-triggered "Run now". Fire-and-forget: `RunJobStarted`
/// carries the new `running` row's id so the tab can poll for it to resolve;
/// `RunJobRejected` means the job was already in flight under either trigger.
type RunJobResult =
    | RunJobStarted of runId: int64
    | RunJobRejected

// Event surgery (administration-wwc36, ADR-0034): the raw-log escape hatch
// for cases the compensating-event composer (ADR-0032) can't reach — a
// genuinely wrong-payload event, or a stranded event-type name left by a
// code-side DU rename. Every mutation runs behind the same three-guardrail
// protocol: VACUUM INTO backup first, preview + explicit confirm, then a
// projections-dirty signal (checkpoint rewind, reusing ADR-0025's
// isAnyProjectionDirty verbatim — no new flag table).

/// One event row as event surgery reads/targets it. Unlike `EventDto` (the
/// event explorer's read-only row), this carries `Metadata` too, since edit
/// lets the operator change both `Data` and `Metadata`.
type SurgeryEventRow = {
    GlobalPosition: int64
    StreamId: string
    StreamPosition: int64
    EventType: string
    Data: string
    Metadata: string
    Timestamp: string
}

/// Delete preview: the one targeted row, plus the stream's CURRENT (i.e.
/// pre-delete) `stream_position` — carried so the client can render the
/// stream-position-gap consequence copy ("deleting this leaves a gap; the
/// stream is currently at position N") without a second round-trip.
type SurgeryDeletePreview = {
    Event: SurgeryEventRow
    StreamCurrentPosition: int64
}

/// Rename preview: the exact count of rows at the old `event_type`, plus a
/// bounded sample — a rename can touch far more rows than any preview should
/// try to render in full.
type SurgeryRenamePreview = {
    Count: int
    Sample: SurgeryEventRow list
}

/// Result of a surgery commit op (edit/delete/rename). Backup failure is a
/// typed case, not an exception — mirrors the `OrphanScan`/`PurgeResult`
/// typed-outcome idiom. `BackupFailed` means NO row was touched (the
/// `VACUUM INTO` or its post-backup verify failed — abort before any
/// mutation). `Applied` carries the backup file's path (the operator's
/// restore point for this exact operation) and the number of rows the
/// mutation affected.
type SurgeryResult =
    | BackupFailed of reason: string
    | Applied of backupPath: string * affectedRows: int

/// Cumulative footprint of the keep-all `backups/` directory. No backup is
/// ever auto-pruned by this feature (builder decision, locked at refinement)
/// — this is purely a directory-walk stat for the Surgery tab's backup panel.
type BackupStats = { Count: int; TotalBytes: int64 }

// Wipe-first event log import (administration-n8kqw, ADR-0038): overwriting a
// non-empty store, reusing ADR-0034's three-guardrail protocol (VACUUM INTO
// backup first, preview + explicit confirm, projections-dirty signal) with
// one inversion — the transaction, not the backup, is the primary restore
// path. Route: raw Giraffe SSE `POST /api/stream/wipe-import-events`, a
// sibling of `/api/stream/import-events`, not a flag on it (keeps the safe
// route's "refuses any non-empty store" invariant literally true).

/// The discard-side stats for the wipe-import confirm dialog — what's about
/// to be thrown away, from `Administration.getWipeImportPreview` /
/// `EventStore.getEventStoreSummary`. The incoming-side line count is
/// computed client-side from the `File` object before upload; there is no
/// staging area or second upload phase.
type WipeImportPreview = {
    EventCount: int
    DistinctStreamCount: int
    OldestTimestamp: string option
    NewestTimestamp: string option
}

type IAdminApi = {
    // Event Store Browser
    getEventPage: EventPageQuery -> Async<EventPage>
    getEventStreams: unit -> Async<string list>
    getEventTypes: unit -> Async<string list>
    getBoundedContexts: unit -> Async<string list>
    // Stream drill-in (administration-v4y9g)
    getStreamDetail: string -> Async<StreamDetailDto>
    /// Live-tail poll for Follow mode (administration-mtf1f) — see EventTailQuery.
    getEventsAfter: EventTailQuery -> Async<EventDto list>
    // Compensating-event composer (administration-xjmda, ADR-0032)
    /// Every event type seen anywhere under `streamId`'s bounded-context
    /// prefix — the "clone a real event" type picker. Empty if the stream's
    /// prefix matches no known bounded context.
    getCompensatingEventTypes: string -> Async<string list>
    /// Pre-fill payload for a chosen event type on `streamId` — see
    /// `CompensatingEventTemplate`. None if no instance of that type exists
    /// anywhere under the stream's bounded-context prefix.
    getCompensatingEventTemplate: string -> string -> Async<CompensatingEventTemplate option>
    /// Round-trip-validates and canonicalizes an edited payload without
    /// appending anything — feeds the confirmation dialog's exact preview
    /// and captures `ExpectedPosition` for the eventual commit. `Error` when
    /// the payload doesn't deserialize as a valid instance of the event type.
    previewCompensatingEvent: string -> string -> string -> Async<Result<CompensatingEventPreview, string>>
    /// Commits a compensating event: re-validates the edited payload
    /// (independently of any earlier preview), appends the re-serialized
    /// canonical form via `EventStore.appendToStream` with
    /// `expectedPosition` (optimistic concurrency — `Error` on conflict),
    /// then runs every registered projection's catch-up. Arguments are
    /// (streamId, eventType, editedPayload, expectedPosition).
    appendCompensatingEvent: string -> string -> string -> int64 -> Async<Result<unit, string>>
    // Health
    getHealthStats: unit -> Async<HealthStats>
    // Projections (administration-qjcp4)
    getProjectionStats: unit -> Async<ProjectionStatRow list>
    /// The Cache/Imperative tables `getProjectionStats` doesn't cover
    /// (administration-t9bzx) — a separate, explicitly un-rebuildable
    /// section. Client display is optional/deferred; this is server-side
    /// visibility for now.
    getUnrebuildableTableStats: unit -> Async<UnrebuildableTableStat list>
    // Images (administration-xx3mw)
    getImageCacheStats: unit -> Async<ImageCacheStats>
    listOrphanedImages: unit -> Async<OrphanScan>
    purgeOrphanedImages: PurgeSelection -> Async<PurgeResult>
    // Jobs (administration-yamm5)
    getJobStatuses: unit -> Async<JobStatusDto list>
    runJobNow: string -> Async<RunJobResult>
    // Event surgery (administration-wwc36, ADR-0034)
    /// The one targeted row by `global_position`, for the edit dialog's
    /// pre-fill. `None` if no such position exists.
    previewEventEdit: int64 -> Async<SurgeryEventRow option>
    /// The one targeted row by `global_position` plus the stream's current
    /// position, for the delete confirmation's gap-consequence copy. `None`
    /// if no such position exists.
    previewEventDelete: int64 -> Async<SurgeryDeletePreview option>
    /// Exact count of rows at `oldType` plus a bounded sample, for the
    /// rename confirmation dialog.
    previewEventTypeRename: string -> Async<SurgeryRenamePreview>
    /// Commits an edit: (globalPosition, newData, newMetadata). Backs up via
    /// `VACUUM INTO` first (abort with no row touched on backup failure),
    /// then updates the one row, re-syncs `events_fts`, and rewinds every
    /// checkpoint-tracked projection to dirty.
    editEvent: int64 -> string -> string -> Async<SurgeryResult>
    /// Commits a delete: globalPosition. Leaves stream_position/global_position
    /// gaps by design (no renumbering) — verified safe against the keyset/
    /// live-tail cursors and appendToStream's fresh MAX re-read.
    deleteEvent: int64 -> Async<SurgeryResult>
    /// Commits a store-wide event-type rename: (oldType, newType).
    renameEventType: string -> string -> Async<SurgeryResult>
    /// Cumulative count + total bytes of every backup ever taken by this
    /// feature — keep-all retention, no pruning.
    getBackupStats: unit -> Async<BackupStats>
    // Wipe-first event log import (administration-n8kqw, ADR-0038)
    /// Discard-side stats for the wipe-import confirm dialog — a fast
    /// aggregate read (plain Async, not SSE): exact event count, distinct
    /// stream count, oldest/newest timestamp of what's currently in the
    /// store, i.e. what would be discarded by a Wipe & Import.
    getWipeImportPreview: unit -> Async<WipeImportPreview>
}
