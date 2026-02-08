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

// DTOs

type TmdbSearchResult = {
    TmdbId: int
    Title: string
    Year: int option
    Overview: string
    PosterPath: string option
}

type CastMemberDto = {
    Name: string
    Role: string
    ImageRef: string option
    TmdbId: int
}

type FriendRef = {
    Slug: string
    Name: string
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

// Watch Sessions

type WatchSessionDto = {
    SessionId: string
    Date: string
    Duration: int option
    Friends: FriendRef list
}

type RecordWatchSessionRequest = {
    Date: string
    Duration: int option
    FriendSlugs: string list
}

// Content Blocks

type ContentBlockType =
    | TextBlock
    | ImageBlock
    | LinkBlock

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
    Cast: CastMemberDto list
    RecommendedBy: FriendRef list
    WantToWatchWith: FriendRef list
    WatchSessions: WatchSessionDto list
    ContentBlocks: ContentBlockDto list
}

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type IMediathecaApi = {
    healthCheck: unit -> Async<string>
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
    addFriend: string -> Async<Result<string, string>>
    updateFriend: string -> string -> string option -> Async<Result<unit, string>>
    removeFriend: string -> Async<Result<unit, string>>
    getFriend: string -> Async<FriendDetail option>
    getFriends: unit -> Async<FriendListItem list>
    // Watch Sessions
    recordWatchSession: string -> RecordWatchSessionRequest -> Async<Result<string, string>>
    updateWatchSessionDate: string -> string -> string -> Async<Result<unit, string>>
    addFriendToWatchSession: string -> string -> string -> Async<Result<unit, string>>
    removeFriendFromWatchSession: string -> string -> string -> Async<Result<unit, string>>
    getWatchSessions: string -> Async<WatchSessionDto list>
    // Content Blocks
    addContentBlock: string -> string option -> AddContentBlockRequest -> Async<Result<string, string>>
    updateContentBlock: string -> string -> UpdateContentBlockRequest -> Async<Result<unit, string>>
    removeContentBlock: string -> string -> Async<Result<unit, string>>
    reorderContentBlocks: string -> string option -> string list -> Async<Result<unit, string>>
    getContentBlocks: string -> string option -> Async<ContentBlockDto list>
    uploadContentImage: byte array -> string -> Async<Result<string, string>>
    // Settings
    getTmdbApiKey: unit -> Async<string>
    setTmdbApiKey: string -> Async<Result<unit, string>>
    testTmdbApiKey: string -> Async<Result<unit, string>>
}
