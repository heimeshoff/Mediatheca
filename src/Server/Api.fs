namespace Mediatheca.Server

open System.Net.Http
open Microsoft.Data.Sqlite
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

    let private generateUniqueSlug (conn: SqliteConnection) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (Catalog.streamId slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    let create
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (tmdbConfig: Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : IMediathecaApi =

        let movieProjections = projectionHandlers
        let friendProjections = projectionHandlers

        {
            healthCheck = fun () -> async { return "Mediatheca is running" }

            searchTmdb = fun query -> async {
                return! Tmdb.searchMovies httpClient tmdbConfig query
            }

            addMovie = fun tmdbId -> async {
                try
                    // 1. Fetch TMDB details + credits
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
                    let slug = generateUniqueSlug conn baseSlug
                    let sid = Catalog.streamId slug

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
                    let movieData: Catalog.MovieAddedData = {
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
                            Catalog.Serialization.fromStoredEvent
                            Catalog.reconstitute
                            Catalog.decide
                            Catalog.Serialization.toEventData
                            (Catalog.AddMovieToLibrary movieData)
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
                let sid = Catalog.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        Catalog.RemoveMovieFromLibrary
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
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.CategorizeMovie genres)
                        movieProjections
            }

            replacePoster = fun slug posterRef -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.ReplacePoster posterRef)
                        movieProjections
            }

            replaceBackdrop = fun slug backdropRef -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.ReplaceBackdrop backdropRef)
                        movieProjections
            }

            recommendMovie = fun slug friendSlug -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.RecommendBy friendSlug)
                        movieProjections
            }

            removeRecommendation = fun slug friendSlug -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.RemoveRecommendation friendSlug)
                        movieProjections
            }

            wantToWatchWith = fun slug friendSlug -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.AddWantToWatchWith friendSlug)
                        movieProjections
            }

            removeWantToWatchWith = fun slug friendSlug -> async {
                let sid = Catalog.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalog.Serialization.fromStoredEvent
                        Catalog.reconstitute
                        Catalog.decide
                        Catalog.Serialization.toEventData
                        (Catalog.RemoveFromWantToWatchWith friendSlug)
                        movieProjections
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
                        (Friends.AddFriend (name, None))
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
                        (Friends.UpdateFriend (name, imageRef))
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
                        Friends.RemoveFriend
                        friendProjections
            }

            getFriend = fun slug -> async {
                return FriendProjection.getBySlug conn slug
            }

            getFriends = fun () -> async {
                return FriendProjection.getAll conn
            }
        }
