namespace Mediatheca.Server

open System.Data
open System.Net.Http
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

module Api =

    let private executeCommand
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

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    let create
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : IMediathecaApi =

        let movieProjections = projectionHandlers
        let friendProjections = projectionHandlers

        {
            healthCheck = fun () -> async { return "Mediatheca is running" }

            searchLibrary = fun query -> async {
                return MovieProjection.search conn query
            }

            searchTmdb = fun query -> async {
                return! Tmdb.searchMovies httpClient (getTmdbConfig()) query
            }

            addMovie = fun tmdbId -> async {
                try
                    // 1. Fetch TMDB details + credits
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

                    // 2. Generate unique slug
                    let baseSlug = Slug.movieSlug details.Title year
                    let slug = generateUniqueSlug conn Movies.streamId baseSlug
                    let sid = Movies.streamId slug

                    // 3. Download poster + backdrop
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

                    // 4. Execute command
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
                        // 5. Insert cast into CastStore
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

                        return Ok slug
                with ex ->
                    return Error $"Failed to add movie: {ex.Message}"
            }

            removeMovie = fun slug -> async {
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
                    // Clean up cast and images
                    CastStore.removeMovieCastAndCleanup conn imageBasePath sid
                    ImageStore.deleteImage imageBasePath (sprintf "posters/%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/%s.jpg" slug)
                    return Ok ()
                | Error e -> return Error e
            }

            getMovie = fun slug -> async {
                return MovieProjection.getBySlug conn slug
            }

            getMovies = fun () -> async {
                return MovieProjection.getAll conn
            }

            categorizeMovie = fun slug genres -> async {
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

            // Watch Sessions
            recordWatchSession = fun slug request -> async {
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

            getWatchSessions = fun slug -> async {
                return MovieProjection.getWatchSessions conn slug
            }

            // Content Blocks
            addContentBlock = fun slug sessionId request -> async {
                let sid = ContentBlocks.streamId slug
                let blockId = System.Guid.NewGuid().ToString("N")
                let blockData: ContentBlocks.ContentBlockData = {
                    BlockId = blockId
                    BlockType = request.BlockType
                    Content = request.Content
                    ImageRef = request.ImageRef
                    Url = request.Url
                    Caption = request.Caption
                }
                let result =
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Add_content_block (blockData, sessionId))
                        projectionHandlers
                match result with
                | Ok () -> return Ok blockId
                | Error e -> return Error e
            }

            updateContentBlock = fun slug blockId request -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Update_content_block (blockId, request.Content, request.ImageRef, request.Url, request.Caption))
                        projectionHandlers
            }

            removeContentBlock = fun slug blockId -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Remove_content_block blockId)
                        projectionHandlers
            }

            reorderContentBlocks = fun slug sessionId blockIds -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Reorder_content_blocks (blockIds, sessionId))
                        projectionHandlers
            }

            getContentBlocks = fun slug sessionId -> async {
                match sessionId with
                | Some sid -> return ContentBlockProjection.getBySession conn slug sid
                | None -> return ContentBlockProjection.getForMovieDetail conn slug
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

            // Catalogs
            createCatalog = fun request -> async {
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
                return CatalogProjection.getBySlug conn slug
            }

            getCatalogs = fun () -> async {
                return CatalogProjection.getAll conn
            }

            addCatalogEntry = fun slug request -> async {
                let sid = Catalogs.streamId slug
                let entryId = System.Guid.NewGuid().ToString("N")
                let data: Catalogs.EntryAddedData = {
                    EntryId = entryId
                    MovieSlug = request.MovieSlug
                    Note = request.Note
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

            // Dashboard
            getDashboardStats = fun () -> async {
                let movieCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_list"
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
                return {
                    Mediatheca.Shared.DashboardStats.MovieCount = movieCount
                    FriendCount = friendCount
                    CatalogCount = catalogCount
                    WatchSessionCount = watchSessionCount
                    TotalWatchTimeMinutes = totalWatchTime
                }
            }

            getRecentActivity = fun count -> async {
                let events = EventStore.getRecentEvents conn count
                return events |> List.map (fun e ->
                    let description =
                        match e.EventType with
                        | "Movie_added_to_library" -> "Movie added to library"
                        | "Movie_removed_from_library" -> "Movie removed from library"
                        | "Watch_session_recorded" -> "Watch session recorded"
                        | "Friend_added" -> "Friend added"
                        | "Friend_removed" -> "Friend removed"
                        | "Catalog_created" -> "Catalog created"
                        | "Catalog_removed" -> "Catalog removed"
                        | "Entry_added" -> "Entry added to catalog"
                        | "Content_block_added" -> "Content block added"
                        | other -> other.Replace("_", " ")
                    { Mediatheca.Shared.RecentActivityItem.Timestamp = e.Timestamp.ToString("o")
                      StreamId = e.StreamId
                      EventType = e.EventType
                      Description = description }
                )
            }

            // Event Store Browser
            getEvents = fun query -> async {
                let events = EventStore.queryEvents conn query.StreamFilter query.EventTypeFilter query.Limit query.Offset
                return events |> List.map (fun e ->
                    { Mediatheca.Shared.EventDto.GlobalPosition = e.GlobalPosition
                      StreamId = e.StreamId
                      StreamPosition = e.StreamPosition
                      EventType = e.EventType
                      Data = e.Data
                      Timestamp = e.Timestamp.ToString("o") }
                )
            }

            getEventStreams = fun () -> async {
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                return EventStore.getDistinctEventTypes conn
            }

            addFriend = fun name -> async {
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
                let sid = Friends.streamId slug
                return
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        Friends.Remove_friend
                        friendProjections
            }

            getFriend = fun slug -> async {
                return FriendProjection.getBySlug conn slug
            }

            getFriends = fun () -> async {
                return FriendProjection.getAll conn
            }

            uploadFriendImage = fun slug data filename -> async {
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

            getTmdbApiKey = fun () -> async {
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
                    let! results = Tmdb.searchMovies httpClient testConfig "test"
                    return Ok ()
                with ex ->
                    return Error $"TMDB API key validation failed: {ex.Message}"
            }
        }
