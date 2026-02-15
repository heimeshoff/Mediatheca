namespace Mediatheca.Server

open System
open System.Data
open System.IO
open System.Net.Http
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

module CinemarcoImport =

    type ImportCounters = {
        mutable Friends: int
        mutable Movies: int
        mutable Series: int
        mutable EpisodesWatched: int
        mutable Catalogs: int
        mutable ContentBlocks: int
        mutable Images: int
        mutable Errors: ResizeArray<string>
    }

    let private appendEvents
        (conn: SqliteConnection)
        (streamId: string)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (events: EventStore.EventData list)
        =
        let currentPos =
            match streamPositions.TryGetValue(streamId) with
            | true, pos -> pos
            | false, _ -> -1L
        match EventStore.appendToStream conn streamId currentPos events with
        | EventStore.Success _ ->
            streamPositions.[streamId] <- currentPos + (int64 events.Length)
        | EventStore.ConcurrencyConflict _ ->
            failwithf "Concurrency conflict on stream %s" streamId

    let private copyImage (srcBase: string) (srcRelative: string) (dstBase: string) (dstRelative: string) (counters: ImportCounters) =
        let src = Path.Combine(srcBase, srcRelative)
        let dst = Path.Combine(dstBase, dstRelative)
        if File.Exists(src) then
            let dir = Path.GetDirectoryName(dst)
            if not (Directory.Exists(dir)) then
                Directory.CreateDirectory(dir) |> ignore
            File.Copy(src, dst, true)
            counters.Images <- counters.Images + 1

    let private readNullableString (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None
        else Some (rd.ReadString col)

    let private readNullableInt (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None
        else Some (rd.ReadInt32 col)

    let private readNullableFloat (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None
        else Some (rd.ReadDouble col)

    let private readNullableInt64 (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None
        else Some (rd.ReadInt64 col)

    let private parseYear (dateStr: string option) =
        match dateStr with
        | Some d when d.Length >= 4 ->
            match Int32.TryParse(d.[0..3]) with
            | true, y -> y
            | _ -> 0
        | _ -> 0

    let private parseGenres (genresJson: string option) =
        match genresJson with
        | None | Some "" | Some "null" -> []
        | Some json ->
            try
                // Cinemarco stores genres as JSON array of strings: ["Action","Comedy"]
                let decoded = Thoth.Json.Net.Decode.fromString (Thoth.Json.Net.Decode.list Thoth.Json.Net.Decode.string) json
                match decoded with
                | Ok genres -> genres
                | Error _ -> []
            with _ -> []

    // Step 1: Import Friends
    let private importFriends
        (cmConn: SqliteConnection)
        (conn: SqliteConnection)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (cmImagesPath: string)
        (imageBasePath: string)
        (counters: ImportCounters)
        =
        let friendIdToSlug = System.Collections.Generic.Dictionary<int64, string>()

        let friends =
            cmConn
            |> Db.newCommand "SELECT id, name, avatar_url FROM friends"
            |> Db.query (fun rd ->
                rd.ReadInt64 "id",
                rd.ReadString "name",
                readNullableString rd "avatar_url")

        for (id, name, avatarUrl) in friends do
            try
                let slug = Slug.friendSlug name
                friendIdToSlug.[id] <- slug

                // Copy avatar image
                let imageRef =
                    match avatarUrl with
                    | Some avatar when avatar <> "" ->
                        let ref = sprintf "friends/%s.jpg" slug
                        copyImage cmImagesPath (sprintf "profiles/%s" avatar) imageBasePath ref counters
                        Some ref
                    | _ -> None

                let event = Friends.Friend_added { Name = name; ImageRef = imageRef }
                let eventData = Friends.Serialization.toEventData event
                let sid = Friends.streamId slug
                appendEvents conn sid streamPositions [ eventData ]
                counters.Friends <- counters.Friends + 1
            with ex ->
                counters.Errors.Add(sprintf "Friend '%s': %s" name ex.Message)

        friendIdToSlug

    // Step 2: Import Movies
    let private importMovies
        (cmConn: SqliteConnection)
        (conn: SqliteConnection)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (friendIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (cmImagesPath: string)
        (imageBasePath: string)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (counters: ImportCounters)
        =
        // Build entry_id -> slug map for later (content blocks, catalogs)
        let entryIdToSlug = System.Collections.Generic.Dictionary<int64, string>()

        let movies =
            cmConn
            |> Db.newCommand """
                SELECT m.id as movie_id, m.tmdb_id, m.title, m.overview, m.release_date,
                       m.runtime_minutes, m.poster_path, m.backdrop_path, m.genres, m.vote_average,
                       le.id as entry_id, le.personal_rating, le.why_recommended_by_friend_id,
                       le.notes, le.abandoned_reason
                FROM library_entries le
                JOIN movies m ON le.movie_id = m.id
                WHERE le.media_type = 'Movie'
            """
            |> Db.query (fun rd ->
                {| MovieId = rd.ReadInt64 "movie_id"
                   TmdbId = rd.ReadInt32 "tmdb_id"
                   Title = rd.ReadString "title"
                   Overview = readNullableString rd "overview" |> Option.defaultValue ""
                   ReleaseDate = readNullableString rd "release_date"
                   Runtime = readNullableInt rd "runtime_minutes"
                   PosterPath = readNullableString rd "poster_path"
                   BackdropPath = readNullableString rd "backdrop_path"
                   Genres = readNullableString rd "genres"
                   VoteAverage = readNullableFloat rd "vote_average"
                   EntryId = rd.ReadInt64 "entry_id"
                   PersonalRating = readNullableInt rd "personal_rating"
                   RecommendedByFriendId = readNullableInt64 rd "why_recommended_by_friend_id"
                   Notes = readNullableString rd "notes"
                   AbandonedReason = readNullableString rd "abandoned_reason" |})

        for m in movies do
            try
                let year = parseYear m.ReleaseDate
                let slug = Slug.movieSlug m.Title year
                let sid = Movies.streamId slug
                entryIdToSlug.[m.EntryId] <- slug

                // Copy images
                let posterRef =
                    match m.PosterPath with
                    | Some p when p <> "" ->
                        let ref = sprintf "posters/%s.jpg" slug
                        copyImage cmImagesPath (sprintf "posters/%s" p) imageBasePath ref counters
                        Some ref
                    | _ -> None

                let backdropRef =
                    match m.BackdropPath with
                    | Some p when p <> "" ->
                        let ref = sprintf "backdrops/%s.jpg" slug
                        copyImage cmImagesPath (sprintf "backdrops/%s" p) imageBasePath ref counters
                        Some ref
                    | _ -> None

                // 1. Movie_added_to_library
                let movieData: Movies.MovieAddedData = {
                    Name = m.Title
                    Year = year
                    Runtime = m.Runtime
                    Overview = m.Overview
                    Genres = parseGenres m.Genres
                    PosterRef = posterRef
                    BackdropRef = backdropRef
                    TmdbId = m.TmdbId
                    TmdbRating = m.VoteAverage
                }
                let events = ResizeArray<EventStore.EventData>()
                events.Add(Movies.Serialization.toEventData (Movies.Movie_added_to_library movieData))

                // 2. Personal_rating_set
                match m.PersonalRating with
                | Some rating ->
                    events.Add(Movies.Serialization.toEventData (Movies.Personal_rating_set (Some rating)))
                | None -> ()

                // 3. Movie_recommended_by
                match m.RecommendedByFriendId with
                | Some friendId ->
                    match friendIdToSlug.TryGetValue(friendId) with
                    | true, friendSlug ->
                        events.Add(Movies.Serialization.toEventData (Movies.Movie_recommended_by friendSlug))
                    | _ -> ()
                | None -> ()

                // 4. Watch sessions
                let sessions =
                    cmConn
                    |> Db.newCommand """
                        SELECT mws.id, mws.watched_date
                        FROM movie_watch_sessions mws
                        WHERE mws.entry_id = @entry_id
                        ORDER BY mws.watched_date
                    """
                    |> Db.setParams [ "entry_id", SqlType.Int64 m.EntryId ]
                    |> Db.query (fun rd ->
                        rd.ReadInt64 "id",
                        rd.ReadString "watched_date")

                for (sessionId, watchedDate) in sessions do
                    let sessionFriends =
                        cmConn
                        |> Db.newCommand "SELECT friend_id FROM movie_session_friends WHERE session_id = @session_id"
                        |> Db.setParams [ "session_id", SqlType.Int64 sessionId ]
                        |> Db.query (fun rd -> rd.ReadInt64 "friend_id")
                        |> List.choose (fun fid ->
                            match friendIdToSlug.TryGetValue(fid) with
                            | true, slug -> Some slug
                            | _ -> None)

                    let wsData: Movies.WatchSessionRecordedData = {
                        SessionId = Guid.NewGuid().ToString("N")
                        Date = watchedDate
                        Duration = m.Runtime
                        FriendSlugs = sessionFriends
                    }
                    events.Add(Movies.Serialization.toEventData (Movies.Watch_session_recorded wsData))

                appendEvents conn sid streamPositions (events |> Seq.toList)

                // Fetch cast from TMDB
                try
                    let credits =
                        Tmdb.getMovieCredits httpClient (getTmdbConfig()) m.TmdbId
                        |> Async.RunSynchronously
                    let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                    for castMember in topBilled do
                        let castImageRef =
                            match castMember.ProfilePath with
                            | Some p ->
                                let ref = sprintf "cast/%d.jpg" castMember.Id
                                let destPath = Path.Combine(imageBasePath, ref)
                                if not (ImageStore.imageExists imageBasePath ref) then
                                    try
                                        Tmdb.downloadImage httpClient (getTmdbConfig()) p "w185" destPath
                                        |> Async.RunSynchronously
                                    with _ -> ()
                                Some ref
                            | None -> None
                        let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                        CastStore.addMovieCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)
                with castEx ->
                    counters.Errors.Add(sprintf "Movie cast '%s': %s" m.Title castEx.Message)

                counters.Movies <- counters.Movies + 1
            with ex ->
                counters.Errors.Add(sprintf "Movie '%s': %s" m.Title ex.Message)

        entryIdToSlug

    // Step 3: Import Series
    let private importSeries
        (cmConn: SqliteConnection)
        (conn: SqliteConnection)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (friendIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (cmImagesPath: string)
        (imageBasePath: string)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (counters: ImportCounters)
        =
        let entryIdToSlug = System.Collections.Generic.Dictionary<int64, string>()
        // Also need series_id -> slug for catalog collection_items with item_type='season'/'episode'
        let seriesIdToSlug = System.Collections.Generic.Dictionary<int64, string>()

        let seriesList =
            cmConn
            |> Db.newCommand """
                SELECT s.id as series_id, s.tmdb_id, s.name, s.overview, s.first_air_date,
                       s.poster_path, s.backdrop_path, s.genres, s.vote_average, s.status,
                       s.episode_runtime_minutes,
                       le.id as entry_id, le.personal_rating, le.why_recommended_by_friend_id,
                       le.watch_status, le.notes, le.abandoned_reason
                FROM library_entries le
                JOIN series s ON le.series_id = s.id
                WHERE le.media_type = 'Series'
            """
            |> Db.query (fun rd ->
                {| SeriesId = rd.ReadInt64 "series_id"
                   TmdbId = rd.ReadInt32 "tmdb_id"
                   Name = rd.ReadString "name"
                   Overview = readNullableString rd "overview" |> Option.defaultValue ""
                   FirstAirDate = readNullableString rd "first_air_date"
                   PosterPath = readNullableString rd "poster_path"
                   BackdropPath = readNullableString rd "backdrop_path"
                   Genres = readNullableString rd "genres"
                   VoteAverage = readNullableFloat rd "vote_average"
                   Status = readNullableString rd "status" |> Option.defaultValue ""
                   EpisodeRuntime = readNullableInt rd "episode_runtime_minutes"
                   EntryId = rd.ReadInt64 "entry_id"
                   PersonalRating = readNullableInt rd "personal_rating"
                   RecommendedByFriendId = readNullableInt64 rd "why_recommended_by_friend_id"
                   WatchStatus = rd.ReadString "watch_status"
                   Notes = readNullableString rd "notes"
                   AbandonedReason = readNullableString rd "abandoned_reason" |})

        for s in seriesList do
            try
                let year = parseYear s.FirstAirDate
                let slug = Slug.seriesSlug s.Name year
                let sid = Series.streamId slug
                entryIdToSlug.[s.EntryId] <- slug
                seriesIdToSlug.[s.SeriesId] <- slug

                // Read seasons
                let seasons =
                    cmConn
                    |> Db.newCommand """
                        SELECT season_number, name, overview, poster_path, air_date
                        FROM seasons WHERE series_id = @series_id AND season_number > 0
                        ORDER BY season_number
                    """
                    |> Db.setParams [ "series_id", SqlType.Int64 s.SeriesId ]
                    |> Db.query (fun rd ->
                        {| SeasonNumber = rd.ReadInt32 "season_number"
                           Name = readNullableString rd "name" |> Option.defaultValue ""
                           Overview = readNullableString rd "overview" |> Option.defaultValue ""
                           PosterPath = readNullableString rd "poster_path"
                           AirDate = readNullableString rd "air_date" |})

                // Read all episodes for this series
                let episodes =
                    cmConn
                    |> Db.newCommand """
                        SELECT season_number, episode_number, name, overview, air_date, runtime_minutes, still_path
                        FROM episodes WHERE series_id = @series_id AND season_number > 0
                        ORDER BY season_number, episode_number
                    """
                    |> Db.setParams [ "series_id", SqlType.Int64 s.SeriesId ]
                    |> Db.query (fun rd ->
                        {| SeasonNumber = rd.ReadInt32 "season_number"
                           EpisodeNumber = rd.ReadInt32 "episode_number"
                           Name = rd.ReadString "name"
                           Overview = readNullableString rd "overview" |> Option.defaultValue ""
                           AirDate = readNullableString rd "air_date"
                           Runtime = readNullableInt rd "runtime_minutes"
                           StillPath = readNullableString rd "still_path" |})

                let episodesBySeason = episodes |> List.groupBy (fun e -> e.SeasonNumber) |> Map.ofList

                // Copy images and build season data
                let posterRef =
                    match s.PosterPath with
                    | Some p when p <> "" ->
                        let ref = sprintf "posters/series-%s.jpg" slug
                        copyImage cmImagesPath (sprintf "posters/%s" p) imageBasePath ref counters
                        Some ref
                    | _ -> None

                let backdropRef =
                    match s.BackdropPath with
                    | Some p when p <> "" ->
                        let ref = sprintf "backdrops/series-%s.jpg" slug
                        copyImage cmImagesPath (sprintf "backdrops/%s" p) imageBasePath ref counters
                        Some ref
                    | _ -> None

                let seasonImportData =
                    seasons |> List.map (fun sn ->
                        // Copy season poster
                        let snPosterRef =
                            match sn.PosterPath with
                            | Some p when p <> "" ->
                                let ref = sprintf "posters/series-%s-s%02d.jpg" slug sn.SeasonNumber
                                copyImage cmImagesPath (sprintf "posters/%s" p) imageBasePath ref counters
                                Some ref
                            | _ -> None

                        let eps = episodesBySeason |> Map.tryFind sn.SeasonNumber |> Option.defaultValue []

                        let episodeData =
                            eps |> List.map (fun ep ->
                                // Copy episode still
                                let stillRef =
                                    match ep.StillPath with
                                    | Some p when p <> "" ->
                                        let ref = sprintf "stills/%s-s%02de%02d.jpg" slug ep.SeasonNumber ep.EpisodeNumber
                                        copyImage cmImagesPath (sprintf "stills/%s" p) imageBasePath ref counters
                                        Some ref
                                    | _ -> None

                                let epData : Series.EpisodeImportData = {
                                    EpisodeNumber = ep.EpisodeNumber
                                    Name = ep.Name
                                    Overview = ep.Overview
                                    Runtime = ep.Runtime
                                    AirDate = ep.AirDate
                                    StillRef = stillRef
                                    TmdbRating = None
                                }
                                epData)

                        let snData : Series.SeasonImportData = {
                            SeasonNumber = sn.SeasonNumber
                            Name = sn.Name
                            Overview = sn.Overview
                            PosterRef = snPosterRef
                            AirDate = sn.AirDate
                            Episodes = episodeData
                        }
                        snData)

                let mapStatus (status: string) =
                    match status.ToLowerInvariant() with
                    | "returning series" | "returning" -> "Returning"
                    | "ended" -> "Ended"
                    | "canceled" | "cancelled" -> "Canceled"
                    | "in production" -> "InProduction"
                    | "planned" -> "Planned"
                    | _ -> "UnknownStatus"

                // 1. Series_added_to_library
                let seriesData: Series.SeriesAddedData = {
                    Name = s.Name
                    Year = year
                    Overview = s.Overview
                    Genres = parseGenres s.Genres
                    Status = mapStatus s.Status
                    PosterRef = posterRef
                    BackdropRef = backdropRef
                    TmdbId = s.TmdbId
                    TmdbRating = s.VoteAverage
                    EpisodeRuntime = s.EpisodeRuntime
                    Seasons = seasonImportData
                }

                let events = ResizeArray<EventStore.EventData>()
                events.Add(Series.Serialization.toEventData (Series.Series_added_to_library seriesData))

                // 2. Series_personal_rating_set
                match s.PersonalRating with
                | Some rating ->
                    events.Add(Series.Serialization.toEventData (Series.Series_personal_rating_set (Some rating)))
                | None -> ()

                // 3. Series_recommended_by
                match s.RecommendedByFriendId with
                | Some friendId ->
                    match friendIdToSlug.TryGetValue(friendId) with
                    | true, friendSlug ->
                        events.Add(Series.Serialization.toEventData (Series.Series_recommended_by friendSlug))
                    | _ -> ()
                | None -> ()

                // 4. Series_abandoned
                if s.WatchStatus = "Abandoned" then
                    events.Add(Series.Serialization.toEventData Series.Series_abandoned)

                // 5+6. Rewatch sessions (non-default)
                let watchSessions =
                    cmConn
                    |> Db.newCommand """
                        SELECT id, name, is_default
                        FROM watch_sessions
                        WHERE entry_id = @entry_id
                        ORDER BY is_default DESC, id
                    """
                    |> Db.setParams [ "entry_id", SqlType.Int64 s.EntryId ]
                    |> Db.query (fun rd ->
                        {| Id = rd.ReadInt64 "id"
                           Name = rd.ReadString "name"
                           IsDefault = rd.ReadInt32 "is_default" = 1 |})

                // Map Cinemarco session IDs to Mediatheca rewatch IDs
                let sessionIdToRewatchId = System.Collections.Generic.Dictionary<int64, string>()

                for ws in watchSessions do
                    if ws.IsDefault then
                        sessionIdToRewatchId.[ws.Id] <- "default"
                    else
                        let rewatchId = Guid.NewGuid().ToString("N")
                        sessionIdToRewatchId.[ws.Id] <- rewatchId

                        // Get friends for this session
                        let sessionFriendSlugs =
                            cmConn
                            |> Db.newCommand "SELECT friend_id FROM session_friends WHERE session_id = @session_id"
                            |> Db.setParams [ "session_id", SqlType.Int64 ws.Id ]
                            |> Db.query (fun rd -> rd.ReadInt64 "friend_id")
                            |> List.choose (fun fid ->
                                match friendIdToSlug.TryGetValue(fid) with
                                | true, slug -> Some slug
                                | _ -> None)

                        let rwData: Series.RewatchSessionCreatedData = {
                            RewatchId = rewatchId
                            Name = Some ws.Name
                            FriendSlugs = sessionFriendSlugs
                        }
                        events.Add(Series.Serialization.toEventData (Series.Rewatch_session_created rwData))

                // Add friends to default session from session_friends AND entry_friends
                let defaultSessionFriends = ResizeArray<string>()
                for ws in watchSessions do
                    if ws.IsDefault then
                        let sfriends =
                            cmConn
                            |> Db.newCommand "SELECT friend_id FROM session_friends WHERE session_id = @session_id"
                            |> Db.setParams [ "session_id", SqlType.Int64 ws.Id ]
                            |> Db.query (fun rd -> rd.ReadInt64 "friend_id")
                            |> List.choose (fun fid ->
                                match friendIdToSlug.TryGetValue(fid) with
                                | true, slug -> Some slug
                                | _ -> None)
                        for slug in sfriends do
                            defaultSessionFriends.Add(slug)

                // Also read entry-level friends (entry_friends table)
                let entryFriends =
                    cmConn
                    |> Db.newCommand "SELECT friend_id FROM entry_friends WHERE entry_id = @entry_id"
                    |> Db.setParams [ "entry_id", SqlType.Int64 s.EntryId ]
                    |> Db.query (fun rd -> rd.ReadInt64 "friend_id")
                    |> List.choose (fun fid ->
                        match friendIdToSlug.TryGetValue(fid) with
                        | true, slug -> Some slug
                        | _ -> None)
                for slug in entryFriends do
                    defaultSessionFriends.Add(slug)

                // Deduplicate and emit events
                let uniqueDefaultFriends = defaultSessionFriends |> Seq.distinct |> Seq.toList
                for friendSlug in uniqueDefaultFriends do
                    events.Add(Series.Serialization.toEventData (
                        Series.Rewatch_session_friend_added { RewatchId = "default"; FriendSlug = friendSlug }))

                // 7. Episode progress
                let episodeProgress =
                    cmConn
                    |> Db.newCommand """
                        SELECT ep.session_id, ep.season_number, ep.episode_number, ep.watched_date
                        FROM episode_progress ep
                        WHERE ep.entry_id = @entry_id AND ep.is_watched = 1
                        ORDER BY ep.session_id, ep.season_number, ep.episode_number
                    """
                    |> Db.setParams [ "entry_id", SqlType.Int64 s.EntryId ]
                    |> Db.query (fun rd ->
                        {| SessionId = rd.ReadInt64 "session_id"
                           SeasonNumber = rd.ReadInt32 "season_number"
                           EpisodeNumber = rd.ReadInt32 "episode_number"
                           WatchedDate = readNullableString rd "watched_date" |})

                for ep in episodeProgress do
                    match sessionIdToRewatchId.TryGetValue(ep.SessionId) with
                    | true, rewatchId ->
                        let epData: Series.EpisodeWatchedData = {
                            RewatchId = rewatchId
                            SeasonNumber = ep.SeasonNumber
                            EpisodeNumber = ep.EpisodeNumber
                            Date = ep.WatchedDate |> Option.defaultValue (DateTime.UtcNow.ToString("yyyy-MM-dd"))
                        }
                        events.Add(Series.Serialization.toEventData (Series.Episode_watched epData))
                        counters.EpisodesWatched <- counters.EpisodesWatched + 1
                    | _ -> ()

                appendEvents conn sid streamPositions (events |> Seq.toList)

                // Fetch cast from TMDB
                try
                    let creditsResult =
                        Tmdb.getTvSeriesCredits httpClient (getTmdbConfig()) s.TmdbId
                        |> Async.RunSynchronously
                    match creditsResult with
                    | Ok credits ->
                        let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                        for castMember in topBilled do
                            let castImageRef =
                                match castMember.ProfilePath with
                                | Some p ->
                                    let ref = sprintf "cast/%d.jpg" castMember.Id
                                    let destPath = Path.Combine(imageBasePath, ref)
                                    if not (ImageStore.imageExists imageBasePath ref) then
                                        try
                                            Tmdb.downloadImage httpClient (getTmdbConfig()) p "w185" destPath
                                            |> Async.RunSynchronously
                                        with _ -> ()
                                    Some ref
                                | None -> None
                            let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                            CastStore.addSeriesCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)
                    | Error _ -> ()
                with castEx ->
                    counters.Errors.Add(sprintf "Series cast '%s': %s" s.Name castEx.Message)

                counters.Series <- counters.Series + 1
            with ex ->
                counters.Errors.Add(sprintf "Series '%s': %s" s.Name ex.Message)

        entryIdToSlug, seriesIdToSlug

    // Step 4: Import Content Blocks
    let private importContentBlocks
        (cmConn: SqliteConnection)
        (conn: SqliteConnection)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (movieEntryIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (seriesEntryIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (counters: ImportCounters)
        =
        // Combine both maps
        let allEntries =
            cmConn
            |> Db.newCommand """
                SELECT id, media_type, notes, abandoned_reason
                FROM library_entries
                WHERE (notes IS NOT NULL AND notes != '') OR (abandoned_reason IS NOT NULL AND abandoned_reason != '')
            """
            |> Db.query (fun rd ->
                {| EntryId = rd.ReadInt64 "id"
                   MediaType = rd.ReadString "media_type"
                   Notes = readNullableString rd "notes"
                   AbandonedReason = readNullableString rd "abandoned_reason" |})

        for entry in allEntries do
            try
                let slugOpt =
                    match entry.MediaType with
                    | "Movie" ->
                        match movieEntryIdToSlug.TryGetValue(entry.EntryId) with
                        | true, slug -> Some slug
                        | _ -> None
                    | "Series" ->
                        match seriesEntryIdToSlug.TryGetValue(entry.EntryId) with
                        | true, slug -> Some slug
                        | _ -> None
                    | _ -> None

                match slugOpt with
                | Some slug ->
                    let sid = ContentBlocks.streamId slug
                    let events = ResizeArray<EventStore.EventData>()

                    match entry.Notes with
                    | Some notes when notes <> "" ->
                        let blockData: ContentBlocks.ContentBlockData = {
                            BlockId = Guid.NewGuid().ToString("N")
                            BlockType = "text"
                            Content = notes
                            ImageRef = None
                            Url = None
                            Caption = None
                        }
                        events.Add(ContentBlocks.Serialization.toEventData (
                            ContentBlocks.Content_block_added (blockData, 0, None)))
                        counters.ContentBlocks <- counters.ContentBlocks + 1
                    | _ -> ()

                    match entry.AbandonedReason with
                    | Some reason when reason <> "" ->
                        let blockData: ContentBlocks.ContentBlockData = {
                            BlockId = Guid.NewGuid().ToString("N")
                            BlockType = "text"
                            Content = sprintf "Abandoned: %s" reason
                            ImageRef = None
                            Url = None
                            Caption = None
                        }
                        let pos = if events.Count > 0 then 1 else 0
                        events.Add(ContentBlocks.Serialization.toEventData (
                            ContentBlocks.Content_block_added (blockData, pos, None)))
                        counters.ContentBlocks <- counters.ContentBlocks + 1
                    | _ -> ()

                    if events.Count > 0 then
                        appendEvents conn sid streamPositions (events |> Seq.toList)
                | None -> ()
            with ex ->
                counters.Errors.Add(sprintf "ContentBlock entry %d: %s" entry.EntryId ex.Message)

    // Step 5: Import Catalogs
    let private importCatalogs
        (cmConn: SqliteConnection)
        (conn: SqliteConnection)
        (streamPositions: System.Collections.Generic.Dictionary<string, int64>)
        (movieEntryIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (seriesEntryIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (seriesIdToSlug: System.Collections.Generic.Dictionary<int64, string>)
        (counters: ImportCounters)
        =
        let collections =
            cmConn
            |> Db.newCommand "SELECT id, name, description FROM collections ORDER BY id"
            |> Db.query (fun rd ->
                {| Id = rd.ReadInt64 "id"
                   Name = rd.ReadString "name"
                   Description = readNullableString rd "description" |> Option.defaultValue "" |})

        for coll in collections do
            try
                let catalogSlug = Slug.catalogSlug coll.Name
                let sid = Catalogs.streamId catalogSlug

                let events = ResizeArray<EventStore.EventData>()

                // Catalog_created
                let createData: Catalogs.CatalogCreatedData = {
                    Name = coll.Name
                    Description = coll.Description
                    IsSorted = true
                }
                events.Add(Catalogs.Serialization.toEventData (Catalogs.Catalog_created createData))

                // Get items
                let items =
                    cmConn
                    |> Db.newCommand """
                        SELECT item_type, entry_id, series_id, season_number, episode_number, position, notes
                        FROM collection_items
                        WHERE collection_id = @collection_id
                        ORDER BY position
                    """
                    |> Db.setParams [ "collection_id", SqlType.Int64 coll.Id ]
                    |> Db.query (fun rd ->
                        {| ItemType = rd.ReadString "item_type"
                           EntryId = readNullableInt64 rd "entry_id"
                           SeriesId = readNullableInt64 rd "series_id"
                           SeasonNumber = readNullableInt rd "season_number"
                           EpisodeNumber = readNullableInt rd "episode_number"
                           Position = rd.ReadInt32 "position"
                           Notes = readNullableString rd "notes" |})

                for item in items do
                    let movieSlugOpt =
                        match item.ItemType with
                        | "entry" ->
                            match item.EntryId with
                            | Some entryId ->
                                // Try movie first, then series
                                match movieEntryIdToSlug.TryGetValue(entryId) with
                                | true, slug -> Some slug
                                | _ ->
                                    match seriesEntryIdToSlug.TryGetValue(entryId) with
                                    | true, slug -> Some slug
                                    | _ -> None
                            | None -> None
                        | "season" ->
                            match item.SeriesId, item.SeasonNumber with
                            | Some seriesId, Some seasonNum ->
                                match seriesIdToSlug.TryGetValue(seriesId) with
                                | true, slug -> Some (sprintf "%s:s%d" slug seasonNum)
                                | _ -> None
                            | _ -> None
                        | "episode" ->
                            match item.SeriesId, item.SeasonNumber, item.EpisodeNumber with
                            | Some seriesId, Some seasonNum, Some epNum ->
                                match seriesIdToSlug.TryGetValue(seriesId) with
                                | true, slug -> Some (sprintf "%s:s%de%d" slug seasonNum epNum)
                                | _ -> None
                            | _ -> None
                        | _ -> None

                    match movieSlugOpt with
                    | Some movieSlug ->
                        let entryData: Catalogs.EntryAddedData = {
                            EntryId = Guid.NewGuid().ToString("N")
                            MovieSlug = movieSlug
                            Note = item.Notes
                        }
                        events.Add(Catalogs.Serialization.toEventData (
                            Catalogs.Entry_added (entryData, item.Position)))
                    | None -> ()

                appendEvents conn sid streamPositions (events |> Seq.toList)
                counters.Catalogs <- counters.Catalogs + 1
            with ex ->
                counters.Errors.Add(sprintf "Catalog '%s': %s" coll.Name ex.Message)

    // Main import function
    let runImport
        (conn: SqliteConnection)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (request: ImportFromCinemarcoRequest)
        : Result<ImportResult, string> =

        // Pre-check: abort if Mediatheca event store is not empty
        let eventCount = EventStore.getTotalEventCount conn
        if eventCount > 0 then
            Error (sprintf "Mediatheca event store is not empty (%d events). Import requires a fresh database." eventCount)
        else

        // Validate Cinemarco paths
        if not (File.Exists(request.DatabasePath)) then
            Error (sprintf "Cinemarco database not found: %s" request.DatabasePath)
        else

        let cmImagesPath = request.ImagesPath
        if not (Directory.Exists(cmImagesPath)) then
            Error (sprintf "Cinemarco images directory not found: %s" cmImagesPath)
        else

        try
            // Open Cinemarco DB read-only
            let cmConnStr = sprintf "Data Source=%s;Mode=ReadOnly" request.DatabasePath
            use cmConn = new SqliteConnection(cmConnStr)
            cmConn.Open()

            let counters = {
                Friends = 0; Movies = 0; Series = 0; EpisodesWatched = 0
                Catalogs = 0; ContentBlocks = 0; Images = 0
                Errors = ResizeArray<string>()
            }

            let streamPositions = System.Collections.Generic.Dictionary<string, int64>()

            // Step 1: Friends
            let friendIdToSlug = importFriends cmConn conn streamPositions cmImagesPath imageBasePath counters

            // Step 2: Movies
            let movieEntryIdToSlug = importMovies cmConn conn streamPositions friendIdToSlug cmImagesPath imageBasePath httpClient getTmdbConfig counters

            // Step 3: Series
            let seriesEntryIdToSlug, seriesIdToSlug = importSeries cmConn conn streamPositions friendIdToSlug cmImagesPath imageBasePath httpClient getTmdbConfig counters

            // Step 4: Content Blocks
            importContentBlocks cmConn conn streamPositions movieEntryIdToSlug seriesEntryIdToSlug counters

            // Step 5: Catalogs
            importCatalogs cmConn conn streamPositions movieEntryIdToSlug seriesEntryIdToSlug seriesIdToSlug counters

            // Step 6: Rebuild all projections
            for handler in projectionHandlers do
                Projection.rebuildProjection conn handler

            Ok {
                FriendsImported = counters.Friends
                MoviesImported = counters.Movies
                SeriesImported = counters.Series
                EpisodesWatched = counters.EpisodesWatched
                CatalogsImported = counters.Catalogs
                ContentBlocksImported = counters.ContentBlocks
                ImagesCopied = counters.Images
                Errors = counters.Errors |> Seq.toList
            }
        with ex ->
            Error (sprintf "Import failed: %s" ex.Message)
