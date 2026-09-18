namespace Mediatheca.Server

open System.Data
open System.Net.Http
open Microsoft.Data.Sqlite
open Donald
open Giraffe
open Mediatheca.Shared

module Api =

    /// `Qbittorrent.QbittorrentError` -> a plain message, the sibling of how
    /// `Jellyfin.withReauthRetry`'s errors are already flattened to strings
    /// at this boundary (integration-r4vzm).
    let private qbErrorToString (e: Qbittorrent.QbittorrentError) : string =
        match e with
        | Qbittorrent.AuthFailed -> "qBittorrent authentication failed"
        | Qbittorrent.OtherFailure msg -> msg

    /// games-v4nqe: read-modify-write helper shared by every converted Steam
    /// emission site below. Reads the current `game_metadata_cache` identity
    /// card, overrides only the fields the caller actually passes `Some` for
    /// (`None` = leave unchanged), and writes the whole slice back through
    /// `MetadataCache.upsertGameIdentityCard` in one call — never
    /// constructing a partial record from scratch, which would silently
    /// blank the fields a given call site doesn't touch (description for the
    /// sites that only ever refresh short_description/website_url, for
    /// instance). Genres is NOT part of this slice — ADR-0055 (amending
    /// ADR-0043) keeps it event-carried on `game_list`/`game_detail`.
    ///
    /// games-fffvm: stamps `description_fetched_at` iff `description` is
    /// `Some` — the exact "sanitizer-output description genuinely written"
    /// rule this task's stamping section calls for. A short_description/
    /// website_url-only refresh (`description = None`, the re-enrich
    /// branches below) never stamps, so a legacy flattened description
    /// stays a backfill candidate until an actual description write lands.
    let private updateGameIdentityCache
        (conn: SqliteConnection)
        (slug: string)
        (description: string option)
        (shortDescription: string option)
        (websiteUrl: (string option) option)
        : unit =
        let current = MetadataCache.tryGetGameIdentityCard conn slug
        MetadataCache.upsertGameIdentityCard conn slug {
            Description = description |> Option.defaultValue current.Description
            ShortDescription = shortDescription |> Option.defaultValue current.ShortDescription
            WebsiteUrl = websiteUrl |> Option.defaultValue current.WebsiteUrl
        }
        if description.IsSome then
            MetadataCache.stampDescriptionFetched conn slug

    /// games-v4nqe: derives ADR-0053 facets from a Steam fetch's category ids
    /// and writes them (plus the raw ids) to the cache — the same
    /// `deriveFacets` + `upsertGameFacets` pairing every converted emission
    /// site uses, factored out once. No "don't clobber overrides" guard
    /// needed (ADR-0053 — the override lives on a different column set
    /// entirely, `game_detail.facet_override_*`, never touched here).
    let private updateGameFacetsFromCategoryIds (conn: SqliteConnection) (slug: string) (categoryIds: int list) : unit =
        let facets = FacetDerivation.deriveFacets categoryIds
        MetadataCache.upsertGameFacets conn slug facets categoryIds

    /// games-ev65k (ADR-0043/ADR-0045): writes Steam's release-date facts to
    /// the cache tier — called alongside `updateGameFacetsFromCategoryIds`
    /// at every creation-path/Steam-fetch call site below, so a fresh Steam
    /// fetch keeps both slices current together. Never touches
    /// `game_detail` (there is no override tier for a release date).
    let private updateGameReleaseDate (conn: SqliteConnection) (slug: string) (details: Steam.SteamStoreDetails) : unit =
        let parsed = ReleaseDateParsing.tryParseSortable details.ReleaseDateRaw
        MetadataCache.upsertGameReleaseDate conn slug details.ReleaseDateRaw parsed details.ComingSoon

    // administration-mz6kp (ADR-0033): `conn` is a per-request connection
    // opened by the caller via the shared factory (`use conn = factory()` at
    // the record member/handler that reaches this function) — no other
    // in-flight request shares this connection object, so the process-wide
    // `dbLock` ADR-0030 threaded through here is retired along with the
    // shared connection it guarded.
    let private executeCommandCore
        (conn: SqliteConnection)
        (streamId: string)
        (fromStoredEvent: EventStore.StoredEvent -> 'Event option)
        (reconstitute: 'Event list -> 'State)
        (decide: 'State -> 'Command -> Result<'Event list, string>)
        (toEventData: 'Event -> EventStore.EventData)
        (command: 'Command)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<unit, string> =
        // 1. Read stream, deserialize, reconstitute
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose fromStoredEvent
        let state = reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId

        // 2. Decide
        match decide state command with
        | Error e -> Error e
        | Ok newEvents ->
            if List.isEmpty newEvents then
                Ok ()
            else
                // 3. Serialize and append
                let eventDataList = newEvents |> List.map toEventData
                match EventStore.appendToStream conn streamId currentPosition eventDataList with
                | EventStore.ConcurrencyConflict _ ->
                    Error "Concurrency conflict, please retry"
                | EventStore.Success _ ->
                    // 4. Catch-up projections
                    for handler in projectionHandlers do
                        Projection.runProjection conn handler
                    Ok ()

    /// curation-h4k2p: clears a removed media item's Notes document and
    /// deletes its uploaded `content/` images — the ADR-0080 successor to
    /// the deleted `GameJournal.deleteForGame`, now shared across all four
    /// media types. Called from each `removeX` handler right after its
    /// aggregate's `Ok ()`, alongside the existing catalog-entry and
    /// poster/backdrop cascade.
    ///
    /// Order matters:
    /// 1. Read the projection FIRST — its rows for this owner vanish the
    ///    instant `Notes_saved []` is handled (ADR-0080 §6).
    /// 2. Clear the document through the event log via an ordinary
    ///    `Save_notes []` command — never an imperative `notes_blocks`
    ///    DELETE, which a projection rebuild would resurrect (ADR-0080 §6)
    ///    and which would destroy the user's own writing instead of leaving
    ///    it recoverable in the stream (ADR-0043). `decide` already yields
    ///    `Notes_saved []` when there was content, and `Ok []` (append
    ///    nothing, create no stream) when there was none.
    /// 3. Delete the files LAST, after the append, so a concurrency
    ///    conflict on step 2 does not orphan files that live state still
    ///    references. Files are cache tier, deleted imperatively at command
    ///    time, never on projection replay (ADR-0045).
    ///
    /// Scoped to `content/`-prefixed `ImageRef`s only — exactly as the old
    /// `GameJournal.deleteForGame` scoped it — never posters/backdrops/
    /// covers/stills. Best-effort and non-transactional, matching the
    /// existing removal cascade style: a failure here does not fail the
    /// removal.
    let private clearNotesOnRemoval
        (conn: SqliteConnection)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (mediaType: MediaType)
        (slug: string)
        : unit =
        let contentRefs =
            NotesProjection.getForOwner conn mediaType slug
            |> List.choose (fun (b: JournalBlockDto) -> b.ImageRef)
            |> List.filter (fun (r: string) -> r.StartsWith("content/"))
        let sid = Notes.streamId mediaType slug
        executeCommandCore
            conn sid
            Notes.Serialization.fromStoredEvent
            Notes.reconstitute
            Notes.decide
            Notes.Serialization.toEventData
            (Notes.Save_notes [])
            projectionHandlers
        |> ignore
        for ref in contentRefs do
            try ImageStore.deleteImage imageBasePath ref with _ -> ()

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    let private tryFindSlugByTmdbId (conn: SqliteConnection) (table: string) (tmdbId: int) : string option =
        // Active entries only — Series_removed/Movie_removed deletes the row from the *_detail table.
        conn
        |> Db.newCommand (sprintf "SELECT slug FROM %s WHERE tmdb_id = @tmdb_id LIMIT 1" table)
        |> Db.setParams [ "tmdb_id", SqlType.Int32 tmdbId ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "slug")

    let private tryFindMovieSlugByTmdbId (conn: SqliteConnection) (tmdbId: int) : string option =
        tryFindSlugByTmdbId conn "movie_detail" tmdbId

    let private tryFindSeriesSlugByTmdbId (conn: SqliteConnection) (tmdbId: int) : string option =
        tryFindSlugByTmdbId conn "series_detail" tmdbId

    let private addMovieToLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (movieProjections: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            // administration-mz6kp (ADR-0033): full eta-expansion is still
            // required here — a bare partial application (`executeCommandCore`)
            // would collapse to the FIRST call site's concrete type
            // instantiation (F#'s value restriction on partially-applied
            // generic functions) and break every other event type this
            // function's sibling call sites use.
            let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
                executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
            try
                let tmdbConfig = getTmdbConfig()
                let! details = Tmdb.getMovieDetails httpClient tmdbConfig tmdbId
                let! credits = Tmdb.getMovieCredits httpClient tmdbConfig tmdbId

                let year =
                    details.ReleaseDate
                    |> Option.bind (fun d ->
                        if d.Length >= 4 then
                            match System.Int32.TryParse(d.[0..3]) with
                            | true, y -> Some y
                            | _ -> None
                        else None)
                    |> Option.defaultValue 0

                let baseSlug = Slug.movieSlug details.Title year
                let slug = generateUniqueSlug conn Movies.streamId baseSlug
                let sid = Movies.streamId slug

                let posterRef =
                    match details.PosterPath with
                    | Some p ->
                        let ref = sprintf "posters/%s.jpg" slug
                        try
                            Tmdb.downloadImage httpClient tmdbConfig p "w500" (System.IO.Path.Combine(imageBasePath, ref))
                            |> Async.RunSynchronously
                            Some ref
                        with _ -> None
                    | None -> None

                let backdropRef =
                    match details.BackdropPath with
                    | Some p ->
                        let ref = sprintf "backdrops/%s.jpg" slug
                        try
                            Tmdb.downloadImage httpClient tmdbConfig p "w1280" (System.IO.Path.Combine(imageBasePath, ref))
                            |> Async.RunSynchronously
                            Some ref
                        with _ -> None
                    | None -> None

                let movieData: Movies.MovieAddedData = {
                    Name = details.Title
                    Year = year
                    Runtime = details.Runtime
                    Overview = details.Overview
                    Genres = details.Genres |> List.map (fun g -> g.Name)
                    PosterRef = posterRef
                    BackdropRef = backdropRef
                    TmdbId = tmdbId
                    TmdbRating = details.VoteAverage
                }

                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_movie_to_library movieData)
                        movieProjections

                match result with
                | Error e -> return Error e
                | Ok () ->
                    let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                    for castMember in topBilled do
                        let castImageRef =
                            match castMember.ProfilePath with
                            | Some p ->
                                let ref = sprintf "cast/%d.jpg" castMember.Id
                                let destPath = System.IO.Path.Combine(imageBasePath, ref)
                                if not (ImageStore.imageExists imageBasePath ref) then
                                    try
                                        Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                        |> Async.RunSynchronously
                                    with _ -> ()
                                Some ref
                            | None -> None
                        let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                        CastStore.addMovieCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)

                    // Store directors from crew
                    let directors = credits.Crew |> List.filter (fun c -> c.Job = "Director")
                    for director in directors do
                        let dirImageRef =
                            match director.ProfilePath with
                            | Some p ->
                                let ref = sprintf "cast/%d.jpg" director.Id
                                let destPath = System.IO.Path.Combine(imageBasePath, ref)
                                if not (ImageStore.imageExists imageBasePath ref) then
                                    try
                                        Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                        |> Async.RunSynchronously
                                    with _ -> ()
                                Some ref
                            | None -> None
                        let cmId = CastStore.upsertCastMember conn director.Name director.Id dirImageRef
                        CastStore.addMovieCrew conn sid cmId director.Job director.Department

                    return Ok slug
            with ex ->
                return Error $"Failed to add movie: {ex.Message}"
        }

    /// Public entry point for adding a movie. Short-circuits when the TMDB id is already in the
    /// library — avoids the 20-second TMDB-fetch race that previously let parallel callers
    /// (Jellyfin auto-sync vs. manual add) create duplicate slugs for the same title.
    let private addMovieToLibrary
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (movieProjections: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            match tryFindMovieSlugByTmdbId conn tmdbId with
            | Some existing -> return Ok existing
            | None -> return! addMovieToLibraryImpl conn httpClient getTmdbConfig imageBasePath movieProjections tmdbId
        }

    let private addSeriesToLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
                executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
            try
                let tmdbConfig = getTmdbConfig()
                let! detailsResult = Tmdb.getTvSeriesDetails httpClient tmdbConfig tmdbId
                match detailsResult with
                | Error e -> return Error e
                | Ok details ->
                    let year =
                        details.FirstAirDate
                        |> Option.bind (fun d ->
                            if d.Length >= 4 then
                                match System.Int32.TryParse(d.[0..3]) with
                                | true, y -> Some y
                                | _ -> None
                            else None)
                        |> Option.defaultValue 0

                    let baseSlug = Slug.seriesSlug details.Name year
                    let slug = generateUniqueSlug conn Series.streamId baseSlug
                    let sid = Series.streamId slug

                    let! posterRef, backdropRef =
                        Tmdb.downloadSeriesImages httpClient tmdbConfig slug details.PosterPath details.BackdropPath imageBasePath

                    let! seasons =
                        details.Seasons
                        |> List.filter (fun s -> s.SeasonNumber > 0)
                        |> List.map (fun seasonSummary -> async {
                            let! seasonResult = Tmdb.getTvSeasonDetails httpClient tmdbConfig tmdbId seasonSummary.SeasonNumber
                            match seasonResult with
                            | Ok seasonDetails ->
                                let! episodes =
                                    seasonDetails.Episodes
                                    |> List.map (fun ep -> async {
                                        let stillRef =
                                            match ep.StillPath with
                                            | Some stillPath ->
                                                try
                                                    Tmdb.downloadEpisodeStill httpClient tmdbConfig slug seasonSummary.SeasonNumber ep.EpisodeNumber stillPath imageBasePath
                                                    |> Async.RunSynchronously
                                                with _ -> None
                                            | None -> None
                                        let epData: Series.EpisodeImportData = {
                                            EpisodeNumber = ep.EpisodeNumber
                                            Name = ep.Name
                                            Overview = ep.Overview
                                            Runtime = ep.Runtime
                                            AirDate = ep.AirDate
                                            StillRef = stillRef
                                            TmdbRating = if ep.VoteAverage > 0.0 then Some ep.VoteAverage else None
                                        }
                                        return epData
                                    })
                                    |> Async.Sequential
                                let seasonData: Series.SeasonImportData = {
                                    SeasonNumber = seasonSummary.SeasonNumber
                                    Name = seasonSummary.Name
                                    Overview = seasonSummary.Overview
                                    PosterRef = None
                                    AirDate = seasonSummary.AirDate
                                    Episodes = episodes |> Array.toList
                                }
                                return Some seasonData
                            | Error _ -> return None
                        })
                        |> Async.Sequential

                    let validSeasons = seasons |> Array.toList |> List.choose id

                    let episodeRuntime =
                        match details.EpisodeRunTime with
                        | first :: _ -> Some first
                        | [] -> None

                    let seriesData: Series.SeriesAddedData = {
                        Name = details.Name
                        Year = year
                        Overview = details.Overview
                        Genres = details.Genres |> List.map (fun g -> g.Name)
                        Status = Tmdb.mapSeriesStatus details.Status
                        PosterRef = posterRef
                        BackdropRef = backdropRef
                        TmdbId = tmdbId
                        TmdbRating = if details.VoteAverage > 0.0 then Some details.VoteAverage else None
                        EpisodeRuntime = episodeRuntime
                        Seasons = validSeasons
                    }

                    let result =
                        executeCommand
                            conn sid
                            Series.Serialization.fromStoredEvent
                            Series.reconstitute
                            Series.decide
                            Series.Serialization.toEventData
                            (Series.Add_series_to_library seriesData)
                            projectionHandlers

                    match result with
                    | Error e -> return Error e
                    | Ok () ->
                        // Season/episode cache seed (series-r2xhv): imperative,
                        // command-time only — never sourced from projection
                        // replay (ADR-0043/ADR-0045's cache-tier discipline).
                        SeriesRefresh.upsertSeasonEpisodeCache conn slug validSeasons
                        // Flat metadata cache seed (series-t3jkv): same
                        // command-time pattern — without this, a series added
                        // after the one-time startup seed has no
                        // series_metadata_cache row and shows no rating/
                        // overview/runtime forever.
                        MetadataCache.upsertSeriesMetadata conn slug seriesData.Overview seriesData.BackdropRef seriesData.TmdbRating seriesData.EpisodeRuntime

                        let! creditsResult = Tmdb.getTvSeriesCredits httpClient tmdbConfig tmdbId
                        match creditsResult with
                        | Ok credits ->
                            let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                            for castMember in topBilled do
                                let castImageRef =
                                    match castMember.ProfilePath with
                                    | Some p ->
                                        let ref = sprintf "cast/%d.jpg" castMember.Id
                                        let destPath = System.IO.Path.Combine(imageBasePath, ref)
                                        if not (ImageStore.imageExists imageBasePath ref) then
                                            try
                                                Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                                |> Async.RunSynchronously
                                            with _ -> ()
                                        Some ref
                                    | None -> None
                                let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                                CastStore.addSeriesCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)
                        | Error _ -> ()

                        return Ok slug
            with ex ->
                return Error $"Failed to add series: {ex.Message}"
        }

    /// Public entry point for adding a series. Short-circuits when the TMDB id is already in the
    /// library — avoids the ~20-second TMDB-fetch race that previously let parallel callers
    /// (Jellyfin auto-sync vs. manual add) create duplicate slugs for the same title.
    let private addSeriesToLibrary
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            match tryFindSeriesSlugByTmdbId conn tmdbId with
            | Some existing -> return Ok existing
            | None -> return! addSeriesToLibraryImpl conn httpClient getTmdbConfig imageBasePath projectionHandlers tmdbId
        }

    /// integration-n3vqa: encode/decode `SteamFamilyImportResult` (arrivals
    /// included) for the `steam_family_last_result` SettingsStore blob —
    /// same JSON-string-in-SettingsStore shape as `steam_family_members`
    /// above, so Settings can re-render the last import's arrivals after a
    /// reload instead of only right after a fresh click.
    let private encodeSteamFamilyArrival (a: Mediatheca.Shared.SteamFamilyArrival) =
        Thoth.Json.Net.Encode.object [
            "appId", Thoth.Json.Net.Encode.int a.AppId
            "name", Thoth.Json.Net.Encode.string a.Name
            "acquiredDate", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string a.AcquiredDate
            "addedBy", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string a.AddedBy
        ]

    let private decodeSteamFamilyArrival : Thoth.Json.Net.Decoder<Mediatheca.Shared.SteamFamilyArrival> =
        Thoth.Json.Net.Decode.object (fun get -> {
            Mediatheca.Shared.SteamFamilyArrival.AppId = get.Required.Field "appId" Thoth.Json.Net.Decode.int
            Name = get.Required.Field "name" Thoth.Json.Net.Decode.string
            AcquiredDate = get.Optional.Field "acquiredDate" Thoth.Json.Net.Decode.string
            AddedBy = get.Optional.Field "addedBy" Thoth.Json.Net.Decode.string
        })

    let private encodeSteamFamilyImportResult (r: Mediatheca.Shared.SteamFamilyImportResult) : string =
        Thoth.Json.Net.Encode.object [
            "familyMembers", Thoth.Json.Net.Encode.int r.FamilyMembers
            "gamesProcessed", Thoth.Json.Net.Encode.int r.GamesProcessed
            "gamesCreated", Thoth.Json.Net.Encode.int r.GamesCreated
            "familyOwnersSet", Thoth.Json.Net.Encode.int r.FamilyOwnersSet
            "arrivals", r.Arrivals |> List.map encodeSteamFamilyArrival |> Thoth.Json.Net.Encode.list
            "sinceLastSync", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string r.SinceLastSync
            "errors", r.Errors |> List.map Thoth.Json.Net.Encode.string |> Thoth.Json.Net.Encode.list
        ]
        |> Thoth.Json.Net.Encode.toString 0

    let private decodeSteamFamilyImportResult : Thoth.Json.Net.Decoder<Mediatheca.Shared.SteamFamilyImportResult> =
        Thoth.Json.Net.Decode.object (fun get -> {
            Mediatheca.Shared.SteamFamilyImportResult.FamilyMembers = get.Required.Field "familyMembers" Thoth.Json.Net.Decode.int
            GamesProcessed = get.Required.Field "gamesProcessed" Thoth.Json.Net.Decode.int
            GamesCreated = get.Required.Field "gamesCreated" Thoth.Json.Net.Decode.int
            FamilyOwnersSet = get.Required.Field "familyOwnersSet" Thoth.Json.Net.Decode.int
            Arrivals = get.Optional.Field "arrivals" (Thoth.Json.Net.Decode.list decodeSteamFamilyArrival) |> Option.defaultValue []
            SinceLastSync = get.Optional.Field "sinceLastSync" Thoth.Json.Net.Decode.string
            Errors = get.Optional.Field "errors" (Thoth.Json.Net.Decode.list Thoth.Json.Net.Decode.string) |> Option.defaultValue []
        })

    /// integration-n3vqa: `Incremental` (the default click) skips
    /// `getSteamStoreDetails` and its downstream identity-card/facet/
    /// release-date updates for apps `GameProjection.findBySteamAppId`
    /// already knows — the fix for the burst-enumerate-everything traffic
    /// shape Valve flagged. `FullReenrich` reproduces pre-n3vqa behaviour
    /// (appdetails for every app, known or new) as an explicit second
    /// action, never the default.
    type SteamFamilyImportMode =
        | Incremental
        | FullReenrich

    let runSteamFamilyImport
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (emit: SteamFamilyImportProgress -> unit)
        (mode: SteamFamilyImportMode)
        : Async<Result<SteamFamilyImportResult, string>> = async {
            let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
                executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
            try
                let accessToken =
                    SettingsStore.getSetting conn "steam_family_token"
                    |> Option.defaultValue ""
                if System.String.IsNullOrWhiteSpace(accessToken) then
                    return Error "Steam Family access token not configured"
                else
                    // integration-v0xmv: no mint-and-retry, no refresh token —
                    // the browser-obtained access token pasted in Settings is
                    // the only credential; a 401/403 surfaces as a typed
                    // "paste a fresh token" error (`mapFamilyFetchError`).
                    let! familyResult = Steam.getFamilyGroupForUser httpClient accessToken
                    match familyResult with
                    | Error e -> return Error e
                    | Ok familyGroupBasic ->
                        let existingMembersJson =
                            SettingsStore.getSetting conn "steam_family_members"
                            |> Option.defaultValue "[]"
                        let memberDecoder =
                            Thoth.Json.Net.Decode.list (
                                Thoth.Json.Net.Decode.object (fun get -> {
                                    Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                                    DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                                    FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                                    IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                                })
                            )
                        let memberMappings =
                            match Thoth.Json.Net.Decode.fromString memberDecoder existingMembersJson with
                            | Ok m -> m
                            | Error _ -> []

                        let steamIdToFriendSlug =
                            memberMappings
                            |> List.choose (fun m ->
                                match m.FriendSlug with
                                | Some slug -> Some (m.SteamId, slug)
                                | None -> None)
                            |> Map.ofList

                        let! sharedResult = Steam.getSharedLibraryApps httpClient accessToken familyGroupBasic.FamilyGroupid
                        match sharedResult with
                        | Error e -> return Error e
                        | Ok sharedApps ->
                            let mutable gamesProcessed = 0
                            let mutable gamesCreated = 0
                            let mutable familyOwnersSet = 0
                            let mutable errors: string list = []
                            let mutable arrivals: Mediatheca.Shared.SteamFamilyArrival list = []

                            // integration-n3vqa: the cursor this run's "what's new"
                            // diff is measured against — read BEFORE this run
                            // overwrites `steam_family_last_sync` below. None on a
                            // library's first-ever import (no prior sync to diff
                            // against, so every app counts as an arrival).
                            let previousLastSync = SettingsStore.getSetting conn "steam_family_last_sync"
                            let previousLastSyncUnixSeconds =
                                previousLastSync
                                |> Option.bind (fun iso ->
                                    match System.DateTimeOffset.TryParse(
                                            iso,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.RoundtripKind) with
                                    | true, dto -> Some (dto.ToUnixTimeSeconds())
                                    | false, _ -> None)

                            // Steam's GetSharedLibraryApps may omit the authenticated user's
                            // own Steam ID from owner_steamids. Supplement with their owned
                            // games — a best-effort call keyed on the *Web API key*, a
                            // different credential from the family token above. A rejected
                            // key (integration-r8kwd: Valve revokes it as part of an
                            // "account possibly compromised" flag) must not abort the whole
                            // import — it degrades to "own-ownership not set" plus one
                            // attributed error line naming the right credential and remedy,
                            // the same fault-isolation discipline the per-app loop below
                            // already follows (ADR-0010).
                            let steamConfig = getSteamConfig()
                            let! ownedGamesResult = Steam.tryGetOwnedGames httpClient steamConfig
                            let ownedGames =
                                match ownedGamesResult with
                                | Ok [] ->
                                    // integration-k4vqm: an empty owned-games response is
                                    // genuinely ambiguous — Steam's IPlayerService/GetOwnedGames
                                    // returns this exact same `{"response":{}}` shape whether
                                    // the account owns nothing OR its Game Details privacy is
                                    // not Public. It is NOT evidence the key is bad, so it must
                                    // NOT clear a standing `steam_api_key_last_error` notice —
                                    // but it costs the same own-ownership backfill degradation
                                    // a key rejection does, so it earns its own non-fatal error
                                    // line, worded distinctly from KeyRejected's so the two
                                    // causes are never confused.
                                    let msg =
                                        "Steam Web API key returned no owned games — the account's " +
                                        "Game Details privacy may not be Public, or it genuinely owns " +
                                        "nothing; own-ownership was not backfilled this run"
                                    errors <- errors @ [ msg ]
                                    []
                                | Ok games ->
                                    // A genuinely informative success — clear any previously
                                    // persisted key rejection (e.g. the builder regenerated it).
                                    SettingsStore.deleteSetting conn "steam_api_key_last_error"
                                    games
                                | Error Steam.KeyRejected ->
                                    let msg = Steam.webApiKeyRejectedMessage
                                    SettingsStore.setSetting conn "steam_api_key_last_error" msg
                                    errors <- errors @ [ msg ]
                                    []
                                | Error (Steam.WebApiOtherFailure m) ->
                                    errors <- errors @ [ sprintf "Steam Web API key check failed: %s" m ]
                                    []
                            let userOwnedAppIds = ownedGames |> List.map (fun g -> g.AppId) |> Set.ofList
                            let userSteamId = steamConfig.SteamId

                            let enrichedApps =
                                if System.String.IsNullOrWhiteSpace(userSteamId) then sharedApps
                                else
                                    sharedApps
                                    |> List.map (fun app ->
                                        if userOwnedAppIds |> Set.contains app.Appid
                                           && not (app.OwnerSteamids |> List.contains userSteamId) then
                                            { app with OwnerSteamids = userSteamId :: app.OwnerSteamids }
                                        else app)

                            let total = enrichedApps.Length

                            let setFamilyOwners (sid: string) (app: Steam.SteamSharedLibraryApp) =
                                for ownerSteamId in app.OwnerSteamids do
                                    if ownerSteamId = userSteamId && not (System.String.IsNullOrWhiteSpace(userSteamId)) then
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                Games.Mark_as_owned
                                                projectionHandlers
                                        match result with
                                        | Ok () -> familyOwnersSet <- familyOwnersSet + 1
                                        | Error _ -> ()
                                    else
                                        match steamIdToFriendSlug |> Map.tryFind ownerSteamId with
                                        | Some friendSlug ->
                                            let result =
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Add_family_owner friendSlug)
                                                    projectionHandlers
                                            match result with
                                            | Ok () -> familyOwnersSet <- familyOwnersSet + 1
                                            | Error _ -> ()
                                        | None -> ()

                            // integration-n3vqa: a family member steamid this app
                            // is credited to, resolved to a display label ("You"
                            // for the caller, a Friends-BC slug where mapped) —
                            // best-effort, first match wins; None when no owning
                            // steamid on the app has a mapping yet.
                            let ownerLabelFor (app: Steam.SteamSharedLibraryApp) : string option =
                                app.OwnerSteamids
                                |> List.tryPick (fun sid ->
                                    if sid = userSteamId && not (System.String.IsNullOrWhiteSpace(userSteamId)) then Some "You"
                                    else steamIdToFriendSlug |> Map.tryFind sid)

                            for app in enrichedApps do
                                try
                                    gamesProcessed <- gamesProcessed + 1

                                    let existingByAppId = GameProjection.findBySteamAppId conn app.Appid
                                    let isKnownByAppId = existingByAppId.IsSome
                                    // "what's new" — any brand-new app, plus any
                                    // already-known one whose rt_time_acquired
                                    // postdates the previous sync (someone in the
                                    // family bought a game the caller already
                                    // owns is still news). No prior cursor (first
                                    // -ever import) means everything is new.
                                    let isNewlyAcquired =
                                        not isKnownByAppId
                                        || match previousLastSyncUnixSeconds with
                                           | Some cursor -> app.RtTimeAcquired > 0 && int64 app.RtTimeAcquired > cursor
                                           | None -> true
                                    if isNewlyAcquired then
                                        arrivals <- arrivals @ [ {
                                            Mediatheca.Shared.SteamFamilyArrival.AppId = app.Appid
                                            Name = app.Name
                                            AcquiredDate = Steam.unixTimestampToDateString app.RtTimeAcquired
                                            AddedBy = ownerLabelFor app
                                        } ]

                                    match existingByAppId with
                                    | Some slug ->
                                        let sid = Games.streamId slug
                                        setFamilyOwners sid app
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                            projectionHandlers |> ignore
                                        // integration-n3vqa: the default (Incremental)
                                        // path skips the Steam Store fetch entirely for
                                        // an already-known app — this is the fix for
                                        // the burst-enumerate-everything traffic shape.
                                        // FullReenrich (the explicit second action)
                                        // reproduces the old always-fetch behaviour.
                                        if mode = FullReenrich then
                                            let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                            match storeDetails with
                                            | Ok details ->
                                                if details.AboutTheGame <> "" then
                                                    let desc = Steam.storeDescription details
                                                    if desc <> "" then
                                                        updateGameIdentityCache conn slug None (Some details.ShortDescription) None
                                                if details.WebsiteUrl.IsSome then
                                                    updateGameIdentityCache conn slug None None (Some details.WebsiteUrl)
                                                updateGameFacetsFromCategoryIds conn slug details.CategoryIds
                                                updateGameReleaseDate conn slug details
                                            | Error _ -> ()
                                        emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Matched" }
                                    | None ->
                                        let existingByName =
                                            if app.Name <> "" then GameProjection.findByName conn app.Name
                                            else []
                                        match existingByName with
                                        | (slug, _) :: _ ->
                                            let sid = Games.streamId slug
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_app_id app.Appid)
                                                projectionHandlers |> ignore
                                            setFamilyOwners sid app
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                                projectionHandlers |> ignore
                                            // Fetch Steam Store details for description, website, and facets
                                            let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                            match storeDetails with
                                            | Ok details ->
                                                if details.AboutTheGame <> "" then
                                                    updateGameIdentityCache conn slug None (Some details.ShortDescription) None
                                                if details.WebsiteUrl.IsSome then
                                                    updateGameIdentityCache conn slug None None (Some details.WebsiteUrl)
                                                updateGameFacetsFromCategoryIds conn slug details.CategoryIds
                                                updateGameReleaseDate conn slug details
                                            | Error _ -> ()
                                            emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Matched by name" }
                                        | [] ->
                                            if app.Name = "" then
                                                errors <- errors @ [ sprintf "App %d has no name, skipping" app.Appid ]
                                                emit { Current = gamesProcessed; Total = total; GameName = (sprintf "App %d" app.Appid); Action = "Skipped" }
                                            else
                                                let rawgConfig = getRawgConfig()
                                                let! rawgResults =
                                                    if not (System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey)) then
                                                        Rawg.searchGames httpClient rawgConfig app.Name None
                                                    else
                                                        async { return [] }

                                                let rawgMatch = rawgResults |> List.tryHead

                                                let rawgDescription, genres, rawgId, rawgRating, year =
                                                    match rawgMatch with
                                                    | Some r ->
                                                        let rawgYear = r.Year |> Option.defaultValue 0
                                                        "", r.Genres, Some r.RawgId, r.Rating, rawgYear
                                                    | None ->
                                                        "", [], None, None, 0

                                                // Fetch Steam Store details for description, website, and facets
                                                let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                                let steamDescription, steamShortDescription, steamWebsiteUrl, steamCategoryIds =
                                                    match storeDetails with
                                                    | Ok details ->
                                                        Steam.storeDescription details, details.ShortDescription, details.WebsiteUrl, details.CategoryIds
                                                    | Error _ -> "", "", None, []

                                                let description =
                                                    if steamDescription <> "" then steamDescription
                                                    elif rawgDescription <> "" then rawgDescription
                                                    else ""

                                                // games-r1tx4 (verifier iteration 2): the
                                                // plain-text sibling of `description` above,
                                                // for the `Game_added_to_library` payload --
                                                // an event never carries HTML (ADR-0043), even
                                                // though `description` (sanitized HTML when it
                                                // came from Steam) is what the identity-card
                                                // cache write below keeps.
                                                let plainDescription =
                                                    if steamDescription <> "" then DescriptionSanitizer.toPlainText steamDescription
                                                    elif rawgDescription <> "" then rawgDescription
                                                    else ""

                                                let baseSlug = Slug.gameSlug app.Name (if year > 0 then year else 2000)
                                                let slug = generateUniqueSlug conn Games.streamId baseSlug
                                                let! coverRef = Steam.downloadSteamCover httpClient app.Appid slug imageBasePath
                                                let! backdropRef = Steam.downloadSteamBackdrop httpClient app.Appid slug imageBasePath

                                                let gameData: Games.GameAddedData = {
                                                    Name = app.Name
                                                    Year = if year > 0 then year else 0
                                                    Genres = genres
                                                    Description = plainDescription
                                                    ShortDescription = steamShortDescription
                                                    WebsiteUrl = steamWebsiteUrl
                                                    CoverRef = coverRef
                                                    BackdropRef = backdropRef
                                                    RawgId = rawgId
                                                    RawgRating = rawgRating
                                                }

                                                let sid = Games.streamId slug
                                                let result =
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Add_game gameData)
                                                        projectionHandlers

                                                match result with
                                                | Ok () ->
                                                    gamesCreated <- gamesCreated + 1
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_steam_app_id app.Appid)
                                                        projectionHandlers |> ignore
                                                    setFamilyOwners sid app
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                                        projectionHandlers |> ignore
                                                    // games-v4nqe (hazard 1): creation code path writes the
                                                    // identity card + derived facets directly, imperatively,
                                                    // never the ProjectionHandler (ADR-0045).
                                                    MetadataCache.upsertGameIdentityCard conn slug {
                                                        Description = description
                                                        ShortDescription = steamShortDescription
                                                        WebsiteUrl = steamWebsiteUrl
                                                    }
                                                    // games-fffvm: a creation-path identity-card
                                                    // write always carries a sanitizer-output
                                                    // description (Steam's or RAWG's, possibly
                                                    // empty when both failed) — stamp so the row
                                                    // never lands in the backfill's own candidate set.
                                                    MetadataCache.stampDescriptionFetched conn slug
                                                    updateGameFacetsFromCategoryIds conn slug steamCategoryIds
                                                    match storeDetails with
                                                    | Ok details -> updateGameReleaseDate conn slug details
                                                    | Error _ -> ()
                                                    emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Created" }
                                                | Error e ->
                                                    errors <- errors @ [ sprintf "Failed to create '%s': %s" app.Name e ]
                                                    emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Error" }
                                with ex ->
                                    errors <- errors @ [ sprintf "Error processing app %d: %s" app.Appid ex.Message ]
                                    emit { Current = gamesProcessed; Total = total; GameName = (sprintf "App %d" app.Appid); Action = "Error" }

                            // Persist last sync time for Steam Family
                            SettingsStore.setSetting conn "steam_family_last_sync" (System.DateTime.UtcNow.ToString("o"))

                            let importResult : Mediatheca.Shared.SteamFamilyImportResult = {
                                FamilyMembers = memberMappings.Length
                                GamesProcessed = gamesProcessed
                                GamesCreated = gamesCreated
                                FamilyOwnersSet = familyOwnersSet
                                Arrivals = arrivals
                                SinceLastSync = previousLastSync
                                Errors = errors
                            }
                            // integration-n3vqa: persist the last completed
                            // result (mirroring the `steam_family_members`
                            // JSON-in-SettingsStore pattern above) so Settings
                            // can re-render "N new since ..." after a reload,
                            // not only right after a fresh click.
                            SettingsStore.setSetting conn "steam_family_last_result" (encodeSteamFamilyImportResult importResult)

                            return Ok importResult
            with ex ->
                return Error $"Steam Family import failed: {ex.Message}"
        }

    let steamFamilyImportHandler
        (factory: unit -> SqliteConnection)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (mode: SteamFamilyImportMode)
        : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                use conn = factory ()
                ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("text/event-stream")
                ctx.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                ctx.Response.Headers.["Connection"] <- Microsoft.Extensions.Primitives.StringValues("keep-alive")

                let writer = ctx.Response

                let writeEvent (eventType: string) (json: string) = task {
                    let line = Sse.sseFrame eventType json
                    let bytes = System.Text.Encoding.UTF8.GetBytes(line)
                    do! writer.Body.WriteAsync(bytes, 0, bytes.Length)
                    do! writer.Body.FlushAsync()
                }

                let emit (progress: SteamFamilyImportProgress) =
                    let json = sprintf "\"current\":%d,\"total\":%d,\"gameName\":\"%s\",\"action\":\"%s\""
                                    progress.Current progress.Total
                                    (progress.GameName.Replace("\\", "\\\\").Replace("\"", "\\\""))
                                    progress.Action
                    writeEvent "progress" (sprintf "{%s}" json)
                    |> Async.AwaitTask |> Async.RunSynchronously

                let! result =
                    runSteamFamilyImport conn httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers emit mode
                    |> Async.StartAsTask

                match result with
                | Ok r ->
                    // integration-n3vqa: built via the shared Thoth encoder (not
                    // hand-rolled sprintf) now that the payload carries nested
                    // arrivals objects — `errors` stays the final field so
                    // `Sse.sseFrame`'s brace-trim (which strips ALL trailing
                    // `}` characters) only ever sees one to strip, matching the
                    // previous shape's `[...]}` ending.
                    let json = encodeSteamFamilyImportResult r
                    do! writeEvent "complete" json
                | Error e ->
                    let escaped = e.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    do! writeEvent "error" (sprintf "{\"message\":\"%s\"}" escaped)

                return! earlyReturn ctx
            }

    /// Effects for `JellyfinImport.syncMovieWatchHistory`, factored out so
    /// both `runJellyfinImport`'s bulk Phase 2 loop and
    /// `removeLocalCopy`'s mandatory preserve-watch-history step
    /// (integration-r4vzm, ADR-0071) write watch sessions identically.
    let private movieWatchHistoryEffects
        (conn: SqliteConnection)
        (movieProjections: Projection.ProjectionHandler list)
        : (string -> string -> bool) * (string -> int option) * (string -> string -> int option -> Result<unit, string>) =
        let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
            executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
        let existsOnDate (slug: string) (date: string) : bool =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions WHERE movie_slug = @slug AND SUBSTR(date, 1, 10) = @date"
            |> Db.setParams [ "slug", SqlType.String slug; "date", SqlType.String date ]
            |> Db.querySingle (fun (rd: System.Data.IDataReader) -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
            |> fun c -> c > 0
        let getRuntime (slug: string) : int option =
            conn
            |> Db.newCommand "SELECT runtime FROM movie_detail WHERE slug = @slug"
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: System.Data.IDataReader) ->
                if rd.IsDBNull(rd.GetOrdinal("runtime")) then None
                else Some (rd.ReadInt32 "runtime"))
            |> Option.flatten
        let writeSession (slug: string) (date: string) (runtime: int option) : Result<unit, string> =
            let sessionData: Movies.WatchSessionRecordedData = {
                SessionId = System.Guid.NewGuid().ToString("N")
                Date = date
                Duration = runtime
                FriendSlugs = []
            }
            let sid = Movies.streamId slug
            executeCommand
                conn sid
                Movies.Serialization.fromStoredEvent
                Movies.reconstitute
                Movies.decide
                Movies.Serialization.toEventData
                (Movies.Record_watch_session sessionData)
                movieProjections
        (existsOnDate, getRuntime, writeSession)

    /// The series sibling of `movieWatchHistoryEffects`'s `writeSession`,
    /// factored out for the same reason -- reused by `removeLocalCopy`'s
    /// preserve-watch-history step.
    let private seriesWatchHistoryWriteEpisode
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        : string -> string -> int -> int -> string -> Result<unit, string> =
        let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
            executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
        fun (slug: string) (defaultRewatchId: string) (seasonNum: int) (epNum: int) (watchDate: string) ->
            let sid = Series.streamId slug
            executeCommand
                conn sid
                Series.Serialization.fromStoredEvent
                Series.reconstitute
                Series.decide
                Series.Serialization.toEventData
                (Series.Mark_episode_watched {
                    RewatchId = defaultRewatchId
                    SeasonNumber = seasonNum
                    EpisodeNumber = epNum
                    Date = watchDate
                })
                projectionHandlers

    let runJellyfinImport
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (getJellyfinConfig: unit -> Jellyfin.JellyfinConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<JellyfinImportResult, string>> = async {
            try
                let config = getJellyfinConfig ()
                if System.String.IsNullOrWhiteSpace(config.AccessToken) || System.String.IsNullOrWhiteSpace(config.UserId) then
                    return Error "Jellyfin not configured. Please test the connection first."
                else
                    // Persist a fresh token/user after a self-healing re-auth (integration-002),
                    // and refresh the in-flight config so subsequent fetches in this run use it.
                    let mutable config = config
                    let persistAuth (auth: Jellyfin.JellyfinAuthResult) =
                        SettingsStore.setSetting conn "jellyfin_user_id" auth.UserId
                        SettingsStore.setSetting conn "jellyfin_access_token" auth.AccessToken
                        config <- { config with UserId = auth.UserId; AccessToken = auth.AccessToken }
                    let movieProjections = projectionHandlers
                    let mutable moviesAdded = 0
                    let mutable episodesAdded = 0
                    let mutable moviesAutoAdded = 0
                    let mutable seriesAutoAdded = 0
                    let mutable itemsSkipped = 0
                    let mutable errors: string list = []

                    // --- Movie watch sync (REQ-304) ---
                    let! moviesResult = Jellyfin.getMoviesWithReauth httpClient config persistAuth
                    match moviesResult with
                    | Error e -> errors <- errors @ [sprintf "Failed to fetch Jellyfin movies: %s" e]
                    | Ok jellyfinMovies ->
                        // Build TMDB ID -> (slug, name) lookup
                        let mutable moviesByTmdbId =
                            conn
                            |> Db.newCommand "SELECT slug, name, tmdb_id FROM movie_detail"
                            |> Db.query (fun (rd: System.Data.IDataReader) ->
                                let tmdbId = rd.ReadInt32 "tmdb_id"
                                let slug = rd.ReadString "slug"
                                let name = rd.ReadString "name"
                                (tmdbId, (slug, name)))
                            |> Map.ofList

                        // Phase 1: Auto-add unmatched movies with TMDB IDs
                        for item in jellyfinMovies do
                            let tmdbId =
                                item.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid when not (Map.containsKey tid moviesByTmdbId) ->
                                try
                                    let! addResult = addMovieToLibrary conn httpClient getTmdbConfig imageBasePath movieProjections tid
                                    match addResult with
                                    | Ok slug ->
                                        moviesByTmdbId <- Map.add tid (slug, item.Name) moviesByTmdbId
                                        moviesAutoAdded <- moviesAutoAdded + 1
                                    | Error e ->
                                        errors <- errors @ [sprintf "Auto-add movie '%s' (TMDB %d): %s" item.Name tid e]
                                with ex ->
                                    errors <- errors @ [sprintf "Auto-add movie '%s' (TMDB %d): %s" item.Name tid ex.Message]
                            | _ -> ()

                        // Clear all existing Jellyfin IDs before re-populating (handles removed items)
                        JellyfinStore.clearAll conn

                        // Phase 1b: Persist Jellyfin IDs for all matched movies
                        for item in jellyfinMovies do
                            let tmdbId =
                                item.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid ->
                                match Map.tryFind tid moviesByTmdbId with
                                | Some (slug, _) ->
                                    JellyfinStore.setMovieJellyfinId conn slug item.Id
                                | None -> ()
                            | None -> ()

                        // Phase 2: Sync watch history. Matched-with-tmdb movies go into a
                        // batch (isolating a bad write from the rest, integration-001's
                        // pattern); an unmatched or tmdb-less item is skipped right here
                        // exactly as before, since JellyfinImport.syncMovieWatchHistory
                        // (integration-r4vzm) only ever sees already-matched items.
                        let mutable movieBatch: (string * Jellyfin.JellyfinBaseItem) list = []
                        for item in jellyfinMovies do
                            let tmdbId =
                                item.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid ->
                                match Map.tryFind tid moviesByTmdbId with
                                | Some (slug, _name) -> movieBatch <- movieBatch @ [ (slug, item) ]
                                | None -> itemsSkipped <- itemsSkipped + 1
                            | None -> itemsSkipped <- itemsSkipped + 1
                        let (existsOnDate, getRuntime, writeSession) = movieWatchHistoryEffects conn movieProjections
                        let movieWatchSync = JellyfinImport.syncMovieWatchHistory movieBatch existsOnDate getRuntime writeSession
                        moviesAdded <- moviesAdded + movieWatchSync.MoviesAdded
                        itemsSkipped <- itemsSkipped + movieWatchSync.ItemsSkipped
                        errors <- errors @ movieWatchSync.Errors

                    // --- Series episode watch sync (REQ-305) ---
                    let! seriesResult = Jellyfin.getSeriesWithReauth httpClient config persistAuth
                    match seriesResult with
                    | Error e -> errors <- errors @ [sprintf "Failed to fetch Jellyfin series: %s" e]
                    | Ok jellyfinSeries ->
                        let mutable seriesByTmdbId =
                            conn
                            |> Db.newCommand "SELECT slug, name, tmdb_id FROM series_detail"
                            |> Db.query (fun (rd: System.Data.IDataReader) ->
                                let tmdbId = rd.ReadInt32 "tmdb_id"
                                let slug = rd.ReadString "slug"
                                let name = rd.ReadString "name"
                                (tmdbId, (slug, name)))
                            |> Map.ofList

                        // Phase 1: Auto-add unmatched series with TMDB IDs
                        for seriesItem in jellyfinSeries do
                            let tmdbId =
                                seriesItem.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid when not (Map.containsKey tid seriesByTmdbId) ->
                                try
                                    let! addResult = addSeriesToLibrary conn httpClient getTmdbConfig imageBasePath projectionHandlers tid
                                    match addResult with
                                    | Ok slug ->
                                        seriesByTmdbId <- Map.add tid (slug, seriesItem.Name) seriesByTmdbId
                                        seriesAutoAdded <- seriesAutoAdded + 1
                                    | Error e ->
                                        errors <- errors @ [sprintf "Auto-add series '%s' (TMDB %d): %s" seriesItem.Name tid e]
                                with ex ->
                                    errors <- errors @ [sprintf "Auto-add series '%s' (TMDB %d): %s" seriesItem.Name tid ex.Message]
                            | _ -> ()

                        // Phase 1b: Persist Jellyfin IDs for all matched series + episodes
                        for seriesItem in jellyfinSeries do
                            let tmdbId =
                                seriesItem.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid ->
                                match Map.tryFind tid seriesByTmdbId with
                                | Some (slug, _) ->
                                    // Persist series Jellyfin ID
                                    JellyfinStore.setSeriesJellyfinId conn slug seriesItem.Id
                                    // Fetch and persist episode Jellyfin IDs
                                    let! episodesForIds = Jellyfin.getEpisodesWithReauth httpClient config persistAuth seriesItem.Id
                                    match episodesForIds with
                                    | Ok eps ->
                                        for ep in eps do
                                            match ep.ParentIndexNumber, ep.IndexNumber with
                                            | Some seasonNum, Some episodeNum ->
                                                JellyfinStore.setEpisodeJellyfinId conn slug seasonNum episodeNum ep.Id
                                            | _ -> ()
                                    | Error _ -> ()
                                | None -> ()
                            | None -> ()

                        // Phase 2: Sync watch history.
                        // Fetch episodes for every matched series into a batch first
                        // (isolating fetch errors per series), then delegate the write
                        // loop to JellyfinImport.syncSeriesWatchHistory, which isolates
                        // a fault on one series/episode so it cannot abort the rest of
                        // the run (integration-001). The previous structure let a single
                        // SqliteException escape executeCommand and abort everything.
                        let mutable seriesBatch: (string * Jellyfin.JellyfinBaseItem list) list = []
                        for seriesItem in jellyfinSeries do
                            let tmdbId =
                                seriesItem.ProviderIds.Tmdb
                                |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                            match tmdbId with
                            | Some tid ->
                                match Map.tryFind tid seriesByTmdbId with
                                | Some (slug, _name) ->
                                    let! episodesResult = Jellyfin.getEpisodesWithReauth httpClient config persistAuth seriesItem.Id
                                    match episodesResult with
                                    | Error e -> errors <- errors @ [sprintf "Series '%s' episodes: %s" slug e]
                                    | Ok episodes -> seriesBatch <- seriesBatch @ [ (slug, episodes) ]
                                | None -> ()
                            | None -> ()

                        // Materialize season/episode metadata for anything Jellyfin holds
                        // that TMDB has not (yet) published (integration-m4k7p). MUST run
                        // before the watch-history sync below so the row exists when
                        // Mark_episode_watched recomputes progress / next-up. TMDB stays
                        // authoritative: rows are tagged source='jellyfin' and a later TMDB
                        // refresh enriches them in place. Stills are fetched best-effort
                        // (integration-007, closes the ADR 0012 deferral): downloaded via
                        // Jellyfin.getPrimaryImageWithReauth (reuses the ADR 0011 re-auth
                        // policy) and saved under a distinct `-jellyfin.jpg` suffix so a
                        // later TMDB refresh is never short-circuited into keeping the
                        // Jellyfin bytes. Any failure degrades to None, never an error.
                        // Also backfills the still for rows this materialization already
                        // created on an earlier run but left NULL (integration-q7wv3, the
                        // gap integration-007's materialize-time fetch never reached) —
                        // same best-effort fetch, written via the dedicated UPDATE path
                        // `SeriesProjection.backfillEpisodeStill` since INSERT OR IGNORE
                        // cannot touch an already-existing row.
                        let materializeResult =
                            JellyfinImport.materializeMissingEpisodes
                                seriesBatch
                                (SeriesProjection.getExistingEpisodeKeys conn)
                                (SeriesProjection.getExistingSeasonNumbers conn)
                                (SeriesProjection.getJellyfinEpisodesMissingStill conn)
                                (JellyfinImport.fetchEpisodeStill
                                    (fun jellyfinItemId ->
                                        Jellyfin.getPrimaryImageWithReauth httpClient config persistAuth jellyfinItemId
                                        |> Async.RunSynchronously)
                                    (fun ref bytes -> ImageStore.saveImage imageBasePath ref bytes))
                                (fun slug seasonNum epNum stillRef ->
                                    try SeriesProjection.backfillEpisodeStill conn slug seasonNum epNum stillRef; Ok ()
                                    with ex -> Error ex.Message)
                                (fun slug seasonNum ->
                                    try SeriesProjection.materializeSeason conn slug seasonNum; Ok ()
                                    with ex -> Error ex.Message)
                                (fun slug ep ->
                                    try SeriesProjection.materializeEpisode conn slug ep; Ok ()
                                    with ex -> Error ex.Message)
                        errors <- errors @ materializeResult.Errors

                        let writeEpisode = seriesWatchHistoryWriteEpisode conn projectionHandlers

                        let watchSync =
                            JellyfinImport.syncSeriesWatchHistory
                                seriesBatch
                                (SeriesProjection.getDefaultRewatchId conn)
                                (SeriesProjection.getWatchedEpisodesForSession conn)
                                writeEpisode
                        episodesAdded <- episodesAdded + watchSync.EpisodesAdded
                        itemsSkipped <- itemsSkipped + watchSync.ItemsSkipped
                        errors <- errors @ watchSync.Errors

                    let importResult: JellyfinImportResult = {
                        MoviesAdded = moviesAdded
                        EpisodesAdded = episodesAdded
                        MoviesAutoAdded = moviesAutoAdded
                        SeriesAutoAdded = seriesAutoAdded
                        ItemsSkipped = itemsSkipped
                        Errors = errors
                    }
                    // Surface partial failure (integration-001): if any item errored
                    // the run is NOT a clean success — report it as Error so the sync
                    // status becomes SyncFailed instead of a silent SyncCompleted. The
                    // counts are folded into the message so the persisted failure still
                    // shows what *did* get written before/around the failures.
                    if List.isEmpty errors then
                        return Ok importResult
                    else
                        let summary =
                            sprintf "Jellyfin sync completed with %d error(s) (%d movies, %d episodes added; %d skipped):\n%s"
                                (List.length errors) moviesAdded episodesAdded itemsSkipped
                                (errors |> String.concat "\n")
                        return Error summary
            with ex ->
                return Error $"Jellyfin import failed: {ex.Message}"
        }

    /// Attaches a Steam App ID to an existing game by fetching Store details:
    /// still emits `Set_steam_app_id` (the link is our decision, ADR-0043),
    /// but description/short_description/website_url now write
    /// `game_metadata_cache` directly (games-v4nqe) — `Set_description`/
    /// `Set_short_description`/`Set_website_url`/`Add_play_mode` are demoted.
    /// Preserves the pre-cutover "only fill if currently empty" guard for
    /// description/short_description/website_url by reading the cache-backed
    /// `GameDetail` fields (already cache-sourced since games-a7dqx) instead
    /// of the now-dropped projection columns; play modes are replaced
    /// outright by facets derived from Steam's category ids, with no
    /// "don't clobber overrides" guard needed (ADR-0053).
    let private attachSteamToGameCore
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        (appId: int)
        : Async<Result<unit, string>> =
        async {
            let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
                executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers
            let! storeDetails = Steam.getSteamStoreDetails httpClient appId
            match storeDetails with
            | Error e -> return Error (sprintf "Steam lookup failed: %s" e)
            | Ok details ->
                let sid = Games.streamId slug
                // Current projected state (to avoid overwriting user edits)
                let current = GameProjection.getBySlug conn slug
                match current with
                | None -> return Error (sprintf "Game '%s' not found" slug)
                | Some game ->
                    // 1. Set steam_app_id
                    executeCommand conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Set_steam_app_id appId)
                        projectionHandlers |> ignore

                    // 2. Description — only if the current one is empty
                    let computedDesc = Steam.storeDescription details
                    let newDescription =
                        if System.String.IsNullOrWhiteSpace(game.Description) && computedDesc <> "" then computedDesc
                        else game.Description

                    // 3. Short description — only if empty
                    let newShortDescription =
                        if System.String.IsNullOrWhiteSpace(game.ShortDescription) && details.ShortDescription <> "" then
                            details.ShortDescription
                        else game.ShortDescription

                    // 4. Website URL — only if currently missing
                    let newWebsiteUrl =
                        if game.WebsiteUrl.IsNone && details.WebsiteUrl.IsSome then details.WebsiteUrl
                        else game.WebsiteUrl

                    MetadataCache.upsertGameIdentityCard conn slug {
                        Description = newDescription
                        ShortDescription = newShortDescription
                        WebsiteUrl = newWebsiteUrl
                    }
                    // games-fffvm: attaching Steam always writes a
                    // sanitizer-output description (Steam's, or the existing
                    // one echoed back unchanged when already non-empty) —
                    // stamp so this row drops out of the backfill's cursor.
                    MetadataCache.stampDescriptionFetched conn slug

                    // 5. Facets — derived from Steam's category ids
                    updateGameFacetsFromCategoryIds conn slug details.CategoryIds

                    // 6. Release date — always refreshed on a re-fetch (games-ev65k)
                    updateGameReleaseDate conn slug details

                    return Ok ()
        }

    /// games-k3vps: imports a game the user picked from the search modal's
    /// Steam source (a query-search result, not a library-owned game) —
    /// unlike `attachSteamToGameCore` this CREATES a new game rather than
    /// attaching to an existing one, and never dispatches `Mark_as_owned`
    /// (nothing here confirms Steam ownership). Mirrors the Steam-library
    /// import's "no match — create new game" branch (`importSteamLibrary`,
    /// above): identity-card fields (`Name`/`Year`/`Genres`) ride the
    /// `Add_game` event, description/short_description/website_url land in
    /// `game_metadata_cache` via THIS creation code path — never
    /// `GameProjection.handleEvent` (ADR-0043/ADR-0045) — and facets are
    /// derived from Steam's own category ids. `Genres` is `[]`: unlike the
    /// RAWG-sourced `addGame` path, there is no RAWG lookup here to source
    /// genres from (out of scope for this task).
    let private addGameFromSteamCore
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (request: AddGameFromSteamRequest)
        : Async<Result<AddGameOutcome, string>> =
        async {
            try
                // Duplicate check: by steam_app_id, then by exact
                // case-insensitive name — mirrors `addGame`'s RAWG-id-then-name
                // check. Skipped when the caller has already confirmed they
                // want a duplicate (SkipDuplicateCheck, set by the client's
                // "add as duplicate" action).
                let existing =
                    if request.SkipDuplicateCheck then None
                    else
                        let byAppId =
                            GameProjection.findBySteamAppId conn request.AppId
                            |> Option.map (fun slug ->
                                match GameProjection.getBySlug conn slug with
                                | Some g -> slug, g.Name
                                | None -> slug, request.Name)
                        match byAppId with
                        | Some _ -> byAppId
                        | None ->
                            match GameProjection.findByName conn request.Name with
                            | (existingSlug, _) :: _ ->
                                match GameProjection.getBySlug conn existingSlug with
                                | Some g -> Some (existingSlug, g.Name)
                                | None -> Some (existingSlug, request.Name)
                            | [] -> None

                match existing with
                | Some (existingSlug, existingName) ->
                    return Ok (Duplicate_found (existingSlug, existingName))
                | None ->
                    let year = request.Year |> Option.defaultValue 0
                    let baseSlug = Slug.gameSlug request.Name (if year > 0 then year else 2000)
                    let slug = generateUniqueSlug conn Games.streamId baseSlug
                    let sid = Games.streamId slug

                    let! storeDetails = Steam.getSteamStoreDetails httpClient request.AppId
                    let description, shortDescription, websiteUrl, categoryIds =
                        match storeDetails with
                        | Ok details ->
                            Steam.storeDescription details, details.ShortDescription, details.WebsiteUrl, details.CategoryIds
                        | Error _ -> "", "", None, []

                    // games-r1tx4 (verifier iteration 2): `description` above
                    // is sanitized HTML (Steam.storeDescription sanitizes at
                    // decode time) -- kept for the identity-card cache write
                    // below. The `Game_added_to_library` payload instead
                    // carries this plain-text projection: an event never
                    // carries HTML (ADR-0043).
                    let plainDescription = DescriptionSanitizer.toPlainText description

                    let! coverRef = Steam.downloadSteamCover httpClient request.AppId slug imageBasePath
                    let! backdropRef = Steam.downloadSteamBackdrop httpClient request.AppId slug imageBasePath

                    let gameData: Games.GameAddedData = {
                        Name = request.Name
                        Year = year
                        Genres = []
                        Description = plainDescription
                        ShortDescription = shortDescription
                        WebsiteUrl = websiteUrl
                        CoverRef = coverRef
                        BackdropRef = backdropRef
                        RawgId = None
                        RawgRating = None
                    }

                    let result =
                        executeCommandCore
                            conn sid
                            Games.Serialization.fromStoredEvent
                            Games.reconstitute
                            Games.decide
                            Games.Serialization.toEventData
                            (Games.Add_game gameData)
                            projectionHandlers

                    match result with
                    | Error e -> return Error e
                    | Ok () ->
                        executeCommandCore
                            conn sid
                            Games.Serialization.fromStoredEvent
                            Games.reconstitute
                            Games.decide
                            Games.Serialization.toEventData
                            (Games.Set_steam_app_id request.AppId)
                            projectionHandlers |> ignore

                        // games-k3vps (mirroring games-v4nqe, ADR-0045): creation
                        // code path writes the identity card + derived facets
                        // directly, imperatively, never the ProjectionHandler.
                        MetadataCache.upsertGameIdentityCard conn slug {
                            Description = description
                            ShortDescription = shortDescription
                            WebsiteUrl = websiteUrl
                        }
                        // games-fffvm: creation-path identity-card write —
                        // stamp so the row never lands in the backfill's set.
                        MetadataCache.stampDescriptionFetched conn slug
                        updateGameFacetsFromCategoryIds conn slug categoryIds
                        // games-ev65k: the Tenebris Somnia (appId 2121510)
                        // end-to-end path — release date lands on the cache
                        // in the same creation code path as the identity
                        // card and facets above.
                        match storeDetails with
                        | Ok details -> updateGameReleaseDate conn slug details
                        | Error _ -> ()

                        return Ok (Created slug)
            with ex ->
                return Error (sprintf "Failed to add game from Steam: %s" ex.Message)
        }

    /// Shared by `addBook` and `addBookFromOpenLibrary` (integration-c8d4x) —
    /// extracted so the Open Library import path reuses the exact same
    /// duplicate-check/slug/cover-download/command sequence rather than
    /// re-deriving it, the `addMovieToLibraryImpl`/`addMovieToLibrary`
    /// precedent above.
    let private addBookToLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (request: AddBookRequest)
        // integration-c8d4x, verifier iteration 2: when the caller already
        // owns a source-specific, correctly-throttled/UA'd cover fetch
        // (Open Library's `downloadCover`), it hands that in here keyed by
        // the slug this function computes, instead of this function falling
        // back to a bare, unthrottled `httpClient.GetAsync(request.CoverUrl)`.
        // `None` (the manual-entry `addBook` path) preserves the original
        // CoverUrl-fetch behaviour.
        (coverDownloader: (string -> Async<string option>) option)
        : Async<Result<AddBookOutcome, string>> = async {
            try
                let year = request.Year |> Option.defaultValue 0
                let baseSlug = Slug.bookSlug request.Title year

                // Duplicate check (books-y9kxy): any external id already
                // linked -> Duplicate_found; else case-insensitive title +
                // first author -> Duplicate_found. SkipDuplicateCheck
                // bypasses both.
                let existing =
                    if request.SkipDuplicateCheck then None
                    else
                        let byExternalId =
                            request.ExternalIds
                            |> List.tryPick (fun eid -> BookProjection.findByExternalId conn eid)
                        match byExternalId with
                        | Some existingSlug ->
                            match BookProjection.getBySlug conn existingSlug with
                            | Some b -> Some (existingSlug, b.Title)
                            | None -> Some (existingSlug, request.Title)
                        | None ->
                            let firstAuthor = request.Authors |> List.tryHead |> Option.map (fun a -> a.ToLowerInvariant())
                            BookProjection.findByTitle conn request.Title
                            |> List.tryFind (fun (candidateSlug, _) ->
                                match BookProjection.getBySlug conn candidateSlug with
                                | Some b -> (b.Authors |> List.tryHead |> Option.map (fun a -> a.ToLowerInvariant())) = firstAuthor
                                | None -> false)

                match existing with
                | Some (existingSlug, existingTitle) ->
                    return Ok (AddBookOutcome.Duplicate_found (existingSlug, existingTitle))
                | None ->
                    let slug = generateUniqueSlug conn Books.streamId baseSlug
                    let sid = Books.streamId slug

                    let! coverRef =
                        match coverDownloader with
                        | Some download -> download slug
                        | None ->
                            async {
                                match request.CoverUrl with
                                | None -> return None
                                | Some url ->
                                    try
                                        let! response = httpClient.GetAsync(url: string) |> Async.AwaitTask
                                        response.EnsureSuccessStatusCode() |> ignore
                                        let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                                        let relativePath = sprintf "posters/book-%s.jpg" slug
                                        ImageStore.saveImage imageBasePath relativePath bytes
                                        return Some relativePath
                                    with _ -> return None
                            }

                    let bookData: Books.BookAddedData = {
                        Title = request.Title
                        Authors = request.Authors
                        Year = request.Year
                        CoverRef = coverRef
                        Subjects = request.Subjects
                        Format = request.Format
                        ExternalIds = request.ExternalIds
                    }

                    let result =
                        executeCommandCore
                            conn sid
                            Books.Serialization.fromStoredEvent
                            Books.reconstitute
                            Books.decide
                            Books.Serialization.toEventData
                            (Books.Add_book_to_library bookData)
                            projectionHandlers

                    match result with
                    | Error e -> return Error e
                    | Ok () -> return Ok (AddBookOutcome.Book_added slug)
            with ex ->
                return Error $"Failed to add book: {ex.Message}"
        }

    /// `search.json`'s `edition_key` is an OLID (`OL33246498M`), never an
    /// ISBN — verifier iteration 1 caught `addBookFromOpenLibraryImpl`
    /// feeding it straight into `/isbn/{isbn}.json`, which 404s for any real
    /// hit. Shape-dispatch on the value instead: OLIDs are `OL...M`
    /// (`/books/{key}.json` — see `OpenLibrary.getEditionByOlid`); an
    /// ISBN-shaped value (10 or 13 digits, ISBN-10's trailing check digit
    /// may be `X`) still resolves via `/isbn/{isbn}.json` for any caller
    /// that happens to pass one.
    let private isOlidShaped (value: string) =
        value.StartsWith("OL", System.StringComparison.Ordinal)
        && value.EndsWith("M", System.StringComparison.Ordinal)

    let private isIsbnShaped (value: string) =
        (value.Length = 10 || value.Length = 13)
        && value
           |> Seq.mapi (fun i c -> i, c)
           |> Seq.forall (fun (i, c) -> System.Char.IsDigit c || (c = 'X' && i = value.Length - 1))

    /// integration-c8d4x (ADR-0075): fetches Open Library's work (and edition,
    /// when given) and turns it into an `AddBookRequest`, then reuses
    /// `addBookToLibraryImpl`. On `Book_added`, writes the cache slice
    /// (`MetadataCache.upsertBookMetadata`, `source = "openlibrary"`) — never
    /// the identity card (ADR-0043's identity-card clause).
    let private addBookFromOpenLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getOpenLibraryConfig: unit -> OpenLibrary.OpenLibraryConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (request: AddBookFromOpenLibraryRequest)
        : Async<Result<AddBookOutcome, string>> = async {
            try
                let config = getOpenLibraryConfig ()
                let! work = OpenLibrary.getWork httpClient config request.WorkKey
                let! editionOpt =
                    match request.EditionKey with
                    | Some key when isOlidShaped key -> OpenLibrary.getEditionByOlid httpClient config key
                    | Some key when isIsbnShaped key -> OpenLibrary.getEditionByIsbn httpClient config key
                    | Some _ | None -> async { return None }

                // books-xntts: the search hit's own `Title` is the fallback
                // when no edition resolves — never the work key string
                // (`request.WorkKey` reads like `/works/OL27448W`, which is
                // not a title).
                let title = editionOpt |> Option.map (fun e -> e.Title) |> Option.defaultValue request.Title
                let authors = editionOpt |> Option.map (fun e -> e.Authors) |> Option.defaultValue []
                let year =
                    editionOpt
                    |> Option.bind (fun e -> e.PublishDate)
                    |> Option.bind (fun d ->
                        if d.Length >= 4 then
                            match System.Int32.TryParse(d.[d.Length - 4 ..]) with
                            | true, y -> Some y
                            | _ -> None
                        else None)
                // books-xntts: the cover the user clicked in the search
                // tile (`request.CoverId`, the search hit's own `cover_i`)
                // is preferred over the resolved edition's `covers[0]` —
                // the edition Open Library resolves via `EditionKey` can
                // still be a different-language printing than the one the
                // tile showed, in which case its own cover is wrong for the
                // book the user actually picked.
                let coverId =
                    request.CoverId
                    |> Option.orElse (editionOpt |> Option.bind (fun e -> e.CoverId))
                // Isbn13 is never parsed out of the edition key — it rides
                // its own explicit field, populated from the search result's
                // own `Isbn13` (verifier iteration 1).
                let isbn13 = request.Isbn13
                let externalIds =
                    [ Some (OpenLibraryWork request.WorkKey)
                      editionOpt |> Option.map (fun e -> OpenLibraryEdition e.EditionKey)
                      isbn13 |> Option.map Isbn13 ]
                    |> List.choose id

                // The cover download is routed through
                // `OpenLibrary.downloadCover` (User-Agent + the covers gate)
                // rather than `addBookToLibraryImpl`'s bare-CoverUrl fallback
                // path (verifier iteration 1: that path issued an
                // unthrottled, UA-less request and left `downloadCover` dead
                // code). `addBookToLibraryImpl` invokes this with the slug it
                // computes internally, once the duplicate check clears.
                let coverDownloader : (string -> Async<string option>) option =
                    coverId
                    |> Option.map (fun id ->
                        fun (slug: string) -> OpenLibrary.downloadCover httpClient config id slug imageBasePath)

                let addRequest: AddBookRequest = {
                    Title = title
                    Authors = authors
                    Year = year
                    CoverUrl = None
                    Subjects = work.Subjects |> List.truncate 8
                    Format = BookFormat.Unknown
                    ExternalIds = externalIds
                    SkipDuplicateCheck = false
                }

                let! result = addBookToLibraryImpl conn httpClient imageBasePath projectionHandlers addRequest coverDownloader
                match result with
                | Ok (AddBookOutcome.Book_added slug) ->
                    let metadata: MetadataCache.BookMetadata = {
                        Description = work.Description
                        PageCount = editionOpt |> Option.bind (fun e -> e.PageCount)
                        RuntimeMinutes = None
                        Narrators = []
                        SeriesName = None
                        SeriesPosition = None
                        Publisher = editionOpt |> Option.bind (fun e -> e.Publishers |> List.tryHead)
                        PublishedDate = editionOpt |> Option.bind (fun e -> e.PublishDate)
                        AverageRating = None
                        Language = None
                        Source = Some "openlibrary"
                    }
                    MetadataCache.upsertBookMetadata conn slug metadata
                    return Ok (AddBookOutcome.Book_added slug)
                | other -> return other
            with ex ->
                return Error $"Failed to add book from Open Library: {ex.Message}"
        }

    /// integration-c8d4x (ADR-0043's identity-card clause): re-fetches the
    /// work/edition for a book that has an Open Library key or ISBN and
    /// rewrites the cache slice ONLY — never the identity card.
    let private refreshBookFromOpenLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getOpenLibraryConfig: unit -> OpenLibrary.OpenLibraryConfig)
        (slug: string)
        : Async<Result<unit, string>> = async {
            match BookProjection.getBySlug conn slug with
            | None -> return Error "Book not found"
            | Some book ->
                match book.OpenLibraryWorkKey, book.Isbn13 with
                | None, None -> return Error "Book has no Open Library work key or ISBN to refresh from"
                | workKeyOpt, isbnOpt ->
                    try
                        let config = getOpenLibraryConfig ()
                        let! workOpt =
                            match workKeyOpt with
                            | Some workKey -> async { let! w = OpenLibrary.getWork httpClient config workKey in return Some w }
                            | None -> async { return None }
                        let! editionOpt =
                            match isbnOpt with
                            | Some isbn -> OpenLibrary.getEditionByIsbn httpClient config isbn
                            | None -> async { return None }

                        let current = MetadataCache.tryGetBookMetadata conn slug
                        let metadata: MetadataCache.BookMetadata = {
                            Description = workOpt |> Option.bind (fun w -> w.Description) |> Option.orElse current.Description
                            PageCount = editionOpt |> Option.bind (fun e -> e.PageCount) |> Option.orElse current.PageCount
                            RuntimeMinutes = current.RuntimeMinutes
                            Narrators = current.Narrators
                            SeriesName = current.SeriesName
                            SeriesPosition = current.SeriesPosition
                            Publisher = (editionOpt |> Option.bind (fun e -> e.Publishers |> List.tryHead)) |> Option.orElse current.Publisher
                            PublishedDate = (editionOpt |> Option.bind (fun e -> e.PublishDate)) |> Option.orElse current.PublishedDate
                            AverageRating = current.AverageRating
                            Language = current.Language
                            Source = Some "openlibrary"
                        }
                        MetadataCache.upsertBookMetadata conn slug metadata
                        return Ok ()
                    with ex ->
                        return Error $"Failed to refresh book from Open Library: {ex.Message}"
        }

    /// integration-dhctm (ADR-0074): persists a freshly-minted access token
    /// (`Audible.withAccessToken`'s `persist` callback) — the same two
    /// `SettingsStore` writes every call site that mints a token needs.
    let private persistAudibleAccessToken (conn: SqliteConnection) (token: Audible.AudibleAccessToken) : unit =
        SettingsStore.setSetting conn "audible_access_token" token.AccessToken
        SettingsStore.setSetting conn "audible_access_token_expires" (token.ExpiresAt.ToString("o"))

    /// Wires `Audible.withAccessToken` to a stored `Audible.AudibleConfig`
    /// and the given `fetch`. `Error "Audible is not configured..."` when no
    /// auth file has been saved yet -- never attempted, since there is no
    /// refresh token to mint from (ADR-0074 point 1: no code path here can
    /// register a device or log in to obtain one).
    let private withAudibleAccessToken
        (httpClient: HttpClient)
        (conn: SqliteConnection)
        (config: Audible.AudibleConfig)
        (fetch: string -> Async<Result<'a, Audible.FetchError>>)
        : Async<Result<'a, string>> =
        match config.AuthFile with
        | None -> async { return Error "Audible is not configured — paste an auth file in Settings" }
        | Some authFile ->
            let cached =
                match config.CachedAccessToken, config.CachedAccessTokenExpiresAt with
                | Some token, Some expiresAt -> Some (token, expiresAt)
                | _ -> None
            Audible.withAccessToken
                cached
                (fun () -> Audible.refreshAccessToken httpClient authFile)
                (persistAudibleAccessToken conn)
                fetch

    /// integration-dhctm (ADR-0074): fetches the Audible product (falling
    /// back to Audnexus when the product lacks a description) and turns it
    /// into an `AddBookRequest`, then reuses `addBookToLibraryImpl`. On
    /// `Book_added`, writes the cache slice (`MetadataCache.upsertBookMetadata`,
    /// `source = "audible"`) — never the identity card (ADR-0043's
    /// identity-card clause). The cover is fetched via
    /// `addBookToLibraryImpl`'s own generic `CoverUrl` path (`coverDownloader
    /// = None`) -- Audible's catalog CDN needs no adapter-owned throttle or
    /// User-Agent the way Open Library's covers host does.
    let private addBookFromAudibleImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getAudibleConfig: unit -> Audible.AudibleConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (request: AddBookFromAudibleRequest)
        : Async<Result<AddBookOutcome, string>> = async {
            try
                let config = getAudibleConfig ()
                let locale = config.Marketplace
                let host = Audible.marketplaceHost locale
                let! productOpt = Audible.getProduct httpClient host request.Asin
                match productOpt with
                | None -> return Error (sprintf "Audible product %s not found" request.Asin)
                | Some product ->
                    let! audnexusOpt =
                        if Option.isNone product.Description then Audnexus.getBook httpClient request.Asin locale
                        else async { return None }

                    let description = product.Description |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.Description))
                    let narrators =
                        if not (List.isEmpty product.Narrators) then product.Narrators
                        else audnexusOpt |> Option.map (fun a -> a.Narrators) |> Option.defaultValue []
                    let seriesName = product.SeriesName |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.SeriesName))
                    let seriesPosition = product.SeriesPosition |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.SeriesPosition))

                    let year =
                        product.ReleaseDate
                        |> Option.bind (fun d ->
                            if d.Length >= 4 then
                                match System.Int32.TryParse(d.Substring(0, 4)) with
                                | true, y -> Some y
                                | _ -> None
                            else None)

                    let addRequest : AddBookRequest = {
                        Title = product.Title
                        Authors = product.Authors
                        Year = year
                        CoverUrl = product.CoverUrl
                        Subjects = product.Categories
                        Format = BookFormat.Audiobook
                        ExternalIds = [ AudibleAsin request.Asin ]
                        SkipDuplicateCheck = request.SkipDuplicateCheck
                    }

                    let! result = addBookToLibraryImpl conn httpClient imageBasePath projectionHandlers addRequest None
                    match result with
                    | Ok (AddBookOutcome.Book_added slug) ->
                        let metadata : MetadataCache.BookMetadata = {
                            Description = description
                            PageCount = None
                            RuntimeMinutes = product.RuntimeMinutes
                            Narrators = narrators
                            SeriesName = seriesName
                            SeriesPosition = seriesPosition
                            Publisher = product.Publisher
                            PublishedDate = product.ReleaseDate
                            AverageRating = product.Rating
                            Language = product.Language
                            Source = Some "audible"
                        }
                        MetadataCache.upsertBookMetadata conn slug metadata
                        return Ok (AddBookOutcome.Book_added slug)
                    | other -> return other
            with ex ->
                return Error $"Failed to add book from Audible: {ex.Message}"
        }

    /// integration-jjvg2: `executeCommandCore` only reports `Result<unit,
    /// string>`, but the import needs to know whether the
    /// `Observe_reading_progress` command it issues actually appended an
    /// event (a same-percent-per-source observation is a legitimate no-op,
    /// ADR-0076 §2, and must not inflate `ProgressObserved`). Same shape as
    /// `AudibleSync.fs`'s own local copy.
    let private executeBookCommandWithEvents
        (conn: SqliteConnection)
        (slug: string)
        (command: Books.BookCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<Books.BookEvent list, string> =
        let streamId = Books.streamId slug
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose Books.Serialization.fromStoredEvent
        let state = Books.reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId
        match Books.decide state command with
        | Error e -> Error e
        | Ok [] -> Ok []
        | Ok newEvents ->
            let eventDataList = newEvents |> List.map Books.Serialization.toEventData
            match EventStore.appendToStream conn streamId currentPosition eventDataList with
            | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
            | EventStore.Success _ ->
                for handler in projectionHandlers do
                    Projection.runProjection conn handler
                Ok newEvents

    /// integration-jjvg2 (ADR-0074/ADR-0076): the one-time "Import library"
    /// click. Known items (matched by ASIN via `BookProjection.findByExternalId`)
    /// are never re-created; new items are created directly from the library
    /// response's OWN fields (no per-title `getProduct` call -- ADR-0069's
    /// "diff, don't re-enrich"), Audnexus only filling description/narrators
    /// when the library item itself lacks them. Every item (known or new)
    /// with a percent then funnels through `AudibleSync.observationFor` --
    /// the SAME pure decision the daily sync uses, so import and sync are
    /// one writer of `Reading_progress_observed`.
    let private importAudibleLibraryImpl
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getAudibleConfig: unit -> Audible.AudibleConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<AudibleImportResult, string>> = async {
            let config = getAudibleConfig ()
            match config.AuthFile with
            | None -> return Error "Audible is not configured — paste an auth file in Settings"
            | Some authFile ->
                let host = Audible.marketplaceHost authFile.LocaleCode
                let cached =
                    match config.CachedAccessToken, config.CachedAccessTokenExpiresAt with
                    | Some t, Some e -> Some (t, e)
                    | _ -> None
                // integration-dtdbb (ADR-0074): the token actually used to
                // authenticate the (possibly retried-after-401) library
                // fetch, captured as a side effect so the per-book
                // `getLastPositionHeard` calls below reuse it directly
                // rather than re-running `withAccessToken`'s whole
                // cache/refresh/retry orchestration for every title.
                let mutable currentAccessToken : string option = None
                let! libraryResult =
                    Audible.withAccessToken
                        cached
                        (fun () -> Audible.refreshAccessToken httpClient authFile)
                        (persistAudibleAccessToken conn)
                        (fun token ->
                            currentAccessToken <- Some token
                            Audible.getLibrary httpClient host token)
                match libraryResult with
                | Error msg ->
                    if msg.StartsWith(Audible.authFileRejectedPrefix) then
                        SettingsStore.setSetting conn "audible_last_error" msg
                    return Error msg
                | Ok items ->
                    // Local calendar date, matching AudibleSync.runProgressSync
                    // (the task's own Notes: "keep the plain local calendar
                    // date of the sync run") -- an import and a same-day sync
                    // must stamp the same ObservedOn, not one in UTC and one
                    // in local time.
                    let today = System.DateTime.Now.ToString("yyyy-MM-dd")
                    let mutable created = 0
                    let mutable alreadyKnown = 0
                    let mutable progressObserved = 0
                    let mutable priorsFromAudible = 0
                    let mutable priorsToday = 0
                    let mutable repaired = 0
                    let mutable errors : string list = []

                    // integration-dtdbb, ADR-0082 §2/§5: `None` on a failed
                    // round trip (network/401/decode) degrades to
                    // both-`None` exactly like `Audible.getLastPositionHeard`
                    // itself does for a "DoesNotExist"/undecodable body --
                    // the caller never distinguishes the two, and the prior
                    // still gets recorded, just dated to `today`.
                    let fetchLastPositionHeard (asin: string) : Async<Audible.LastPositionHeard option> =
                        async {
                            match currentAccessToken with
                            | None -> return None
                            | Some token ->
                                match! Audible.getLastPositionHeard httpClient host token asin with
                                | Ok v -> return Some v
                                | Error _ -> return None
                        }

                    let observe (slug: string) (item: Audible.AudibleLibraryItem) : Async<unit> =
                        async {
                            // Legacy repair (ADR-0082 §7): a book whose ONLY
                            // Audible row predates priors -- exactly one row,
                            // `kind = 'observation'` -- is removed so the
                            // fall-through below records a correctly-dated
                            // prior in its place. Idempotent: once repaired,
                            // the book carries a `prior` row and this finds
                            // nothing on the next run.
                            match BookProjection.legacyObservationToRepair conn slug ProgressSource.Audible with
                            | Some observedOn ->
                                match executeBookCommandWithEvents conn slug (Books.Remove_reading_progress_observation (observedOn, ProgressSource.Audible)) projectionHandlers with
                                | Ok events when not (List.isEmpty events) -> repaired <- repaired + 1
                                | Ok _ -> ()
                                | Error e -> errors <- errors @ [ sprintf "%s (%s): repair failed: %s" item.Title item.Asin e ]
                            | None -> ()

                            if BookProjection.hasSourceProgress conn slug ProgressSource.Audible then
                                match AudibleSync.observationFor item today None with
                                | None -> ()
                                | Some data ->
                                    match executeBookCommandWithEvents conn slug (Books.Observe_reading_progress data) projectionHandlers with
                                    | Ok events when not (List.isEmpty events) -> progressObserved <- progressObserved + 1
                                    | Ok _ -> ()
                                    | Error e -> errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                            else
                                let! lastPositionOpt = fetchLastPositionHeard item.Asin
                                match AudibleSync.observationFor item today lastPositionOpt with
                                | None -> ()
                                | Some data ->
                                    match executeBookCommandWithEvents conn slug (Books.Record_prior_reading_progress data) projectionHandlers with
                                    | Ok events when not (List.isEmpty events) ->
                                        progressObserved <- progressObserved + 1
                                        if lastPositionOpt |> Option.bind (fun l -> l.LastUpdatedOn) |> Option.isSome then
                                            priorsFromAudible <- priorsFromAudible + 1
                                        else
                                            priorsToday <- priorsToday + 1
                                        // integration-dtdbb (verifier iteration 1): `Books.decide`'s
                                        // `Record_prior_reading_progress` branch only emits
                                        // `Book_status_changed` when the book isn't ALREADY
                                        // Finished (it behaves like a no-op re-finish otherwise) --
                                        // so a legacy repair on an already-Finished book (the
                                        // dominant real-world repair case) recorded a correctly-dated
                                        // prior but left `finished_at` stamped at the OLD
                                        // (e.g. import-day) date. Re-dating an already-Finished book
                                        // is a legitimate event (ADR-0077 §4); `Change_status` is a
                                        // no-op only when the effective date is unchanged
                                        // (`statusChangeIsNoOp`), so issuing it unconditionally here
                                        // is harmless when `decide` already set the right date (the
                                        // fresh-book path, where the book starts non-Finished) and
                                        // corrects it when it didn't (the repair path, where the
                                        // book was already Finished before the repair ran).
                                        if data.Percent = 100 || data.Finished then
                                            match executeBookCommandWithEvents conn slug (Books.Change_status (BookStatus.Finished, Some data.ObservedOn)) projectionHandlers with
                                            | Ok _ -> ()
                                            | Error e -> errors <- errors @ [ sprintf "%s (%s): re-finish failed: %s" item.Title item.Asin e ]
                                    | Ok _ -> ()
                                    | Error e -> errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                        }

                    for item in items do
                        try
                            match BookProjection.findByExternalId conn (AudibleAsin item.Asin) with
                            | Some slug ->
                                alreadyKnown <- alreadyKnown + 1
                                do! observe slug item
                            | None ->
                                let year =
                                    item.ReleaseDate
                                    |> Option.bind (fun d ->
                                        if d.Length >= 4 then
                                            match System.Int32.TryParse(d.Substring(0, 4)) with
                                            | true, y -> Some y
                                            | _ -> None
                                        else None)
                                let addRequest : AddBookRequest = {
                                    Title = item.Title
                                    Authors = item.Authors
                                    Year = year
                                    CoverUrl = item.CoverUrl
                                    Subjects = []
                                    Format = BookFormat.Audiobook
                                    ExternalIds = [ AudibleAsin item.Asin ]
                                    SkipDuplicateCheck = true
                                }
                                let! addResult = addBookToLibraryImpl conn httpClient imageBasePath projectionHandlers addRequest None
                                match addResult with
                                | Ok (AddBookOutcome.Book_added slug) ->
                                    created <- created + 1
                                    let! audnexusOpt =
                                        if Option.isNone item.Description || List.isEmpty item.Narrators then
                                            Audnexus.getBook httpClient item.Asin authFile.LocaleCode
                                        else async { return None }
                                    let metadata : MetadataCache.BookMetadata = {
                                        Description = item.Description |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.Description))
                                        PageCount = None
                                        RuntimeMinutes = item.RuntimeMinutes
                                        Narrators =
                                            if not (List.isEmpty item.Narrators) then item.Narrators
                                            else audnexusOpt |> Option.map (fun a -> a.Narrators) |> Option.defaultValue []
                                        SeriesName = item.SeriesName |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.SeriesName))
                                        SeriesPosition = item.SeriesPosition |> Option.orElse (audnexusOpt |> Option.bind (fun a -> a.SeriesPosition))
                                        Publisher = None
                                        PublishedDate = item.ReleaseDate
                                        AverageRating = None
                                        Language = None
                                        Source = Some "audible"
                                    }
                                    MetadataCache.upsertBookMetadata conn slug metadata
                                    do! observe slug item
                                | Ok (AddBookOutcome.Duplicate_found (existingSlug, _)) ->
                                    alreadyKnown <- alreadyKnown + 1
                                    do! observe existingSlug item
                                | Error e -> errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                        with ex ->
                            errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin ex.Message ]

                    let result = {
                        Total = List.length items
                        Created = created
                        AlreadyKnown = alreadyKnown
                        ProgressObserved = progressObserved
                        PriorsFromAudible = priorsFromAudible
                        PriorsToday = priorsToday
                        Repaired = repaired
                        Errors = errors
                    }
                    SettingsStore.setSetting conn "audible_last_import_result" (AudibleSync.formatImportResult result)
                    // integration-k4vqm's lesson (ADR-0068), bound by this
                    // task's own Notes: an empty-but-200 library response is
                    // inconclusive, not evidence the auth file is fine again
                    // -- only a genuinely populated response clears a
                    // standing `audible_last_error` notice (mirrors
                    // Api.fs's Steam `Ok []` vs `Ok games` split above).
                    if not (List.isEmpty items) then
                        SettingsStore.deleteSetting conn "audible_last_error"
                    return Ok result
        }

    let create
        (factory: unit -> SqliteConnection)
        (httpClient: HttpClient)
        (qbittorrentHttpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (getJellyfinConfig: unit -> Jellyfin.JellyfinConfig)
        (getQbittorrentConfig: unit -> Qbittorrent.QbittorrentConfig)
        (getOpenLibraryConfig: unit -> OpenLibrary.OpenLibraryConfig)
        (getAudibleConfig: unit -> Audible.AudibleConfig)
        // integration-jjvg2 (ADR-0026, ADR-0078): built in Composition.fs as a
        // wrapper `JobSpec` closing over the SAME `ScheduledJobs.JobRunRecorder`/
        // job connection/lock the "Audible progress sync" `JobSpec` and the
        // Jobs tab's generic "Run now" share -- so the Settings card's own
        // "Sync progress now" click is ALSO recorded as a `job_runs` row
        // (trigger = "manual") and guarded against overlapping the nightly
        // fire, rather than a bespoke un-recorded trigger
        // (`triggerPlaytimeSync`'s older shape, predating ADR-0026).
        (runAudibleProgressSyncNow: unit -> Async<Result<AudibleProgressSyncResult, string>>)
        (mountRoots: LocalCopyRemoval.MountRoots)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : IMediathecaApi =

        let movieProjections = projectionHandlers
        let friendProjections = projectionHandlers

        // administration-mz6kp (ADR-0033): shadow the module-private
        // `executeCommandCore` so every existing `executeCommand conn sid ...`
        // call site below is unchanged. Written as a full eta-expansion (not
        // a bare partial application) because F#'s value restriction would
        // otherwise collapse this generic function to whichever bounded
        // context's event/state/command types its first call site
        // instantiates, breaking every other bounded context's calls below.
        let executeCommand conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers =
            executeCommandCore conn streamId fromStoredEvent reconstitute decide toEventData command projectionHandlers

        // administration-tj8n2: PlaytimeTracker.runSync takes a per-command
        // lock (guarding its own connection against a concurrent scheduled
        // fire on the JOB connection). `triggerPlaytimeSync` here opens its
        // own per-request connection via `factory` (administration-mz6kp,
        // ADR-0033) — a different connection object from both the job
        // connection and any other request's connection, so this lock is
        // never contended by the scheduled job. It exists solely to satisfy
        // runSync's signature and to keep two overlapping manual triggers
        // from racing each other on the same manually-triggered sync's
        // in-memory bookkeeping; out of scope here (administration-cx92m).
        let manualSyncTriggerLock = new System.Threading.SemaphoreSlim(1, 1)

        {
            healthCheck = fun () -> async { return "Mediatheca is running" }

            searchTmdb = fun (query, year) -> async {
                return! Tmdb.searchMovies httpClient (getTmdbConfig()) query year
            }

            addMovie = fun tmdbId -> async {
                use conn = factory ()
                return! addMovieToLibrary conn httpClient getTmdbConfig imageBasePath movieProjections tmdbId
            }

            removeMovie = fun slug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        Movies.Remove_movie_from_library
                        movieProjections
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this movie
                    let catalogEntries = CatalogProjection.getEntriesByMediaSlug conn Mediatheca.Shared.MediaType.Movie slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up cast and images
                    CastStore.removeMovieCastAndCleanup conn imageBasePath sid
                    ImageStore.deleteImage imageBasePath (sprintf "posters/%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/%s.jpg" slug)
                    // curation-h4k2p: clear the Notes document and its uploaded content images
                    clearNotesOnRemoval conn imageBasePath movieProjections Mediatheca.Shared.MediaType.Movie slug
                    return Ok ()
                | Error e -> return Error e
            }

            getMovie = fun slug -> async {
                use conn = factory ()
                return MovieProjection.getBySlug conn slug
            }

            getMovies = fun () -> async {
                use conn = factory ()
                return MovieProjection.getAll conn
            }

            categorizeMovie = fun slug genres -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Categorize_movie genres)
                        movieProjections
            }

            replacePoster = fun slug posterRef -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Replace_poster posterRef)
                        movieProjections
            }

            replaceBackdrop = fun slug backdropRef -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Replace_backdrop backdropRef)
                        movieProjections
            }

            recommendMovie = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Recommend_by friendSlug)
                        movieProjections
            }

            removeRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_recommendation friendSlug)
                        movieProjections
            }

            wantToWatchWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_want_to_watch_with friendSlug)
                        movieProjections
            }

            removeWantToWatchWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_from_want_to_watch_with friendSlug)
                        movieProjections
            }

            setPersonalRating = fun slug rating -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Set_personal_rating rating)
                        movieProjections
            }

            setMovieInFocus = fun slug inFocus -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                let command = if inFocus then Movies.Set_movie_in_focus else Movies.Clear_movie_in_focus
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        command
                        movieProjections
            }

            // Watch Sessions
            recordWatchSession = fun slug request -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                let runtime =
                    conn
                    |> Db.newCommand "SELECT runtime FROM movie_detail WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.querySingle (fun (rd: IDataReader) ->
                        if rd.IsDBNull(rd.GetOrdinal("runtime")) then None
                        else Some (rd.ReadInt32 "runtime"))
                    |> Option.flatten
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: Movies.WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = request.Date
                    Duration = runtime
                    FriendSlugs = request.FriendSlugs
                }
                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Record_watch_session sessionData)
                        movieProjections
                match result with
                | Ok () -> return Ok sessionId
                | Error e -> return Error e
            }

            updateWatchSessionDate = fun slug sessionId date -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Change_watch_session_date (sessionId, date))
                        movieProjections
            }

            addFriendToWatchSession = fun slug sessionId friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_friend_to_watch_session (sessionId, friendSlug))
                        movieProjections
            }

            removeFriendFromWatchSession = fun slug sessionId friendSlug -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_friend_from_watch_session (sessionId, friendSlug))
                        movieProjections
            }

            removeWatchSession = fun slug sessionId -> async {
                use conn = factory ()
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_watch_session sessionId)
                        movieProjections
            }

            getWatchSessions = fun slug -> async {
                use conn = factory ()
                return MovieProjection.getWatchSessions conn slug
            }

            uploadContentImage = fun data filename -> async {
                try
                    let ext = System.IO.Path.GetExtension(filename).ToLowerInvariant()
                    let imageId = System.Guid.NewGuid().ToString("N")
                    let ref = sprintf "content/%s%s" imageId ext
                    let destPath = System.IO.Path.Combine(imageBasePath, ref)
                    let dir = System.IO.Path.GetDirectoryName(destPath)
                    if not (System.IO.Directory.Exists(dir)) then
                        System.IO.Directory.CreateDirectory(dir) |> ignore
                    System.IO.File.WriteAllBytes(destPath, data)
                    return Ok ref
                with ex ->
                    return Error $"Failed to upload image: {ex.Message}"
            }

            // Notes (curation-h98ve, ADR-0080): event-sourced document per
            // (MediaType, slug); no bespoke write path, same executeCommand
            // idiom every other command uses.
            getNotes = fun mediaType slug -> async {
                use conn = factory ()
                return NotesProjection.getForOwner conn mediaType slug
            }

            saveNotes = fun mediaType slug blocks -> async {
                use conn = factory ()
                let sid = Notes.streamId mediaType slug
                return
                    executeCommand
                        conn sid
                        Notes.Serialization.fromStoredEvent
                        Notes.reconstitute
                        Notes.decide
                        Notes.Serialization.toEventData
                        (Notes.Save_notes blocks)
                        projectionHandlers
            }

            // Catalogs
            createCatalog = fun request -> async {
                use conn = factory ()
                let baseSlug = Slug.catalogSlug request.Name
                let slug = generateUniqueSlug conn Catalogs.streamId baseSlug
                let sid = Catalogs.streamId slug
                let data: Catalogs.CatalogCreatedData = {
                    Name = request.Name
                    Description = request.Description
                    IsSorted = request.IsSorted
                }
                let result =
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Create_catalog data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok slug
                | Error e -> return Error e
            }

            updateCatalog = fun slug request -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                let data: Catalogs.CatalogUpdatedData = {
                    Name = request.Name
                    Description = request.Description
                }
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Update_catalog data)
                        projectionHandlers
            }

            removeCatalog = fun slug -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        Catalogs.Remove_catalog
                        projectionHandlers
            }

            getCatalog = fun slug -> async {
                use conn = factory ()
                return CatalogProjection.getBySlug conn slug
            }

            getCatalogs = fun () -> async {
                use conn = factory ()
                return CatalogProjection.getAll conn
            }

            addCatalogEntry = fun slug request -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                let entryId = System.Guid.NewGuid().ToString("N")
                let data: Catalogs.EntryAddedData = {
                    EntryId = entryId
                    MovieSlug = request.MediaSlug
                    Note = request.Note
                    MediaType = Some request.MediaType
                }
                let result =
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Add_entry data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok entryId
                | Error e -> return Error e
            }

            updateCatalogEntry = fun slug entryId request -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                let data: Catalogs.EntryUpdatedData = {
                    EntryId = entryId
                    Note = request.Note
                }
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Update_entry data)
                        projectionHandlers
            }

            removeCatalogEntry = fun slug entryId -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Remove_entry entryId)
                        projectionHandlers
            }

            reorderCatalogEntries = fun slug entryIds -> async {
                use conn = factory ()
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Reorder_entries entryIds)
                        projectionHandlers
            }

            getCatalogsForMovie = fun movieSlug -> async {
                use conn = factory ()
                return CatalogProjection.getCatalogsForMedia conn Mediatheca.Shared.MediaType.Movie movieSlug
            }

            // Dashboard
            getDashboardStats = fun () -> async {
                use conn = factory ()
                let movieCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let seriesCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let gameCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let friendCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM friend_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let catalogCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM catalog_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let watchSessionCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(md.runtime), 0) as total FROM watch_sessions ws JOIN movie_detail md ON ws.movie_slug = md.slug"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let seriesWatchTime =
                    conn
                    |> Db.newCommand """
                        SELECT COALESCE(SUM(e.runtime), 0) as total
                        FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress) p
                        JOIN series_episode_cache e ON e.series_slug = p.series_slug AND e.season_number = p.season_number AND e.episode_number = p.episode_number
                    """
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let totalPlayTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(total_play_time), 0) as total FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                return {
                    Mediatheca.Shared.DashboardStats.MovieCount = movieCount
                    SeriesCount = seriesCount
                    GameCount = gameCount
                    FriendCount = friendCount
                    CatalogCount = catalogCount
                    WatchSessionCount = watchSessionCount
                    TotalWatchTimeMinutes = totalWatchTime
                    SeriesWatchTimeMinutes = seriesWatchTime
                    TotalPlayTimeMinutes = totalPlayTime
                }
            }

            getRecentSeries = fun count -> async {
                use conn = factory ()
                return SeriesProjection.getRecentSeries conn count
            }

            // Dashboard Tabs
            getDashboardAllTab = fun () -> async {
                use conn = factory ()
                let seriesNextUp = SeriesProjection.getDashboardSeriesNextUp conn (Some 11)
                let moviesToWatch = MovieProjection.getAllTabMoviesToWatch conn
                let gamesInFocus = GameProjection.getGamesInFocus conn
                let gamesRecentlyPlayed = GameProjection.getGamesRecentlyPlayed conn (Some 6)
                let playSessions = PlaytimeTracker.getDashboardPlaySessions conn 14
                let currentlyReading = BookProjection.getAllTabCurrentlyReading conn
                let jellyfinServerUrl = SettingsStore.getSetting conn "jellyfin_server_url"

                return {
                    Mediatheca.Shared.DashboardAllTab.SeriesNextUp = seriesNextUp
                    MoviesToWatch = moviesToWatch
                    GamesInFocus = gamesInFocus
                    GamesRecentlyPlayed = gamesRecentlyPlayed
                    PlaySessions = playSessions
                    JellyfinServerUrl =
                        jellyfinServerUrl
                        |> Option.bind (fun s -> if System.String.IsNullOrWhiteSpace(s) then None else Some s)
                    CurrentlyReading = currentlyReading
                }
            }

            getDashboardMoviesTab = fun () -> async {
                use conn = factory ()
                let recentlyAdded = MovieProjection.getRecentlyAddedMovies conn (Some 10)
                let totalMovies =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchSessions =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(md.runtime), 0) as total FROM watch_sessions ws JOIN movie_detail md ON ws.movie_slug = md.slug"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let averageRating = MovieProjection.getAverageRating conn
                let watchlistCount = MovieProjection.getWatchlistCount conn
                let ratingDistribution = MovieProjection.getRatingDistribution conn
                let genreDistribution = MovieProjection.getGenreDistribution conn
                let recentlyWatched = MovieProjection.getRecentlyWatched conn (Some 10)
                let monthlyActivity = MovieProjection.getMonthlyActivity conn
                let topActors = MovieProjection.getTopActors conn (Some 5)
                let topDirectors = MovieProjection.getTopDirectors conn (Some 5)
                let topWatchedWith = MovieProjection.getTopWatchedWith conn (Some 5)
                let countryDistribution = MovieProjection.getCountryDistribution conn
                let moviesToWatch = MovieProjection.getMoviesToWatch conn
                let jellyfinServerUrl = SettingsStore.getSetting conn "jellyfin_server_url"
                return {
                    Mediatheca.Shared.DashboardMoviesTab.RecentlyAdded = recentlyAdded
                    RecentlyWatched = recentlyWatched
                    MoviesToWatch = moviesToWatch
                    JellyfinServerUrl =
                        jellyfinServerUrl
                        |> Option.bind (fun s -> if System.String.IsNullOrWhiteSpace(s) then None else Some s)
                    TopActors = topActors
                    TopDirectors = topDirectors
                    TopWatchedWith = topWatchedWith
                    Stats = {
                        Mediatheca.Shared.DashboardMovieStats.TotalMovies = totalMovies
                        TotalWatchSessions = totalWatchSessions
                        TotalWatchTimeMinutes = totalWatchTime
                        AverageRating = averageRating
                        WatchlistCount = watchlistCount
                        RatingDistribution = ratingDistribution
                        GenreDistribution = genreDistribution
                        MonthlyActivity = monthlyActivity
                        CountryDistribution = countryDistribution
                    }
                }
            }

            getDashboardSeriesTab = fun () -> async {
                use conn = factory ()
                let nextUp = SeriesProjection.getDashboardSeriesNextUp conn None
                let recentlyFinished = SeriesProjection.getRecentlyFinished conn (Some 10)
                let recentlyAbandoned = SeriesProjection.getRecentlyAbandoned conn (Some 10)
                let totalSeries =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalEpisodesWatched =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress)"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand """
                        SELECT COALESCE(SUM(e.runtime), 0) as total
                        FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress) p
                        JOIN series_episode_cache e ON e.series_slug = p.series_slug AND e.season_number = p.season_number AND e.episode_number = p.episode_number
                    """
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let currentlyWatching = SeriesProjection.getCurrentlyWatchingCount conn
                let averageRating = SeriesProjection.getAverageSeriesRating conn
                let completionRate = SeriesProjection.getCompletionRate conn
                let ratingDistribution = SeriesProjection.getSeriesRatingDistribution conn
                let genreDistribution = SeriesProjection.getSeriesGenreDistribution conn
                let monthlyActivity = SeriesProjection.getMonthlyEpisodeActivity conn
                let episodeActivity = SeriesProjection.getEpisodeActivity conn
                let topWatchedWith = SeriesProjection.getSeriesTopWatchedWith conn (Some 5)
                let returningSoon = SeriesProjection.getReturningSoon conn (Some 5)
                let jellyfinServerUrl = SettingsStore.getSetting conn "jellyfin_server_url"
                return {
                    Mediatheca.Shared.DashboardSeriesTab.NextUp = nextUp
                    RecentlyFinished = recentlyFinished
                    RecentlyAbandoned = recentlyAbandoned
                    EpisodeActivity = episodeActivity
                    TopWatchedWith = topWatchedWith
                    ReturningSoon = returningSoon
                    JellyfinServerUrl =
                        jellyfinServerUrl
                        |> Option.bind (fun s -> if System.String.IsNullOrWhiteSpace(s) then None else Some s)
                    Stats = {
                        Mediatheca.Shared.DashboardSeriesStats.TotalSeries = totalSeries
                        TotalEpisodesWatched = totalEpisodesWatched
                        TotalWatchTimeMinutes = totalWatchTime
                        CurrentlyWatching = currentlyWatching
                        AverageRating = averageRating
                        CompletionRate = completionRate
                        RatingDistribution = ratingDistribution
                        GenreDistribution = genreDistribution
                        MonthlyActivity = monthlyActivity
                    }
                }
            }

            getDashboardGamesTab = fun () -> async {
                use conn = factory ()
                let recentlyAdded = GameProjection.getRecentlyAddedGames conn (Some 10)
                let recentlyPlayed = GameProjection.getGamesRecentlyPlayed conn (Some 10)
                let totalGames =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalPlayTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(total_play_time), 0) as total FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let gamesCompleted =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Retired'"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let gamesInProgress =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'InFocus'"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let backlogSize =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Backlog'"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let completionRate = GameProjection.getGameCompletionRate conn
                let averageRating = GameProjection.getAverageGameRating conn
                let (backlogTimeHours, backlogGameCount, backlogGamesWithoutHltb) = GameProjection.getBacklogStats conn
                let statusDistribution = GameProjection.getGameStatusDistribution conn
                let ratingDistribution = GameProjection.getGameRatingDistribution conn
                let genreDistribution = GameProjection.getGameGenreDistribution conn
                let monthlyPlayTime = GameProjection.getMonthlyPlayTime conn
                let completedPerYear = GameProjection.getGamesCompletedPerYear conn
                let hltbComparisons = GameProjection.getHltbComparisons conn
                let inFocusEstimate = GameProjection.getInFocusEstimate conn
                let monthlyPlayTimePerGame = GameProjection.getMonthlyPlayTimePerGame conn
                return {
                    Mediatheca.Shared.DashboardGamesTab.RecentlyAdded = recentlyAdded
                    RecentlyPlayed = recentlyPlayed
                    HltbComparisons = hltbComparisons
                    InFocusEstimate = inFocusEstimate
                    MonthlyPlayTimePerGame = monthlyPlayTimePerGame
                    Upcoming = GameProjection.getUpcomingGames conn
                    Stats = {
                        Mediatheca.Shared.DashboardGameStats.TotalGames = totalGames
                        TotalPlayTimeMinutes = totalPlayTime
                        GamesCompleted = gamesCompleted
                        GamesInProgress = gamesInProgress
                        BacklogSize = backlogSize
                        CompletionRate = completionRate
                        AverageRating = averageRating
                        BacklogTimeHours = backlogTimeHours
                        BacklogGameCount = backlogGameCount
                        BacklogGamesWithoutHltb = backlogGamesWithoutHltb
                        StatusDistribution = statusDistribution
                        RatingDistribution = ratingDistribution
                        GenreDistribution = genreDistribution
                        MonthlyPlayTime = monthlyPlayTime
                        CompletedPerYear = completedPerYear
                    }
                }
            }

            getDashboardBooksTab = fun () -> async {
                use conn = factory ()
                let currentlyReading =
                    BookProjection.getCurrentlyReading conn
                    |> List.map (BookProjection.toDashboardBookItem false)
                let recentlyFinished =
                    BookProjection.getRecentlyFinished conn 90
                    |> List.map (BookProjection.toDashboardBookItem true)
                let recentlyAdded =
                    BookProjection.getRecentlyAddedUnfinished conn (Some 10)
                    |> List.map (BookProjection.toDashboardBookItem false)
                let stats = BookProjection.getReadingStats conn
                return {
                    Mediatheca.Shared.DashboardBooksTab.CurrentlyReading = currentlyReading
                    RecentlyFinished = recentlyFinished
                    RecentlyAdded = recentlyAdded
                    Stats = stats
                }
            }

            // Dashboard card expansion: the card's query with its row limit
            // lifted. Each tab query above passes `Some n`; this passes `None`.
            getDashboardCardItems = fun query -> async {
                use conn = factory ()
                match query with
                | SeriesNextUpQuery ->
                    return SeriesNextUpItems (SeriesProjection.getDashboardSeriesNextUp conn None)
                | MoviesToWatchQuery ->
                    return MoviesToWatchItems (MovieProjection.getMoviesToWatch conn)
                | AllMoviesToWatchQuery ->
                    return MoviesToWatchItems (MovieProjection.getAllTabMoviesToWatch conn)
                | GamesInFocusQuery ->
                    return GamesInFocusItems (GameProjection.getGamesInFocus conn)
                | MoviesRecentlyWatchedQuery ->
                    return RecentlyWatchedMovieItems (MovieProjection.getRecentlyWatched conn None)
                | MoviesRecentlyAddedQuery ->
                    return MovieItems (MovieProjection.getRecentlyAddedMovies conn None)
                | MoviesTopActorsQuery ->
                    return PersonItems (MovieProjection.getTopActors conn None)
                | MoviesTopDirectorsQuery ->
                    return PersonItems (MovieProjection.getTopDirectors conn None)
                | MoviesTopWatchedWithQuery ->
                    return MovieWatchedWithItems (MovieProjection.getTopWatchedWith conn None)
                | SeriesReturningSoonQuery ->
                    return ReturningSoonItems (SeriesProjection.getReturningSoon conn None)
                | SeriesRecentlyFinishedQuery ->
                    return SeriesItems (SeriesProjection.getRecentlyFinished conn None)
                | SeriesRecentlyAbandonedQuery ->
                    return SeriesItems (SeriesProjection.getRecentlyAbandoned conn None)
                | SeriesTopWatchedWithQuery ->
                    return SeriesWatchedWithItems (SeriesProjection.getSeriesTopWatchedWith conn None)
                | GamesRecentlyPlayedQuery ->
                    return RecentlyPlayedGameItems (GameProjection.getGamesRecentlyPlayed conn None)
                | GamesRecentlyAddedQuery ->
                    return GameItems (GameProjection.getRecentlyAddedGames conn None)
                | GamesUpcomingQuery ->
                    return GameItems (GameProjection.getUpcomingGames conn)
                | SteamRecentAchievementsQuery ->
                    // Steam's cache keeps only the ten most recent unlocks, so
                    // expanding this card shows the same ten the card already
                    // shows. Lifting that cap is a Steam-side change.
                    match! Steam.getRecentAchievements httpClient (getSteamConfig ()) with
                    | Ok achievements -> return AchievementItems achievements
                    | Error message -> return failwith message
                | AllCurrentlyReading ->
                    return BookReadingItems (BookProjection.getAllTabCurrentlyReading conn)
                | BooksCurrentlyReading ->
                    return BookReadingItems (BookProjection.getCurrentlyReading conn |> List.map (BookProjection.toDashboardBookItem false))
                | BooksRecentlyFinished ->
                    return BookReadingItems (BookProjection.getRecentlyFinished conn 90 |> List.map (BookProjection.toDashboardBookItem true))
                | BooksRecentlyAdded ->
                    return BookReadingItems (BookProjection.getRecentlyAddedUnfinished conn None |> List.map (BookProjection.toDashboardBookItem false))
            }

            addFriend = fun name -> async {
                use conn = factory ()
                let slug = Slug.friendSlug name
                let sid = Friends.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        (Friends.Add_friend (name, None))
                        friendProjections
                match result with
                | Ok () -> return Ok slug
                | Error e -> return Error e
            }

            updateFriend = fun slug name imageRef -> async {
                use conn = factory ()
                let sid = Friends.streamId slug
                return
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        (Friends.Update_friend (name, imageRef))
                        friendProjections
            }

            removeFriend = fun slug -> async {
                use conn = factory ()
                let sid = Friends.streamId slug
                let imageRef = FriendProjection.getBySlug conn slug |> Option.bind (fun f -> f.ImageRef)
                let result =
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        Friends.Remove_friend
                        friendProjections
                match result with
                | Ok () ->
                    imageRef |> Option.iter (fun ref -> ImageStore.deleteImage imageBasePath ref)
                    return Ok ()
                | Error e -> return Error e
            }

            getFriend = fun slug -> async {
                use conn = factory ()
                return FriendProjection.getBySlug conn slug
            }

            getFriendMedia = fun friendSlug -> async {
                use conn = factory ()
                let movieRec = MovieProjection.getMoviesRecommendedByFriend conn friendSlug
                let seriesRec = SeriesProjection.getSeriesRecommendedByFriend conn friendSlug
                let gameRec = GameProjection.getGamesRecommendedByFriend conn friendSlug
                let movieWant = MovieProjection.getMoviesWantToWatchWithFriend conn friendSlug
                let seriesWant = SeriesProjection.getSeriesWantToWatchWithFriend conn friendSlug
                let gameWant = GameProjection.getGamesWantToPlayWithFriend conn friendSlug
                let gamePlayed = GameProjection.getGamesPlayedWithFriend conn friendSlug
                let movieWatched = MovieProjection.getMoviesWatchedWithFriend conn friendSlug
                let seriesWatched = SeriesProjection.getSeriesWatchedWithFriend conn friendSlug
                let gamePlayedAsWatched = gamePlayed |> List.map (fun g -> { Slug = g.Slug; Name = g.Name; Year = g.Year; PosterRef = g.PosterRef; Dates = []; MediaType = g.MediaType })
                return {
                    Mediatheca.Shared.FriendMedia.Recommended = (movieRec @ seriesRec @ gameRec) |> List.sortBy (fun i -> i.Name)
                    WantToWatch = (movieWant @ seriesWant @ gameWant) |> List.sortBy (fun i -> i.Name)
                    Watched = (movieWatched @ seriesWatched @ gamePlayedAsWatched) |> List.sortBy (fun i -> i.Name)
                }
            }

            getFriends = fun () -> async {
                use conn = factory ()
                return FriendProjection.getAll conn
            }

            uploadFriendImage = fun slug data filename -> async {
                use conn = factory ()
                let ext = System.IO.Path.GetExtension(filename).ToLowerInvariant()
                let ref = sprintf "friends/%s%s" slug ext
                ImageStore.saveImage imageBasePath ref data
                let sid = Friends.streamId slug
                let friend = FriendProjection.getBySlug conn slug
                match friend with
                | Some f ->
                    let result =
                        executeCommand
                            conn sid
                            Friends.Serialization.fromStoredEvent
                            Friends.reconstitute
                            Friends.decide
                            Friends.Serialization.toEventData
                            (Friends.Update_friend (f.Name, Some ref))
                            friendProjections
                    match result with
                    | Ok () -> return Ok ref
                    | Error e -> return Error e
                | None -> return Error "Friend not found"
            }

            saveFriendCropSettings = fun slug cropSettings -> async {
                use conn = factory ()
                let sid = Friends.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        (Friends.Update_crop_settings (cropSettings.OffsetX, cropSettings.OffsetY, cropSettings.Zoom))
                        friendProjections
                match result with
                | Ok () -> return Ok ()
                | Error e -> return Error e
            }

            getTmdbApiKey = fun () -> async {
                use conn = factory ()
                let key =
                    SettingsStore.getSetting conn "tmdb_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setTmdbApiKey = fun key -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "tmdb_api_key" key
                    return Ok ()
                with ex ->
                    return Error $"Failed to save API key: {ex.Message}"
            }

            testTmdbApiKey = fun key -> async {
                try
                    let testConfig: Tmdb.TmdbConfig = {
                        ApiKey = key
                        ImageBaseUrl = "https://image.tmdb.org/t/p/"
                    }
                    let! results = Tmdb.searchMovies httpClient testConfig "test" None
                    return Ok ()
                with ex ->
                    return Error $"TMDB API key validation failed: {ex.Message}"
            }

            getMovieTrailer = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getMovieTrailer httpClient tmdbConfig tmdbId
                with _ ->
                    return None
            }

            getSeriesTrailer = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getSeriesTrailer httpClient tmdbConfig tmdbId
                with _ ->
                    return None
            }

            getSeasonTrailer = fun tmdbId seasonNumber -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getSeasonTrailer httpClient tmdbConfig tmdbId seasonNumber
                with _ ->
                    return None
            }

            getFullCredits = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    let! credits = Tmdb.getMovieCredits httpClient tmdbConfig tmdbId
                    let imageUrl (profilePath: string option) =
                        match profilePath with
                        | Some p -> Some $"{tmdbConfig.ImageBaseUrl}w185{p}"
                        | None -> None
                    let cast =
                        credits.Cast
                        |> List.sortBy (fun c -> c.Order)
                        |> List.map (fun c ->
                            { CastMemberDto.Name = c.Name
                              Role = c.Character
                              TmdbId = c.Id
                              ImageRef = imageUrl c.ProfilePath })
                    let crew =
                        credits.Crew
                        |> List.map (fun c ->
                            { CrewMemberDto.Name = c.Name
                              Job = c.Job
                              Department = c.Department
                              TmdbId = c.Id
                              ImageRef = imageUrl c.ProfilePath })
                    return Ok { FullCreditsDto.Cast = cast; Crew = crew }
                with ex ->
                    return Error $"Failed to load full credits: {ex.Message}"
            }

            // TV Series
            searchTvSeries = fun (query, year) -> async {
                return! Tmdb.searchTvSeries httpClient (getTmdbConfig()) query year
            }

            addSeries = fun tmdbId -> async {
                use conn = factory ()
                return! addSeriesToLibrary conn httpClient getTmdbConfig imageBasePath projectionHandlers tmdbId
            }

            removeSeries = fun slug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Remove_series
                        projectionHandlers
                match result with
                | Ok () ->
                    // Season/episode cache cleanup (series-r2xhv): imperative,
                    // command-time only — never sourced from projection
                    // replay (ADR-0043/ADR-0045's cache-tier discipline).
                    conn
                    |> Db.newCommand "DELETE FROM series_season_cache WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM series_episode_cache WHERE series_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    // Remove catalog entries referencing this series
                    let catalogEntries = CatalogProjection.getEntriesByMediaSlug conn Mediatheca.Shared.MediaType.Series slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up cast and images
                    CastStore.removeSeriesCastAndCleanup conn imageBasePath sid
                    ImageStore.deleteImage imageBasePath (sprintf "posters/series-%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/series-%s.jpg" slug)
                    // Clean up episode stills
                    let stillsDir = System.IO.Path.Combine(imageBasePath, "stills")
                    if System.IO.Directory.Exists(stillsDir) then
                        let stillFiles = System.IO.Directory.GetFiles(stillsDir, sprintf "%s-s*.jpg" slug)
                        for f in stillFiles do
                            try System.IO.File.Delete(f) with _ -> ()
                    // curation-h4k2p: clear the Notes document and its uploaded content images
                    clearNotesOnRemoval conn imageBasePath projectionHandlers Mediatheca.Shared.MediaType.Series slug
                    return Ok ()
                | Error e -> return Error e
            }

            abandonSeries = fun slug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Abandon_series
                        projectionHandlers
            }

            unabandonSeries = fun slug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Unabandon_series
                        projectionHandlers
            }

            getSeries = fun () -> async {
                use conn = factory ()
                return SeriesProjection.getAll conn
            }

            getSeriesDetail = fun slug rewatchId -> async {
                use conn = factory ()
                return SeriesProjection.getBySlug conn slug rewatchId
            }

            setSeriesPersonalRating = fun slug rating -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Set_series_personal_rating rating)
                        projectionHandlers
            }

            setSeriesInFocus = fun slug inFocus -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                let command = if inFocus then Series.Set_series_in_focus else Series.Clear_series_in_focus
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        command
                        projectionHandlers
            }

            addSeriesRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Recommend_series friendSlug)
                        projectionHandlers
            }

            removeSeriesRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_series_recommendation friendSlug)
                        projectionHandlers
            }

            addSeriesWantToWatchWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Want_to_watch_series_with friendSlug)
                        projectionHandlers
            }

            removeSeriesWantToWatchWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_want_to_watch_series_with friendSlug)
                        projectionHandlers
            }

            refreshSeriesFromTmdb = fun slug -> async {
                use conn = factory ()
                let tmdbConfig = getTmdbConfig()
                if System.String.IsNullOrWhiteSpace(tmdbConfig.ApiKey) then
                    return Error "TMDB API key not configured"
                else
                    let! result =
                        SeriesRefresh.refreshOne
                            conn httpClient tmdbConfig imageBasePath projectionHandlers slug
                    match result with
                    | Ok _ -> return Ok ()
                    | Error e -> return Error e
            }

            // Series Rewatch Sessions
            createRewatchSession = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                let rewatchId = System.Guid.NewGuid().ToString("N")
                let data: Series.RewatchSessionCreatedData = {
                    RewatchId = rewatchId
                    Name = request.Name
                    FriendSlugs = request.FriendSlugs
                }
                let result =
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Create_rewatch_session data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok rewatchId
                | Error e -> return Error e
            }

            removeRewatchSession = fun slug rewatchId -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_rewatch_session rewatchId)
                        projectionHandlers
            }

            setDefaultRewatchSession = fun slug rewatchId -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Set_default_rewatch_session rewatchId)
                        projectionHandlers
            }

            addFriendToRewatchSession = fun slug rewatchId friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Add_friend_to_rewatch_session { RewatchId = rewatchId; FriendSlug = friendSlug })
                        projectionHandlers
            }

            removeFriendFromRewatchSession = fun slug rewatchId friendSlug -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_friend_from_rewatch_session { RewatchId = rewatchId; FriendSlug = friendSlug })
                        projectionHandlers
            }

            // Series Episode Progress
            markEpisodeWatched = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episode_watched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markEpisodeUnwatched = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episode_unwatched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                        })
                        projectionHandlers
            }

            markSeasonWatched = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_season_watched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markEpisodesWatchedUpTo = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episodes_watched_up_to {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markSeasonUnwatched = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_season_unwatched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                        })
                        projectionHandlers
            }

            updateEpisodeWatchedDate = fun slug request -> async {
                use conn = factory ()
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Change_episode_watched_date {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            // Series Catalogs
            getCatalogsForSeries = fun slug -> async {
                use conn = factory ()
                return CatalogProjection.getCatalogsForSeriesWithChildren conn slug
            }

            // Games
            searchRawgGames = fun (query, year) -> async {
                return! Rawg.searchGames httpClient (getRawgConfig()) query year
            }

            // games-k3vps: query-based Steam search for the search modal's
            // Steam source toggle — thin wrapper, `searchSteamForGame`
            // (slug-bound re-link) unchanged.
            searchSteamGames = fun (query, year) -> async {
                return! Steam.searchSteamByName httpClient query year
            }

            addGameFromSteam = fun request -> async {
                use conn = factory ()
                return! addGameFromSteamCore conn httpClient imageBasePath projectionHandlers request
            }

            addGame = fun request -> async {
                use conn = factory ()
                try
                    let year = request.Year
                    let baseSlug = Slug.gameSlug request.Name year

                    // Duplicate check: by RAWG id, then by exact case-insensitive name.
                    // Skipped when the caller has already confirmed they want a duplicate.
                    let existing =
                        if request.SkipDuplicateCheck then None
                        else
                            let byRawg =
                                match request.RawgId with
                                | Some rawgId -> GameProjection.findByRawgId conn rawgId
                                | None -> None
                            match byRawg with
                            | Some _ -> byRawg
                            | None ->
                                match GameProjection.findByName conn request.Name with
                                | (existingSlug, _) :: _ ->
                                    match GameProjection.getBySlug conn existingSlug with
                                    | Some g -> Some (existingSlug, g.Name)
                                    | None -> Some (existingSlug, request.Name)
                                | [] -> None

                    match existing with
                    | Some (existingSlug, existingName) ->
                        return Ok (Duplicate_found (existingSlug, existingName))
                    | None ->
                        let slug = generateUniqueSlug conn Games.streamId baseSlug
                        let sid = Games.streamId slug

                        // If we have a RAWG ID, fetch full details for description + download images
                        let! description, coverRef, backdropRef, cacheDescription =
                            match request.RawgId with
                            | Some rawgId ->
                                async {
                                    let rawgConfig = getRawgConfig()
                                    // Fetch full game details (includes description)
                                    let! details =
                                        async {
                                            try
                                                let! d = Rawg.getGameDetails httpClient rawgConfig rawgId
                                                return Some d
                                            with _ -> return None
                                        }

                                    let desc =
                                        match details with
                                        | Some d when d.DescriptionRaw <> "" -> d.DescriptionRaw
                                        | _ -> request.Description

                                    // games-r1tx4: the sanitized-HTML sibling of `desc`
                                    // above, written to the identity card cache (never
                                    // the `Game_added_to_library` payload, which keeps
                                    // carrying the plain `desc` exactly as before) —
                                    // RAWG's HTML `description` field first, falling
                                    // back to `description_raw`, then the request's own
                                    // plain description.
                                    let cacheDesc =
                                        match details with
                                        | Some d when d.Description <> "" -> DescriptionSanitizer.sanitize d.Description
                                        | Some d when d.DescriptionRaw <> "" -> DescriptionSanitizer.sanitize d.DescriptionRaw
                                        | _ -> DescriptionSanitizer.sanitize request.Description

                                    // Download images locally
                                    let bgImage =
                                        match details with
                                        | Some d -> d.BackgroundImage |> Option.orElse request.CoverRef
                                        | None -> request.CoverRef

                                    let bgImageAdditional =
                                        match details with
                                        | Some d -> d.BackgroundImageAdditional
                                        | None -> None

                                    let! coverRef, backdropRef = Rawg.downloadGameImages httpClient slug bgImage bgImageAdditional imageBasePath
                                    return desc, coverRef, backdropRef, cacheDesc
                                }
                            | None ->
                                async { return request.Description, request.CoverRef, request.BackdropRef, "" }

                        let gameData: Games.GameAddedData = {
                            Name = request.Name
                            Year = year
                            Genres = request.Genres
                            Description = description
                            ShortDescription = ""
                            WebsiteUrl = None
                            CoverRef = coverRef
                            BackdropRef = backdropRef
                            RawgId = request.RawgId
                            RawgRating = request.RawgRating
                        }

                        let result =
                            executeCommand
                                conn sid
                                Games.Serialization.fromStoredEvent
                                Games.reconstitute
                                Games.decide
                                Games.Serialization.toEventData
                                (Games.Add_game gameData)
                                projectionHandlers

                        match result with
                        | Error e -> return Error e
                        | Ok () ->
                            // games-r1tx4: the RAWG path's own creation-code-path
                            // identity-card write (ADR-0045's hard constraint: never
                            // the ProjectionHandler) — the same imperative write the
                            // Steam sites already do. Closes the latent defect (this
                            // task's Why): since games-v4nqe dropped `game_detail`'s
                            // `description` projection column, a RAWG-added game had
                            // an empty description until this write happened.
                            if request.RawgId.IsSome then
                                MetadataCache.upsertGameIdentityCard conn slug {
                                    Description = cacheDescription
                                    ShortDescription = ""
                                    WebsiteUrl = None
                                }
                                // games-fffvm: creation-path identity-card
                                // write — stamp so the row never lands in
                                // the backfill's own candidate set.
                                MetadataCache.stampDescriptionFetched conn slug
                            // Auto-attach Steam for RAWG-sourced games with a clear match.
                            // Best-effort: any failure (Steam down, no match, ambiguous) is
                            // swallowed — the user can still click Connect later.
                            if request.RawgId.IsSome then
                                try
                                    let! candidates = Steam.searchSteamByName httpClient request.Name (Some request.Year)
                                    match candidates with
                                    | top :: rest when top.Score >= 0.95 ->
                                        let unambiguous =
                                            match rest with
                                            | next :: _ -> (top.Score - next.Score) >= 0.05
                                            | [] -> true
                                        if unambiguous then
                                            let! _ = attachSteamToGameCore conn httpClient projectionHandlers slug top.AppId
                                            ()
                                    | _ -> ()
                                with ex ->
                                    printfn "[addGame] Steam auto-attach failed for '%s': %s" slug ex.Message
                            return Ok (Created slug)
                with ex ->
                    return Error $"Failed to add game: {ex.Message}"
            }

            removeGame = fun slug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Remove_game
                        projectionHandlers
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this game
                    let catalogEntries = CatalogProjection.getEntriesByMediaSlug conn Mediatheca.Shared.MediaType.Game slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up images
                    ImageStore.deleteImage imageBasePath (sprintf "posters/game-%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/game-%s.jpg" slug)
                    // curation-h4k2p: clear the Notes document and its uploaded content images
                    clearNotesOnRemoval conn imageBasePath projectionHandlers Mediatheca.Shared.MediaType.Game slug
                    return Ok ()
                | Error e -> return Error e
            }

            getGames = fun () -> async {
                use conn = factory ()
                return GameProjection.getAll conn
            }

            getGameDetail = fun slug -> async {
                use conn = factory ()
                return GameProjection.getBySlug conn slug
            }

            setGameStatus = fun slug status -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Change_status status)
                        projectionHandlers
            }

            setGamePersonalRating = fun slug rating -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Set_personal_rating rating)
                        projectionHandlers
            }

            addGameRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Recommend_game friendSlug)
                        projectionHandlers
            }

            removeGameRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_recommendation friendSlug)
                        projectionHandlers
            }

            addGameWantToPlayWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_want_to_play_with friendSlug)
                        projectionHandlers
            }

            removeGameWantToPlayWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_from_want_to_play_with friendSlug)
                        projectionHandlers
            }

            // games-v4nqe: addGamePlayMode/removeGamePlayMode/getAllPlayModes
            // deleted — superseded by overrideGamePlayFacets (ADR-0053).
            overrideGamePlayFacets = fun slug ovr -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Override_play_facets ovr)
                        projectionHandlers
            }

            markGameAsOwned = fun slug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Mark_as_owned
                        projectionHandlers
            }

            removeGameOwnership = fun slug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Remove_ownership
                        projectionHandlers
            }

            addGameFamilyOwner = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_family_owner friendSlug)
                        projectionHandlers
            }

            removeGameFamilyOwner = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_family_owner friendSlug)
                        projectionHandlers
            }

            addGamePlayedWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_played_with friendSlug)
                        projectionHandlers
            }

            removeGamePlayedWith = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_played_with friendSlug)
                        projectionHandlers
            }

            getCatalogsForGame = fun slug -> async {
                use conn = factory ()
                return CatalogProjection.getCatalogsForMedia conn Mediatheca.Shared.MediaType.Game slug
            }

            getGameImageCandidates = fun slug -> async {
                use conn = factory ()
                match GameProjection.getBySlug conn slug with
                | None -> return []
                | Some game ->
                    let currentCandidates =
                        [ match game.CoverRef with
                          | Some ref ->
                              { GameImageCandidate.Url = $"/images/{ref}"
                                Source = "Current"; Label = "Current Cover"; IsCover = true; IsCurrent = true }
                          | None -> ()
                          match game.BackdropRef with
                          | Some ref ->
                              { GameImageCandidate.Url = $"/images/{ref}"
                                Source = "Current"; Label = "Current Backdrop"; IsCover = false; IsCurrent = true }
                          | None -> () ]

                    let steamCandidates =
                        match game.SteamAppId with
                        | Some appId ->
                            [ { GameImageCandidate.Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg"
                                Source = "Steam"; Label = "Steam Library Cover"; IsCover = true; IsCurrent = false }
                              { Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_hero.jpg"
                                Source = "Steam"; Label = "Steam Library Hero"; IsCover = false; IsCurrent = false }
                              { Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"
                                Source = "Steam"; Label = "Steam Header"; IsCover = false; IsCurrent = false } ]
                        | None -> []

                    let! rawgCandidates = async {
                        match game.RawgId with
                        | Some rawgId ->
                            let rawgConfig = getRawgConfig()
                            let! details = async {
                                try
                                    let! d = Rawg.getGameDetails httpClient rawgConfig rawgId
                                    return Some d
                                with _ -> return None
                            }
                            let detailCandidates =
                                match details with
                                | Some (d: Rawg.RawgGameDetailsResponse) ->
                                    [ match d.BackgroundImage with
                                      | Some url ->
                                          { GameImageCandidate.Url = url; Source = "RAWG"; Label = "RAWG Background"; IsCover = false; IsCurrent = false }
                                      | None -> ()
                                      match d.BackgroundImageAdditional with
                                      | Some url ->
                                          { Url = url; Source = "RAWG"; Label = "RAWG Background 2"; IsCover = false; IsCurrent = false }
                                      | None -> () ]
                                | None -> []
                            let! screenshots = Rawg.getGameScreenshots httpClient rawgConfig rawgId
                            return detailCandidates @ screenshots
                        | None -> return []
                    }

                    return currentCandidates @ steamCandidates @ rawgCandidates
            }

            selectGameImage = fun slug sourceUrl imageKind -> async {
                use conn = factory ()
                try
                    let! response = httpClient.GetAsync(sourceUrl) |> Async.AwaitTask
                    response.EnsureSuccessStatusCode() |> ignore
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    let ref =
                        if imageKind = "cover" then $"posters/game-{slug}.jpg"
                        else $"backdrops/game-{slug}.jpg"
                    ImageStore.saveImage imageBasePath ref bytes
                    let sid = Games.streamId slug
                    let command =
                        if imageKind = "cover" then Games.Replace_cover ref
                        else Games.Replace_backdrop ref
                    return
                        executeCommand
                            conn sid
                            Games.Serialization.fromStoredEvent
                            Games.reconstitute
                            Games.decide
                            Games.Serialization.toEventData
                            command
                            projectionHandlers
                with ex ->
                    return Error $"Failed to download image: {ex.Message}"
            }

            getGameTrailers = fun slug -> async {
                use conn = factory ()
                try
                    match GameProjection.getBySlug conn slug with
                    | None -> return []
                    | Some game ->
                        let! steamTrailers = async {
                            match game.SteamAppId with
                            | Some appId -> return! Steam.getSteamStoreTrailers httpClient appId
                            | None -> return []
                        }
                        let! rawgTrailers = async {
                            match game.RawgId with
                            | Some rawgId ->
                                let rawgConfig = getRawgConfig()
                                return! Rawg.getGameTrailersAll httpClient rawgConfig rawgId
                            | None -> return []
                        }
                        // Deduplicate by VideoUrl, Steam wins
                        let steamUrls =
                            steamTrailers
                            |> List.map (fun t -> t.VideoUrl)
                            |> Set.ofList
                        let dedupedRawg =
                            rawgTrailers
                            |> List.filter (fun t -> not (Set.contains t.VideoUrl steamUrls))
                        return steamTrailers @ dedupedRawg
                with _ ->
                    return []
            }

            // Books (books-y9kxy, ADR-0076/ADR-0077)
            getBooks = fun () -> async {
                use conn = factory ()
                return BookProjection.getAll conn
            }

            getBook = fun slug -> async {
                use conn = factory ()
                return BookProjection.getBySlug conn slug
            }

            addBook = fun request -> async {
                use conn = factory ()
                return! addBookToLibraryImpl conn httpClient imageBasePath projectionHandlers request None
            }

            removeBook = fun slug -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        Books.Remove_book_from_library
                        projectionHandlers
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this book
                    let catalogEntries = CatalogProjection.getEntriesByMediaSlug conn Mediatheca.Shared.MediaType.Book slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    ImageStore.deleteImage imageBasePath (sprintf "posters/book-%s.jpg" slug)
                    // curation-h4k2p: clear the Notes document and its uploaded content images
                    clearNotesOnRemoval conn imageBasePath projectionHandlers Mediatheca.Shared.MediaType.Book slug
                    return Ok ()
                | Error e -> return Error e
            }

            // books-xyqyb: date-range validation is an edge concern (ADR-0077
            // §6, mirroring how `setBookProgress`'s `ObservedOn` is treated)
            // — `decide` only validates the yyyy-MM-dd format, not whether
            // the date has already happened.
            setBookStatus = fun slug status effectiveOn -> async {
                let isFuture =
                    match effectiveOn with
                    | Some d -> d > System.DateTime.Now.ToString("yyyy-MM-dd")
                    | None -> false
                if isFuture then
                    return Error "effectiveOn cannot be in the future"
                else
                    use conn = factory ()
                    let sid = Books.streamId slug
                    return
                        executeCommand
                            conn sid
                            Books.Serialization.fromStoredEvent
                            Books.reconstitute
                            Books.decide
                            Books.Serialization.toEventData
                            (Books.Change_status (status, effectiveOn))
                            projectionHandlers
            }

            setBookFormat = fun slug format -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Set_format format)
                        projectionHandlers
            }

            setBookPersonalRating = fun slug rating -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Set_personal_rating rating)
                        projectionHandlers
            }

            // Manual source, today's date unless given; percent is computed
            // from Page/TotalPages when Percent is absent (books-y9kxy).
            // Derived-percent rounding for adapter sources is the adapters'
            // own concern (integration-jjvg2/integration-y2ak4 floor theirs);
            // this manual path floors too, via integer division.
            setBookProgress = fun request -> async {
                use conn = factory ()
                let position =
                    match request.Page with
                    | Some page -> Some (Page (page, request.TotalPages))
                    | None -> None
                let computedPercent =
                    match request.Percent with
                    | Some p -> Some p
                    | None ->
                        match request.Page, request.TotalPages with
                        | Some page, Some total when total > 0 -> Some (page * 100 / total)
                        | _ -> None
                match computedPercent with
                | None -> return Error "setBookProgress requires Percent, or Page and TotalPages"
                | Some percent ->
                    let observedOn = request.ObservedOn |> Option.defaultValue (System.DateTime.UtcNow.ToString("yyyy-MM-dd"))
                    let data: Books.ReadingProgressObservedData = {
                        Percent = percent
                        Position = position
                        Source = ProgressSource.Manual
                        ObservedOn = observedOn
                        Finished = false
                    }
                    let sid = Books.streamId request.Slug
                    return
                        executeCommand
                            conn sid
                            Books.Serialization.fromStoredEvent
                            Books.reconstitute
                            Books.decide
                            Books.Serialization.toEventData
                            (Books.Observe_reading_progress data)
                            projectionHandlers
            }

            removeBookProgressObservation = fun slug observedOn source -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Remove_reading_progress_observation (observedOn, source))
                        projectionHandlers
            }

            linkBookExternalId = fun slug externalId -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Link_external_id externalId)
                        projectionHandlers
            }

            recommendBookBy = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Recommend_by friendSlug)
                        projectionHandlers
            }

            removeBookRecommendation = fun slug friendSlug -> async {
                use conn = factory ()
                let sid = Books.streamId slug
                return
                    executeCommand
                        conn sid
                        Books.Serialization.fromStoredEvent
                        Books.reconstitute
                        Books.decide
                        Books.Serialization.toEventData
                        (Books.Remove_recommendation friendSlug)
                        projectionHandlers
            }

            getCatalogsForBook = fun slug -> async {
                use conn = factory ()
                return CatalogProjection.getCatalogsForMedia conn Mediatheca.Shared.MediaType.Book slug
            }

            // Games Settings
            getRawgApiKey = fun () -> async {
                use conn = factory ()
                let key =
                    SettingsStore.getSetting conn "rawg_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setRawgApiKey = fun key -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "rawg_api_key" key
                    return Ok ()
                with ex ->
                    return Error $"Failed to save API key: {ex.Message}"
            }

            testRawgApiKey = fun key -> async {
                try
                    let testConfig: Rawg.RawgConfig = {
                        ApiKey = key
                    }
                    let! _ = Rawg.searchGames httpClient testConfig "test" None
                    return Ok ()
                with ex ->
                    return Error $"RAWG API key validation failed: {ex.Message}"
            }

            // Steam Integration
            getSteamApiKey = fun () -> async {
                use conn = factory ()
                let key =
                    SettingsStore.getSetting conn "steam_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setSteamApiKey = fun key -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "steam_api_key" key
                    // integration-r8kwd: saving a (presumably fresh) key clears any
                    // standing "key rejected" notice — the builder's remedy for that
                    // notice is exactly this action.
                    SettingsStore.deleteSetting conn "steam_api_key_last_error"
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Steam API key: {ex.Message}"
            }

            testSteamApiKey = fun key -> async {
                // integration-k4vqm: this used to hardcode a third-party SteamID
                // ("Robin Walker, public profile") and treat an empty
                // `GetOwnedGames` response as "key may be invalid" — but Steam
                // returns that exact same empty shape for ANY profile whose Game
                // Details privacy is not Public, a fact this project neither
                // controls nor can observe changing. The key is tested against
                // the builder's OWN stored `steam_id` instead (a profile this
                // project actually controls); if none is stored yet, it falls
                // back to `tryValidateApiKeyOnly`, which validates the key alone,
                // independent of any profile's privacy.
                use conn = factory ()
                try
                    let steamId = SettingsStore.getSetting conn "steam_id" |> Option.defaultValue ""
                    let! probeResult =
                        if not (System.String.IsNullOrWhiteSpace steamId) then
                            let testConfig: Steam.SteamConfig = { ApiKey = key; SteamId = steamId }
                            async {
                                let! result = Steam.tryGetOwnedGames httpClient testConfig
                                match result with
                                | Ok [] -> return Choice1Of2 ()      // key valid, probe inconclusive
                                | Ok (_ :: _) -> return Choice2Of2 (Ok ())
                                | Error Steam.KeyRejected -> return Choice2Of2 (Error Steam.webApiKeyRejectedMessage)
                                | Error (Steam.WebApiOtherFailure m) -> return Choice2Of2 (Error $"Steam API key validation failed: {m}")
                            }
                        else
                            async {
                                let! result = Steam.tryValidateApiKeyOnly httpClient key
                                match result with
                                | Ok () -> return Choice2Of2 (Ok ())
                                | Error Steam.KeyRejected -> return Choice2Of2 (Error Steam.webApiKeyRejectedMessage)
                                | Error (Steam.WebApiOtherFailure m) -> return Choice2Of2 (Error $"Steam API key validation failed: {m}")
                            }
                    match probeResult with
                    | Choice1Of2 () ->
                        // Key accepted; the probe target's response was empty. This is
                        // NOT evidence the key is bad — never say "may be invalid".
                        return Error "Steam API key accepted; the profile's Game Details privacy is not Public, or the account owns no games — this does not indicate a problem with the key"
                    | Choice2Of2 (Ok ()) ->
                        // integration-r8kwd: a successful, genuinely informative test
                        // also clears any standing "key rejected" notice (acceptance
                        // criterion 4).
                        SettingsStore.deleteSetting conn "steam_api_key_last_error"
                        return Ok ()
                    | Choice2Of2 (Error msg) ->
                        return Error msg
                with ex ->
                    return Error $"Steam API key validation failed: {ex.Message}"
            }

            getSteamId = fun () -> async {
                use conn = factory ()
                return
                    SettingsStore.getSetting conn "steam_id"
                    |> Option.defaultValue ""
            }

            setSteamId = fun steamId -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "steam_id" steamId
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Steam ID: {ex.Message}"
            }

            resolveSteamVanityUrl = fun vanityUrl -> async {
                let steamConfig = getSteamConfig()
                if System.String.IsNullOrWhiteSpace(steamConfig.ApiKey) then
                    return Error "Steam API key not configured"
                else
                    return! Steam.resolveVanityUrl httpClient steamConfig.ApiKey vanityUrl
            }

            importSteamLibrary = fun () -> async {
                use conn = factory ()
                try
                    let steamConfig = getSteamConfig()
                    if System.String.IsNullOrWhiteSpace(steamConfig.ApiKey) || System.String.IsNullOrWhiteSpace(steamConfig.SteamId) then
                        return Error "Steam API key and Steam ID must be configured"
                    else
                        let! steamGames = Steam.getOwnedGames httpClient steamConfig
                        let mutable gamesMatched = 0
                        let mutable gamesCreated = 0
                        let mutable playTimeUpdated = 0
                        let mutable errors: string list = []

                        for steamGame in steamGames do
                            try
                                // games-p6vkz: the old direct play-time setter
                                // is gone — this bulk import now dispatches
                                // the same pure Steam-sync decision
                                // (Games.decide's Record_steam_observed_total)
                                // the scheduled sync uses, keyed on
                                // rtime_last_played (or today, if Steam never
                                // reported one).
                                let gamingDay =
                                    Steam.unixTimestampToDateString steamGame.RtimeLastPlayed
                                    |> Option.defaultValue (System.DateTime.Now.ToString("yyyy-MM-dd"))
                                // Try to match by steam_app_id first
                                let existingByAppId = GameProjection.findBySteamAppId conn steamGame.AppId
                                match existingByAppId with
                                | Some slug ->
                                    // Matched by steam_app_id — update play time
                                    gamesMatched <- gamesMatched + 1
                                    let sid = Games.streamId slug
                                    if steamGame.PlaytimeMinutes > 0 then
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Record_steam_observed_total (steamGame.PlaytimeMinutes, gamingDay))
                                                projectionHandlers
                                        match result with
                                        | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                        | Error _ -> ()
                                    // games-v4nqe: Set_steam_last_played demoted — the
                                    // column it wrote is dropped; getBySlug now derives
                                    // SteamLastPlayed from game_play_session directly.
                                    executeCommand conn sid
                                        Games.Serialization.fromStoredEvent
                                        Games.reconstitute
                                        Games.decide
                                        Games.Serialization.toEventData
                                        Games.Mark_as_owned
                                        projectionHandlers |> ignore
                                | None ->
                                    // Try to match by name
                                    let existingByName = GameProjection.findByName conn steamGame.Name
                                    match existingByName with
                                    | (slug, _) :: _ ->
                                        // Matched by name — set steam_app_id, add store, update play time
                                        gamesMatched <- gamesMatched + 1
                                        let sid = Games.streamId slug
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_steam_app_id steamGame.AppId)
                                            projectionHandlers |> ignore
                                        if steamGame.PlaytimeMinutes > 0 then
                                            let result =
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Record_steam_observed_total (steamGame.PlaytimeMinutes, gamingDay))
                                                    projectionHandlers
                                            match result with
                                            | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                            | Error _ -> ()
                                        // Fetch Steam Store details for description, website, and facets
                                        let! storeDetails = Steam.getSteamStoreDetails httpClient steamGame.AppId
                                        match storeDetails with
                                        | Ok details ->
                                            if details.AboutTheGame <> "" then
                                                updateGameIdentityCache conn slug None (Some details.ShortDescription) None
                                            if details.WebsiteUrl.IsSome then
                                                updateGameIdentityCache conn slug None None (Some details.WebsiteUrl)
                                            updateGameFacetsFromCategoryIds conn slug details.CategoryIds
                                            updateGameReleaseDate conn slug details
                                        | Error _ -> ()
                                        // games-v4nqe: Set_steam_last_played demoted — see
                                        // the matched-by-appid branch's comment above.
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            Games.Mark_as_owned
                                            projectionHandlers |> ignore
                                    | [] ->
                                        // No match — create new game
                                        // Try RAWG enrichment
                                        let rawgConfig = getRawgConfig()
                                        let! rawgResults =
                                            if not (System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey)) then
                                                Rawg.searchGames httpClient rawgConfig steamGame.Name None
                                            else
                                                async { return [] }

                                        let rawgMatch = rawgResults |> List.tryHead

                                        let rawgDescription, genres, rawgId, rawgRating, year =
                                            match rawgMatch with
                                            | Some r ->
                                                let rawgYear = r.Year |> Option.defaultValue 0
                                                "", r.Genres, Some r.RawgId, r.Rating, rawgYear
                                            | None ->
                                                "", [], None, None, 0

                                        // Fetch Steam Store details for description, website, and facets
                                        let! storeDetails = Steam.getSteamStoreDetails httpClient steamGame.AppId
                                        let steamDescription, steamShortDescription, steamWebsiteUrl, steamCategoryIds =
                                            match storeDetails with
                                            | Ok details ->
                                                Steam.storeDescription details, details.ShortDescription, details.WebsiteUrl, details.CategoryIds
                                            | Error _ -> "", "", None, []

                                        // Use Steam description if available, then RAWG, then empty
                                        let description =
                                            if steamDescription <> "" then steamDescription
                                            elif rawgDescription <> "" then rawgDescription
                                            else ""

                                        // games-r1tx4 (verifier iteration 2): the
                                        // plain-text sibling of `description` above, for
                                        // the `Game_added_to_library` payload -- an event
                                        // never carries HTML (ADR-0043), even though
                                        // `description` (sanitized HTML when it came from
                                        // Steam) is what the identity-card cache write
                                        // below keeps.
                                        let plainDescription =
                                            if steamDescription <> "" then DescriptionSanitizer.toPlainText steamDescription
                                            elif rawgDescription <> "" then rawgDescription
                                            else ""

                                        // Download cover and backdrop from Steam CDN
                                        let baseSlug = Slug.gameSlug steamGame.Name (if year > 0 then year else 2000)
                                        let slug = generateUniqueSlug conn Games.streamId baseSlug
                                        let! coverRef = Steam.downloadSteamCover httpClient steamGame.AppId slug imageBasePath
                                        let! backdropRef = Steam.downloadSteamBackdrop httpClient steamGame.AppId slug imageBasePath

                                        let gameData: Games.GameAddedData = {
                                            Name = steamGame.Name
                                            Year = if year > 0 then year else 0
                                            Genres = genres
                                            Description = plainDescription
                                            ShortDescription = steamShortDescription
                                            WebsiteUrl = steamWebsiteUrl
                                            CoverRef = coverRef
                                            BackdropRef = backdropRef
                                            RawgId = rawgId
                                            RawgRating = rawgRating
                                        }

                                        let sid = Games.streamId slug
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Add_game gameData)
                                                projectionHandlers

                                        match result with
                                        | Ok () ->
                                            gamesCreated <- gamesCreated + 1
                                            // Set steam_app_id and store
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_app_id steamGame.AppId)
                                                projectionHandlers |> ignore
                                            if steamGame.PlaytimeMinutes > 0 then
                                                let ptResult =
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Record_steam_observed_total (steamGame.PlaytimeMinutes, gamingDay))
                                                        projectionHandlers
                                                match ptResult with
                                                | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                                | Error _ -> ()
                                            // games-v4nqe (hazard 1): creation code path writes the
                                            // identity card + derived facets directly, imperatively,
                                            // never the ProjectionHandler (ADR-0045).
                                            MetadataCache.upsertGameIdentityCard conn slug {
                                                Description = description
                                                ShortDescription = steamShortDescription
                                                WebsiteUrl = steamWebsiteUrl
                                            }
                                            // games-fffvm: creation-path
                                            // identity-card write — stamp so
                                            // the row never lands in the
                                            // backfill's own candidate set.
                                            MetadataCache.stampDescriptionFetched conn slug
                                            updateGameFacetsFromCategoryIds conn slug steamCategoryIds
                                            match storeDetails with
                                            | Ok details -> updateGameReleaseDate conn slug details
                                            | Error _ -> ()
                                            // Set_steam_last_played demoted — the column it
                                            // wrote is dropped; SteamLastPlayed is derived
                                            // from game_play_session at query time.
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                Games.Mark_as_owned
                                                projectionHandlers |> ignore
                                        | Error e ->
                                            errors <- errors @ [ sprintf "Failed to create '%s': %s" steamGame.Name e ]
                            with ex ->
                                errors <- errors @ [ sprintf "Error processing '%s': %s" steamGame.Name ex.Message ]

                        // Backfill descriptions for games matched by steam_app_id with empty descriptions
                        let mutable descriptionsEnriched = 0
                        let gamesToEnrich = GameProjection.findGamesWithEmptyDescriptionAndSteamAppId conn
                        for (slug, steamAppId) in gamesToEnrich do
                            try
                                // Pacing lives inside Steam.getSteamStoreDetails itself now
                                // (integration-w7ktb's Adapter-owned storefront throttle) --
                                // callers no longer pace themselves.
                                let! storeDetails = Steam.getSteamStoreDetails httpClient steamAppId
                                match storeDetails with
                                | Ok details ->
                                    let desc = Steam.storeDescription details
                                    if desc <> "" then
                                        updateGameIdentityCache conn slug (Some desc) None None
                                    if details.ShortDescription <> "" then
                                        updateGameIdentityCache conn slug None (Some details.ShortDescription) None
                                    if details.WebsiteUrl.IsSome then
                                        updateGameIdentityCache conn slug None None (Some details.WebsiteUrl)
                                    updateGameFacetsFromCategoryIds conn slug details.CategoryIds
                                    updateGameReleaseDate conn slug details
                                    if desc <> "" || details.ShortDescription <> "" then
                                        descriptionsEnriched <- descriptionsEnriched + 1
                                | Error _ -> ()
                            with ex ->
                                errors <- errors @ [ sprintf "Failed to enrich '%s': %s" slug ex.Message ]

                        if descriptionsEnriched > 0 then
                            printfn "Steam import: enriched %d games with missing descriptions" descriptionsEnriched

                        return Ok {
                            Mediatheca.Shared.SteamImportResult.GamesMatched = gamesMatched
                            GamesCreated = gamesCreated
                            PlayTimeUpdated = playTimeUpdated
                            Errors = errors
                        }
                with ex ->
                    return Error $"Steam import failed: {ex.Message}"
            }

            getSteamFamilyToken = fun () -> async {
                use conn = factory ()
                let token =
                    SettingsStore.getSetting conn "steam_family_token"
                    |> Option.defaultValue ""
                if token.Length > 4 then
                    return sprintf "****%s" (token.Substring(token.Length - 4))
                elif token.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setSteamFamilyToken = fun token -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "steam_family_token" token
                    return Ok ()
                with ex ->
                    return Error $"Failed to save family token: {ex.Message}"
            }

            getSteamFamilyMembers = fun () -> async {
                use conn = factory ()
                let json =
                    SettingsStore.getSetting conn "steam_family_members"
                    |> Option.defaultValue "[]"
                let steamConfig = getSteamConfig()
                let userSteamId = steamConfig.SteamId
                let decoder =
                    Thoth.Json.Net.Decode.list (
                        Thoth.Json.Net.Decode.object (fun get -> {
                            Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                            DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                            FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                            IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                        })
                    )
                match Thoth.Json.Net.Decode.fromString decoder json with
                | Ok members ->
                    return members |> List.map (fun m ->
                        { m with IsMe = not (System.String.IsNullOrWhiteSpace(userSteamId)) && m.SteamId = userSteamId })
                | Error _ -> return []
            }

            setSteamFamilyMembers = fun members -> async {
                use conn = factory ()
                try
                    let json =
                        members
                        |> List.map (fun m ->
                            Thoth.Json.Net.Encode.object [
                                "steamId", Thoth.Json.Net.Encode.string m.SteamId
                                "displayName", Thoth.Json.Net.Encode.string m.DisplayName
                                "friendSlug", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string m.FriendSlug
                                "isMe", Thoth.Json.Net.Encode.bool m.IsMe
                            ])
                        |> Thoth.Json.Net.Encode.list
                        |> Thoth.Json.Net.Encode.toString 0
                    SettingsStore.setSetting conn "steam_family_members" json
                    return Ok ()
                with ex ->
                    return Error $"Failed to save family members: {ex.Message}"
            }

            fetchSteamFamilyMembers = fun () -> async {
                use conn = factory ()
                try
                    let accessToken =
                        SettingsStore.getSetting conn "steam_family_token"
                        |> Option.defaultValue ""
                    if System.String.IsNullOrWhiteSpace(accessToken) then
                        return Error "Steam Family access token not configured"
                    else
                        let steamConfig = getSteamConfig()
                        // Get family group ID — plain fetch, no mint-and-retry
                        // (integration-v0xmv): the browser-obtained token pasted
                        // in Settings is the only credential.
                        printfn "[SteamFamily] Step 1: Calling getFamilyGroupForUser..."
                        let! familyResult = Steam.getFamilyGroupForUser httpClient accessToken
                        match familyResult with
                        | Error e ->
                            printfn "[SteamFamily] getFamilyGroupForUser FAILED: %s" e
                            return Error e
                        | Ok familyGroupBasic ->
                            printfn "[SteamFamily] Got family group ID: %s, basic members: %d" familyGroupBasic.FamilyGroupid familyGroupBasic.Members.Length
                            printfn "[SteamFamily] Step 2: Calling getFamilyGroup..."
                            let! familyDetailResult = Steam.getFamilyGroup httpClient accessToken familyGroupBasic.FamilyGroupid
                            let familyMembers =
                                match familyDetailResult with
                                | Ok fg ->
                                    printfn "[SteamFamily] getFamilyGroup OK — members: %d (steamids: %s)" fg.Members.Length (fg.Members |> List.map (fun m -> m.Steamid) |> String.concat ", ")
                                    fg.Members
                                | Error e ->
                                    printfn "[SteamFamily] getFamilyGroup FAILED: %s — falling back to basic members (%d)" e familyGroupBasic.Members.Length
                                    familyGroupBasic.Members // fallback

                            printfn "[SteamFamily] Total family members: %d" familyMembers.Length

                            // Resolve display names via Steam Web API
                            let! playerNames =
                                if not (System.String.IsNullOrWhiteSpace(steamConfig.ApiKey)) && not (List.isEmpty familyMembers) then
                                    Steam.getPlayerSummaries httpClient steamConfig.ApiKey (familyMembers |> List.map (fun m -> m.Steamid))
                                else
                                    async { return Ok [] }

                            let nameMap =
                                match playerNames with
                                | Ok players -> players |> List.map (fun p -> p.Steamid, p.PersonaName) |> Map.ofList
                                | Error _ -> Map.empty

                            // Read existing mappings to preserve FriendSlug
                            let existingMembersJson =
                                SettingsStore.getSetting conn "steam_family_members"
                                |> Option.defaultValue "[]"
                            let memberDecoder =
                                Thoth.Json.Net.Decode.list (
                                    Thoth.Json.Net.Decode.object (fun get -> {
                                        Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                                        DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                                        FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                                        IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                                    })
                                )
                            let existingMappings =
                                match Thoth.Json.Net.Decode.fromString memberDecoder existingMembersJson with
                                | Ok m -> m |> List.map (fun m -> m.SteamId, m.FriendSlug) |> Map.ofList
                                | Error _ -> Map.empty

                            let userSteamId = steamConfig.SteamId
                            let members =
                                familyMembers
                                |> List.map (fun m ->
                                    { Mediatheca.Shared.SteamFamilyMember.SteamId = m.Steamid
                                      DisplayName = nameMap |> Map.tryFind m.Steamid |> Option.defaultValue m.Steamid
                                      FriendSlug = existingMappings |> Map.tryFind m.Steamid |> Option.flatten
                                      IsMe = not (System.String.IsNullOrWhiteSpace(userSteamId)) && m.Steamid = userSteamId })

                            // Persist
                            let json =
                                members
                                |> List.map (fun m ->
                                    Thoth.Json.Net.Encode.object [
                                        "steamId", Thoth.Json.Net.Encode.string m.SteamId
                                        "displayName", Thoth.Json.Net.Encode.string m.DisplayName
                                        "friendSlug", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string m.FriendSlug
                                        "isMe", Thoth.Json.Net.Encode.bool m.IsMe
                                    ])
                                |> Thoth.Json.Net.Encode.list
                                |> Thoth.Json.Net.Encode.toString 0
                            SettingsStore.setSetting conn "steam_family_members" json

                            return Ok members
                with ex ->
                    return Error $"Failed to fetch family members: {ex.Message}"
            }

            importSteamFamily = fun () -> async {
                use conn = factory ()
                return! runSteamFamilyImport conn httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers (fun _ -> ()) Incremental
            }

            // Connect with Steam (manual attach)
            searchSteamForGame = fun slug -> async {
                use conn = factory ()
                match GameProjection.getBySlug conn slug with
                | None -> return []
                | Some game ->
                    try
                        let yearOpt = if game.Year > 0 then Some game.Year else None
                        return! Steam.searchSteamByName httpClient game.Name yearOpt
                    with ex ->
                        printfn "[searchSteamForGame] Failed for '%s': %s" slug ex.Message
                        return []
            }

            attachSteamToGame = fun (slug, appId) -> async {
                use conn = factory ()
                return! attachSteamToGameCore conn httpClient projectionHandlers slug appId
            }

            searchRawgForGame = fun slug -> async {
                use conn = factory ()
                match GameProjection.getBySlug conn slug with
                | None -> return []
                | Some game ->
                    let rawgConfig = getRawgConfig()
                    if System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey) then return []
                    else
                        try
                            let yearOpt = if game.Year > 0 then Some game.Year else None
                            return! Rawg.searchGames httpClient rawgConfig game.Name yearOpt
                        with ex ->
                            printfn "[searchRawgForGame] Failed for '%s': %s" slug ex.Message
                            return []
            }

            attachRawgToGame = fun (slug, rawgId) -> async {
                use conn = factory ()
                let rawgConfig = getRawgConfig()
                if System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey) then
                    return Error "RAWG API key not configured"
                else
                    try
                        let! details = Rawg.getGameDetails httpClient rawgConfig rawgId
                        let sid = Games.streamId slug
                        let ratingOpt =
                            match details.Rating with
                            | Some r when r > 0.0 -> Some r
                            | _ -> None
                        return
                            executeCommand
                                conn sid
                                Games.Serialization.fromStoredEvent
                                Games.reconstitute
                                Games.decide
                                Games.Serialization.toEventData
                                (Games.Set_rawg_id (rawgId, ratingOpt))
                                projectionHandlers
                            |> Result.map ignore
                    with ex ->
                        return Error (sprintf "RAWG lookup failed: %s" ex.Message)
            }

            // Jellyfin Integration
            getJellyfinServerUrl = fun () -> async {
                use conn = factory ()
                return SettingsStore.getSetting conn "jellyfin_server_url" |> Option.defaultValue ""
            }

            setJellyfinServerUrl = fun url -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "jellyfin_server_url" url
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Jellyfin server URL: {ex.Message}"
            }

            getJellyfinUsername = fun () -> async {
                use conn = factory ()
                return SettingsStore.getSetting conn "jellyfin_username" |> Option.defaultValue ""
            }

            setJellyfinCredentials = fun (username, password) -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "jellyfin_username" username
                    SettingsStore.setSetting conn "jellyfin_password" password
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Jellyfin credentials: {ex.Message}"
            }

            scanJellyfinLibrary = fun () -> async {
                use conn = factory ()
                try
                    let config = getJellyfinConfig ()
                    if System.String.IsNullOrWhiteSpace(config.AccessToken) || System.String.IsNullOrWhiteSpace(config.UserId) then
                        return Error "Jellyfin not configured. Please test the connection first."
                    else
                        // Persist a fresh token/user after a self-healing re-auth (integration-002),
                        // and refresh the in-flight config so subsequent fetches use it.
                        let mutable config = config
                        let persistAuth (auth: Jellyfin.JellyfinAuthResult) =
                            SettingsStore.setSetting conn "jellyfin_user_id" auth.UserId
                            SettingsStore.setSetting conn "jellyfin_access_token" auth.AccessToken
                            config <- { config with UserId = auth.UserId; AccessToken = auth.AccessToken }
                        // Fetch movies and series from Jellyfin
                        let! moviesResult = Jellyfin.getMoviesWithReauth httpClient config persistAuth
                        let! seriesResult = Jellyfin.getSeriesWithReauth httpClient config persistAuth
                        match moviesResult, seriesResult with
                        | Error e, _ -> return Error (sprintf "Failed to fetch movies: %s" e)
                        | _, Error e -> return Error (sprintf "Failed to fetch series: %s" e)
                        | Ok jellyfinMovies, Ok jellyfinSeries ->
                            // Build lookup of existing Mediatheca items by tmdb_id
                            let moviesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM movie_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            let seriesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM series_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            // Helper to check if movie has watch sessions
                            let movieHasWatchData (slug: string) =
                                conn
                                |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions WHERE movie_slug = @slug"
                                |> Db.setParams [ "slug", SqlType.String slug ]
                                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                                |> Option.defaultValue 0
                                |> fun c -> c > 0

                            // Helper to check if series has any watched episodes
                            let seriesHasWatchData (slug: string) =
                                conn
                                |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE series_slug = @slug"
                                |> Db.setParams [ "slug", SqlType.String slug ]
                                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                                |> Option.defaultValue 0
                                |> fun c -> c > 0

                            let toJellyfinItem (item: Jellyfin.JellyfinBaseItem) (itemType: JellyfinItemType) : JellyfinItem =
                                let tmdbId =
                                    item.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                { JellyfinId = item.Id
                                  Name = item.Name
                                  Year = item.ProductionYear
                                  ItemType = itemType
                                  TmdbId = tmdbId
                                  Played = item.UserData |> Option.map (fun ud -> ud.Played) |> Option.defaultValue false
                                  PlayCount = item.UserData |> Option.map (fun ud -> ud.PlayCount) |> Option.defaultValue 0
                                  LastPlayedDate = item.UserData |> Option.bind (fun ud -> ud.LastPlayedDate) }

                            // Match movies
                            let matchedMovies, unmatchedMovies =
                                jellyfinMovies
                                |> List.fold (fun (matched, unmatched) item ->
                                    let jItem = toJellyfinItem item JellyfinMovie
                                    match jItem.TmdbId with
                                    | Some tmdbId ->
                                        match Map.tryFind tmdbId moviesByTmdbId with
                                        | Some (slug, name) ->
                                            let m: JellyfinMatchedItem = {
                                                JellyfinItem = jItem
                                                MediathecaSlug = slug
                                                MediathecaName = name
                                                HasExistingWatchData = movieHasWatchData slug
                                            }
                                            (m :: matched, unmatched)
                                        | None -> (matched, jItem :: unmatched)
                                    | None -> (matched, jItem :: unmatched)
                                ) ([], [])

                            // Match series
                            let matchedSeries, unmatchedSeries =
                                jellyfinSeries
                                |> List.fold (fun (matched, unmatched) item ->
                                    let jItem = toJellyfinItem item JellyfinSeries
                                    match jItem.TmdbId with
                                    | Some tmdbId ->
                                        match Map.tryFind tmdbId seriesByTmdbId with
                                        | Some (slug, name) ->
                                            let m: JellyfinMatchedItem = {
                                                JellyfinItem = jItem
                                                MediathecaSlug = slug
                                                MediathecaName = name
                                                HasExistingWatchData = seriesHasWatchData slug
                                            }
                                            (m :: matched, unmatched)
                                        | None -> (matched, jItem :: unmatched)
                                    | None -> (matched, jItem :: unmatched)
                                ) ([], [])

                            // Clear all existing Jellyfin IDs before re-populating (handles removed items)
                            JellyfinStore.clearAll conn

                            // Persist Jellyfin IDs for matched movies
                            for m in matchedMovies do
                                JellyfinStore.setMovieJellyfinId conn m.MediathecaSlug m.JellyfinItem.JellyfinId

                            // Persist Jellyfin IDs for matched series + fetch episode IDs
                            for m in matchedSeries do
                                JellyfinStore.setSeriesJellyfinId conn m.MediathecaSlug m.JellyfinItem.JellyfinId

                                // Fetch episodes from Jellyfin for this series
                                let! episodesResult = Jellyfin.getEpisodesWithReauth httpClient config persistAuth m.JellyfinItem.JellyfinId
                                match episodesResult with
                                | Ok episodes ->
                                    for ep in episodes do
                                        match ep.ParentIndexNumber, ep.IndexNumber with
                                        | Some seasonNum, Some episodeNum ->
                                            JellyfinStore.setEpisodeJellyfinId conn m.MediathecaSlug seasonNum episodeNum ep.Id
                                        | _ -> () // Skip episodes without season/episode numbers
                                | Error _ -> () // Skip if episode fetch fails

                            let result: JellyfinScanResult = {
                                MatchedMovies = List.rev matchedMovies
                                MatchedSeries = List.rev matchedSeries
                                UnmatchedMovies = List.rev unmatchedMovies
                                UnmatchedSeries = List.rev unmatchedSeries
                            }
                            return Ok result
                with ex ->
                    return Error $"Jellyfin scan failed: {ex.Message}"
            }

            importJellyfinWatchHistory = fun () -> async {
                use conn = factory ()
                return! runJellyfinImport conn httpClient getTmdbConfig getJellyfinConfig imageBasePath projectionHandlers
            }

            // Jellyfin Auto-Sync
            //
            // administration-mz6kp (ADR-0033): `JellyfinSync.triggerSync`
            // spawns the actual import as a genuinely detached background
            // async (`Async.Start`) that keeps running after this member
            // itself has returned — it cannot borrow a `use conn =
            // factory()` scoped to this member (that connection would
            // already be disposed by the time the background work runs), so
            // `factory` is forwarded to `triggerSync` itself, which opens its
            // own connection inside the spawned background async.
            triggerJellyfinSync = fun () ->
                JellyfinSync.triggerSync factory httpClient getJellyfinConfig
                    (fun conn -> runJellyfinImport conn httpClient getTmdbConfig getJellyfinConfig imageBasePath projectionHandlers)

            getJellyfinSyncStatus = fun () -> async {
                return JellyfinSync.getSyncStatus ()
            }

            // Steam Family Last Sync
            getSteamFamilyLastSync = fun () -> async {
                use conn = factory ()
                return SettingsStore.getSetting conn "steam_family_last_sync"
            }

            // integration-n3vqa: the last completed family import's full
            // result (arrivals included) — lets Settings re-render "N new
            // since ..." after a reload, not only right after a fresh click.
            getSteamFamilyLastResult = fun () -> async {
                use conn = factory ()
                return
                    SettingsStore.getSetting conn "steam_family_last_result"
                    |> Option.bind (fun json ->
                        match Thoth.Json.Net.Decode.fromString decodeSteamFamilyImportResult json with
                        | Ok r -> Some r
                        | Error _ -> None)
            }

            // Steam Web API key rejection (integration-r8kwd): the standing
            // notice Settings → Steam shows after a family import's
            // owned-games supplement gets a 401 from a revoked/invalid key.
            getSteamApiKeyLastError = fun () -> async {
                use conn = factory ()
                return SettingsStore.getSetting conn "steam_api_key_last_error"
            }

            testJellyfinConnection = fun (serverUrl, username, password) -> async {
                use conn = factory ()
                try
                    let! authResult = Jellyfin.authenticate httpClient serverUrl username password
                    match authResult with
                    | Ok result ->
                        // Save the token and userId for future use
                        SettingsStore.setSetting conn "jellyfin_server_url" serverUrl
                        SettingsStore.setSetting conn "jellyfin_username" username
                        SettingsStore.setSetting conn "jellyfin_password" password
                        SettingsStore.setSetting conn "jellyfin_user_id" result.UserId
                        SettingsStore.setSetting conn "jellyfin_access_token" result.AccessToken
                        return Ok (sprintf "Connected as %s" result.UserName)
                    | Error e ->
                        return Error e
                with ex ->
                    return Error $"Jellyfin connection test failed: {ex.Message}"
            }

            // qBittorrent Integration (integration-qb7tk): credentials +
            // "Test connection" only, ahead of any destructive flow. Unlike
            // Jellyfin's combined "Test & Save", the setter is a distinct
            // member -- testing never persists (ADR-0071 point 7: a
            // qBittorrent session is cheap to reacquire, so there is no
            // stored-token round-trip to validate here, just credentials).
            getQbittorrentSettings = fun () -> async {
                use conn = factory ()
                return {
                    Url = SettingsStore.getSetting conn "qbittorrent_url" |> Option.defaultValue ""
                    Username = SettingsStore.getSetting conn "qbittorrent_username" |> Option.defaultValue ""
                }
            }

            setQbittorrentCredentials = fun (url, username, password) -> async {
                use conn = factory ()
                try
                    SettingsStore.setSetting conn "qbittorrent_url" url
                    SettingsStore.setSetting conn "qbittorrent_username" username
                    SettingsStore.setSetting conn "qbittorrent_password" password
                    return Ok ()
                with ex ->
                    return Error $"Failed to save qBittorrent credentials: {ex.Message}"
            }

            testQbittorrentConnection = fun (url, username, password) -> async {
                let config: Qbittorrent.QbittorrentConfig = { Url = url; Username = username; Password = password }
                let! result = Qbittorrent.testConnection qbittorrentHttpClient config
                match result with
                | Ok (version, torrentCount) ->
                    return Ok (sprintf "Connected -- qBittorrent %s, %d torrent(s)" version torrentCount)
                | Error Qbittorrent.AuthFailed ->
                    return Error "qBittorrent authentication failed: check the username and password"
                | Error (Qbittorrent.OtherFailure msg) ->
                    return Error $"qBittorrent connection test failed: {msg}"
            }

            // Local copy removal (integration-r4vzm, ADR-0071): plan-then-execute
            // across Jellyfin + qBittorrent. `LocalCopyRemoval.fs` holds every pure
            // seam (path mapping, deletion-scope, torrent matching, seed-risk,
            // the `execute` orchestrator) -- the members below only wire real
            // effects (SQLite reads, HTTP calls) into it. No UI yet
            // (integration-mqsd3).
            planLocalCopyRemoval = fun target -> async {
                use conn = factory ()
                let mutable jfConfig = getJellyfinConfig ()
                let qbConfig = getQbittorrentConfig ()
                let persistAuth (auth: Jellyfin.JellyfinAuthResult) =
                    SettingsStore.setSetting conn "jellyfin_user_id" auth.UserId
                    SettingsStore.setSetting conn "jellyfin_access_token" auth.AccessToken
                    jfConfig <- { jfConfig with UserId = auth.UserId; AccessToken = auth.AccessToken }
                let resolveJellyfinId () =
                    match target with
                    | MovieTarget slug -> JellyfinStore.getMovieJellyfinId conn slug
                    | SeriesTarget slug -> JellyfinStore.getSeriesJellyfinId conn slug
                let effects: LocalCopyRemoval.PlanEffects = {
                    ResolveJellyfinId = resolveJellyfinId
                    FetchItem = fun itemId -> Jellyfin.getItemWithReauth httpClient jfConfig persistAuth itemId
                    ListTorrents = fun () -> async {
                        let! r = Qbittorrent.withSession qbittorrentHttpClient qbConfig (fun session -> Qbittorrent.listTorrents qbittorrentHttpClient qbConfig session)
                        return r |> Result.mapError qbErrorToString
                    }
                    ListFiles = fun t -> async {
                        let! r = Qbittorrent.withSession qbittorrentHttpClient qbConfig (fun session -> Qbittorrent.listFiles qbittorrentHttpClient qbConfig session t.Hash)
                        return r |> Result.mapError qbErrorToString
                    }
                }
                return! LocalCopyRemoval.planLocalCopyRemoval mountRoots effects target
            }

            removeLocalCopy = fun (target, acknowledgedHashes) -> async {
                use conn = factory ()
                let mutable jfConfig = getJellyfinConfig ()
                let qbConfig = getQbittorrentConfig ()
                let persistAuth (auth: Jellyfin.JellyfinAuthResult) =
                    SettingsStore.setSetting conn "jellyfin_user_id" auth.UserId
                    SettingsStore.setSetting conn "jellyfin_access_token" auth.AccessToken
                    jfConfig <- { jfConfig with UserId = auth.UserId; AccessToken = auth.AccessToken }
                // Resolved once, up front -- every effect below closes over the
                // same id so a mid-flow JellyfinStore write can't shift it.
                let jellyfinIdOpt =
                    match target with
                    | MovieTarget slug -> JellyfinStore.getMovieJellyfinId conn slug
                    | SeriesTarget slug -> JellyfinStore.getSeriesJellyfinId conn slug

                let listTorrentsEff () : Async<Result<Qbittorrent.TorrentInfo list, string>> = async {
                    let! r = Qbittorrent.withSession qbittorrentHttpClient qbConfig (fun session -> Qbittorrent.listTorrents qbittorrentHttpClient qbConfig session)
                    return r |> Result.mapError qbErrorToString
                }
                let listFilesEff (t: Qbittorrent.TorrentInfo) : Async<Result<string list, string>> = async {
                    let! r = Qbittorrent.withSession qbittorrentHttpClient qbConfig (fun session -> Qbittorrent.listFiles qbittorrentHttpClient qbConfig session t.Hash)
                    return r |> Result.mapError qbErrorToString
                }

                // Step 1 (ADR-0071): import the target's Jellyfin play state through
                // the existing event-producing paths before any delete -- Jellyfin
                // discards user data with the item.
                let preserveWatchHistory () : Async<Result<unit, string>> = async {
                    match jellyfinIdOpt with
                    | None -> return Ok () // already gone in Jellyfin -- nothing to preserve
                    | Some jellyfinId ->
                        match target with
                        | MovieTarget slug ->
                            let! itemResult = Jellyfin.getItemWithReauth httpClient jfConfig persistAuth jellyfinId
                            match itemResult with
                            | Error e -> return Error (sprintf "failed to import play state: %s" e)
                            | Ok None -> return Ok ()
                            | Ok (Some item) ->
                                let (existsOnDate, getRuntime, writeSession) = movieWatchHistoryEffects conn movieProjections
                                let result = JellyfinImport.syncMovieWatchHistory [ (slug, item) ] existsOnDate getRuntime writeSession
                                if result.Failed then return Error (String.concat "; " result.Errors) else return Ok ()
                        | SeriesTarget slug ->
                            let! episodesResult = Jellyfin.getEpisodesWithReauth httpClient jfConfig persistAuth jellyfinId
                            match episodesResult with
                            | Error e -> return Error (sprintf "failed to import play state: %s" e)
                            | Ok episodes ->
                                let writeEpisode = seriesWatchHistoryWriteEpisode conn projectionHandlers
                                let result =
                                    JellyfinImport.syncSeriesWatchHistory
                                        [ (slug, episodes) ]
                                        (SeriesProjection.getDefaultRewatchId conn)
                                        (SeriesProjection.getWatchedEpisodesForSession conn)
                                        writeEpisode
                                if result.Failed then return Error (String.concat "; " result.Errors) else return Ok ()
                }

                // Steps 2/3: re-resolve the deletion scope from a FRESH Jellyfin
                // fetch -- closes the time-of-check/time-of-use window together
                // with `execute`'s fresh torrent re-match.
                let resolveScope () : Async<Result<string option, string>> = async {
                    match jellyfinIdOpt with
                    | None -> return Ok None
                    | Some jellyfinId ->
                        let! itemResult = Jellyfin.getItemWithReauth httpClient jfConfig persistAuth jellyfinId
                        match itemResult with
                        | Error e -> return Error e
                        | Ok None -> return Ok None
                        | Ok (Some item) ->
                            match item.Path with
                            | None -> return Ok None
                            | Some path ->
                                match LocalCopyRemoval.mapJellyfinPath (mountRoots.JellyfinRoot, mountRoots.QbittorrentRoot) path with
                                | None -> return Ok None
                                | Some mappedPath -> return Ok (Some (LocalCopyRemoval.deletionScope mountRoots.QbittorrentRoot target mappedPath))
                }

                let deleteTorrentsEff (hashes: string list) : Async<Result<unit, string>> = async {
                    let! r = Qbittorrent.withSession qbittorrentHttpClient qbConfig (fun session -> Qbittorrent.deleteTorrents qbittorrentHttpClient qbConfig session hashes true)
                    return r |> Result.mapError qbErrorToString
                }

                let deleteJellyfinItemEff () : Async<Result<unit, string>> = async {
                    match jellyfinIdOpt with
                    | None -> return Ok ()
                    | Some jellyfinId -> return! Jellyfin.deleteItemWithReauth httpClient jfConfig persistAuth jellyfinId
                }

                // Step 6: confirm the Jellyfin item is gone (404) AND none of the
                // acknowledged hashes remain in a fresh torrents/info.
                let verifyGoneEff () : Async<Result<bool, string>> = async {
                    match jellyfinIdOpt with
                    | None -> return Ok true
                    | Some jellyfinId ->
                        let! itemResult = Jellyfin.getItemWithReauth httpClient jfConfig persistAuth jellyfinId
                        match itemResult with
                        | Error e -> return Error e
                        | Ok (Some _) -> return Ok false
                        | Ok None ->
                            let! torrentsResult = listTorrentsEff ()
                            match torrentsResult with
                            | Error e -> return Error e
                            | Ok liveTorrents ->
                                let liveHashes = liveTorrents |> List.map (fun t -> t.Hash) |> Set.ofList
                                let stillPresent = acknowledgedHashes |> List.exists (fun h -> liveHashes |> Set.contains h)
                                return Ok (not stillPresent)
                }

                let clearLinksEff () =
                    match target with
                    | MovieTarget slug -> JellyfinStore.clearMovieJellyfinId conn slug
                    | SeriesTarget slug -> JellyfinStore.clearSeriesJellyfinId conn slug

                let effects: LocalCopyRemoval.ExecuteEffects = {
                    IsSyncInProgress = JellyfinSync.isSyncInProgress
                    PreserveWatchHistory = preserveWatchHistory
                    ResolveScope = resolveScope
                    ListTorrents = listTorrentsEff
                    ListFiles = listFilesEff
                    DeleteTorrents = deleteTorrentsEff
                    DeleteJellyfinItem = deleteJellyfinItemEff
                    VerifyGone = verifyGoneEff
                    ClearLinks = clearLinksEff
                }
                return! LocalCopyRemoval.execute effects acknowledgedHashes
            }

            getViewSettings = fun key -> async {
                use conn = factory ()
                match SettingsStore.getSetting conn ("view:" + key) with
                | Some json ->
                    try
                        let settings = Newtonsoft.Json.JsonConvert.DeserializeObject<ViewSettings>(json, Fable.Remoting.Json.FableJsonConverter())
                        return Some settings
                    with _ -> return None
                | None -> return None
            }

            saveViewSettings = fun key settings -> async {
                use conn = factory ()
                let json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Fable.Remoting.Json.FableJsonConverter())
                SettingsStore.setSetting conn ("view:" + key) json
            }

            getCollapsedSections = fun key -> async {
                use conn = factory ()
                match SettingsStore.getSetting conn ("collapsed:" + key) with
                | Some csv when csv <> "" -> return csv.Split(',') |> Array.toList
                | _ -> return []
            }

            saveCollapsedSections = fun key sections -> async {
                use conn = factory ()
                let csv = sections |> String.concat ","
                SettingsStore.setSetting conn ("collapsed:" + key) csv
            }

            // Playtime Tracking
            getGamePlaySessions = fun slug -> async {
                use conn = factory ()
                return PlaytimeTracker.getPlaySessionsForGame conn slug
            }

            addManualPlaySession = fun (slug, date, minutes) -> async {
                use conn = factory ()
                let runCmd s c =
                    executeCommand conn (Games.streamId s)
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        c
                        projectionHandlers
                return PlaytimeTracker.addManualPlaySessionApi conn slug date minutes runCmd
            }

            updatePlaySession = fun edit -> async {
                use conn = factory ()
                let runCmd s c =
                    executeCommand conn (Games.streamId s)
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        c
                        projectionHandlers
                return PlaytimeTracker.updatePlaySessionApi conn edit runCmd
            }

            deletePlaySession = fun (slug, day) -> async {
                use conn = factory ()
                let runCmd s c =
                    executeCommand conn (Games.streamId s)
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        c
                        projectionHandlers
                return PlaytimeTracker.deletePlaySessionApi conn slug day runCmd
            }

            getPlaytimeSummary = fun fromDate toDate -> async {
                use conn = factory ()
                return PlaytimeTracker.getPlaytimeSummary conn fromDate toDate
            }

            getPlaytimeSyncStatus = fun () -> async {
                use conn = factory ()
                return PlaytimeTracker.getSyncStatus conn
            }

            triggerPlaytimeSync = fun () -> async {
                use conn = factory ()
                return! PlaytimeTracker.runSync conn manualSyncTriggerLock httpClient getSteamConfig getRawgConfig imageBasePath projectionHandlers None
            }

            // Steam Achievements
            getSteamRecentAchievements = fun () -> async {
                try
                    let steamConfig = getSteamConfig()
                    return! Steam.getRecentAchievements httpClient steamConfig
                with ex ->
                    return Error (sprintf "Failed to fetch achievements: %s" ex.Message)
            }

            // HowLongToBeat
            fetchHltbData = fun gameSlug -> async {
                use conn = factory ()
                try
                    // Look up the game name from the projection
                    match GameProjection.getBySlug conn gameSlug with
                    | None -> return Error "Game not found"
                    | Some game ->
                        match! HowLongToBeat.searchGame httpClient game.Name with
                        | None -> return Ok None
                        | Some hltbResult ->
                            let mainHours = HowLongToBeat.toHours hltbResult.CompMainSeconds
                            let mainPlusHours = HowLongToBeat.toHours hltbResult.CompPlusSeconds
                            let completionistHours = HowLongToBeat.toHours hltbResult.Comp100Seconds
                            // games-v4nqe: Set_hltb_hours demoted — HLTB
                            // hours are cache-derived now; this fetch writes
                            // game_metadata_cache directly.
                            MetadataCache.upsertGameHltbHours conn gameSlug
                                (Some mainHours)
                                (if mainPlusHours > 0.0 then Some mainPlusHours else None)
                                (if completionistHours > 0.0 then Some completionistHours else None)
                            return Ok (Some mainHours)
                with ex ->
                    return Error $"Failed to fetch HLTB data: {ex.Message}"
            }

            // Event History
            // Search Preview
            previewTmdbMovie = fun tmdbId -> async {
                try
                    return! Tmdb.previewMovie httpClient (getTmdbConfig()) tmdbId
                with _ -> return None
            }

            previewTmdbSeries = fun tmdbId -> async {
                try
                    return! Tmdb.previewSeries httpClient (getTmdbConfig()) tmdbId
                with _ -> return None
            }

            previewRawgGame = fun rawgId -> async {
                try
                    return! Rawg.previewGame httpClient (getRawgConfig()) rawgId
                with _ -> return None
            }

            getStreamEvents = fun streamPrefix -> async {
                use conn = factory ()
                return EventFormatting.getStreamEvents conn [ streamPrefix ]
            }

            // Open Library (integration-c8d4x, ADR-0075) — appended at the
            // tail deliberately: integration-dhctm depends on this task and
            // appends its own Audible members after these, avoiding a
            // manual-merge conflict at squash time (see this task's Notes).
            searchOpenLibraryBooks = fun query -> async {
                try
                    return! OpenLibrary.searchBooks httpClient (getOpenLibraryConfig()) query
                with _ -> return []
            }

            addBookFromOpenLibrary = fun request -> async {
                use conn = factory ()
                return! addBookFromOpenLibraryImpl conn httpClient getOpenLibraryConfig imageBasePath projectionHandlers request
            }

            refreshBookFromOpenLibrary = fun slug -> async {
                use conn = factory ()
                return! refreshBookFromOpenLibraryImpl conn httpClient getOpenLibraryConfig slug
            }

            // Audible (integration-dhctm, ADR-0074) — an imported audible-cli
            // auth file, never a login or device registration. Appended at
            // the tail after Open Library, per this task's own Notes
            // (avoids a manual-merge conflict at squash time with c8d4x).
            getAudibleStatus = fun () -> async {
                use conn = factory ()
                let authFile =
                    SettingsStore.getSetting conn "audible_auth_file"
                    |> Option.bind (fun json -> match Audible.validateAuthFile json with Ok a -> Some a | Error _ -> None)
                let marketplace =
                    authFile
                    |> Option.map (fun a -> a.LocaleCode)
                    |> Option.orElse (SettingsStore.getSetting conn "audible_marketplace")
                    |> Option.defaultValue "de"
                return {
                    Configured = authFile |> Option.isSome
                    CustomerName = authFile |> Option.map (fun a -> a.CustomerName)
                    Marketplace = marketplace
                    LastError = SettingsStore.getSetting conn "audible_last_error"
                }
            }

            setAudibleAuthFile = fun json -> async {
                use conn = factory ()
                match Audible.validateAuthFile json with
                | Error e -> return Error (sprintf "Invalid auth file: %s" e)
                | Ok authFile ->
                    SettingsStore.setSetting conn "audible_auth_file" json
                    // A freshly-pasted file replaces whatever was there --
                    // any standing rejection notice and cached token belong
                    // to the OLD file and must not survive it (the
                    // `steam_api_key_last_error` clear-on-save convention,
                    // ADR-0065).
                    SettingsStore.deleteSetting conn "audible_last_error"
                    SettingsStore.deleteSetting conn "audible_access_token"
                    SettingsStore.deleteSetting conn "audible_access_token_expires"
                    return Ok {
                        Configured = true
                        CustomerName = Some authFile.CustomerName
                        Marketplace = authFile.LocaleCode
                        LastError = None
                    }
            }

            clearAudibleAuthFile = fun () -> async {
                use conn = factory ()
                SettingsStore.deleteSetting conn "audible_auth_file"
                SettingsStore.deleteSetting conn "audible_access_token"
                SettingsStore.deleteSetting conn "audible_access_token_expires"
                SettingsStore.deleteSetting conn "audible_last_error"
                return ()
            }

            testAudibleConnection = fun () -> async {
                use conn = factory ()
                try
                    let config = getAudibleConfig ()
                    match config.AuthFile with
                    | None -> return Error "Audible is not configured — paste an auth file in Settings"
                    | Some authFile ->
                        let host = Audible.marketplaceHost authFile.LocaleCode
                        let! outcome =
                            withAudibleAccessToken httpClient conn config (fun token -> Audible.getCustomerSummary httpClient host token)
                        match outcome with
                        | Ok summary ->
                            SettingsStore.deleteSetting conn "audible_last_error"
                            return Ok (sprintf "Connected as %s (%s) — %s" authFile.CustomerName authFile.LocaleCode summary)
                        | Error msg ->
                            if msg.StartsWith(Audible.authFileRejectedPrefix) then
                                SettingsStore.setSetting conn "audible_last_error" msg
                            return Error msg
                with ex ->
                    return Error $"Audible connection test failed: {ex.Message}"
            }

            getAudibleMarketplace = fun () -> async {
                use conn = factory ()
                return SettingsStore.getSetting conn "audible_marketplace" |> Option.defaultValue "de"
            }

            setAudibleMarketplace = fun marketplace -> async {
                use conn = factory ()
                SettingsStore.setSetting conn "audible_marketplace" marketplace
                return ()
            }

            searchAudibleBooks = fun query -> async {
                try
                    let config = getAudibleConfig ()
                    let host = Audible.marketplaceHost config.Marketplace
                    return! Audible.searchCatalog httpClient host query
                with _ -> return []
            }

            addBookFromAudible = fun request -> async {
                use conn = factory ()
                return! addBookFromAudibleImpl conn httpClient getAudibleConfig imageBasePath projectionHandlers request
            }

            // Audible library import + daily progress sync (integration-jjvg2,
            // ADR-0074/ADR-0076/ADR-0026).
            importAudibleLibrary = fun () -> async {
                use conn = factory ()
                return! importAudibleLibraryImpl conn httpClient getAudibleConfig imageBasePath projectionHandlers
            }

            runAudibleProgressSync = fun () -> runAudibleProgressSyncNow ()

            getAudibleSyncStatus = fun () -> async {
                use conn = factory ()
                return {
                    LastImportResult = SettingsStore.getSetting conn "audible_last_import_result"
                    LastSync = SettingsStore.getSetting conn "audible_last_sync"
                    LastSyncResult = SettingsStore.getSetting conn "audible_last_sync_result"
                }
            }
        }
