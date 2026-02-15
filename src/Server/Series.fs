namespace Mediatheca.Server

open Thoth.Json.Net

module Series =

    // Data records for events

    type EpisodeImportData = {
        EpisodeNumber: int
        Name: string
        Overview: string
        Runtime: int option
        AirDate: string option
        StillRef: string option
        TmdbRating: float option
    }

    type SeasonImportData = {
        SeasonNumber: int
        Name: string
        Overview: string
        PosterRef: string option
        AirDate: string option
        Episodes: EpisodeImportData list
    }

    type SeriesAddedData = {
        Name: string
        Year: int
        Overview: string
        Genres: string list
        Status: string
        PosterRef: string option
        BackdropRef: string option
        TmdbId: int
        TmdbRating: float option
        EpisodeRuntime: int option
        Seasons: SeasonImportData list
    }

    type RewatchSessionCreatedData = {
        RewatchId: string
        Name: string option
        FriendSlugs: string list
    }

    type RewatchSessionFriendData = {
        RewatchId: string
        FriendSlug: string
    }

    type EpisodeWatchedData = {
        RewatchId: string
        SeasonNumber: int
        EpisodeNumber: int
        Date: string
    }

    type EpisodeUnwatchedData = {
        RewatchId: string
        SeasonNumber: int
        EpisodeNumber: int
    }

    type SeasonMarkedWatchedData = {
        RewatchId: string
        SeasonNumber: int
        Date: string
    }

    type EpisodesWatchedUpToData = {
        RewatchId: string
        SeasonNumber: int
        EpisodeNumber: int
        Date: string
    }

    type SeasonMarkedUnwatchedData = {
        RewatchId: string
        SeasonNumber: int
    }

    type EpisodeWatchedDateChangedData = {
        RewatchId: string
        SeasonNumber: int
        EpisodeNumber: int
        Date: string
    }

    // Events

    type SeriesEvent =
        | Series_added_to_library of SeriesAddedData
        | Series_removed_from_library
        | Series_categorized of genres: string list
        | Series_poster_replaced of posterRef: string
        | Series_backdrop_replaced of backdropRef: string
        | Series_recommended_by of friendSlug: string
        | Series_recommendation_removed of friendSlug: string
        | Series_want_to_watch_with of friendSlug: string
        | Series_removed_want_to_watch_with of friendSlug: string
        | Series_personal_rating_set of rating: int option
        | Rewatch_session_created of RewatchSessionCreatedData
        | Rewatch_session_removed of rewatchId: string
        | Rewatch_session_friend_added of RewatchSessionFriendData
        | Rewatch_session_friend_removed of RewatchSessionFriendData
        | Episode_watched of EpisodeWatchedData
        | Episode_unwatched of EpisodeUnwatchedData
        | Season_marked_watched of SeasonMarkedWatchedData
        | Episodes_watched_up_to of EpisodesWatchedUpToData
        | Season_marked_unwatched of SeasonMarkedUnwatchedData
        | Episode_watched_date_changed of EpisodeWatchedDateChangedData

    // State

    type EpisodeState = {
        EpisodeNumber: int
        Name: string
        Overview: string
        Runtime: int option
        AirDate: string option
        StillRef: string option
        TmdbRating: float option
    }

    type SeasonState = {
        SeasonNumber: int
        Name: string
        Overview: string
        PosterRef: string option
        AirDate: string option
        Episodes: Map<int, EpisodeState>
    }

    type RewatchSessionState = {
        RewatchId: string
        Name: string option
        IsDefault: bool
        Friends: Set<string>
        WatchedEpisodes: Set<int * int>
    }

    type ActiveSeries = {
        Name: string
        Year: int
        Overview: string
        Genres: string list
        Status: string
        PosterRef: string option
        BackdropRef: string option
        TmdbId: int
        TmdbRating: float option
        EpisodeRuntime: int option
        PersonalRating: int option
        Seasons: Map<int, SeasonState>
        RecommendedBy: Set<string>
        WantToWatchWith: Set<string>
        RewatchSessions: Map<string, RewatchSessionState>
    }

    type SeriesState =
        | Not_created
        | Active of ActiveSeries
        | Removed

    // Commands

    type SeriesCommand =
        | Add_series_to_library of SeriesAddedData
        | Remove_series
        | Categorize_series of genres: string list
        | Replace_series_poster of posterRef: string
        | Replace_series_backdrop of backdropRef: string
        | Recommend_series of friendSlug: string
        | Remove_series_recommendation of friendSlug: string
        | Want_to_watch_series_with of friendSlug: string
        | Remove_want_to_watch_series_with of friendSlug: string
        | Set_series_personal_rating of rating: int option
        | Create_rewatch_session of RewatchSessionCreatedData
        | Remove_rewatch_session of rewatchId: string
        | Add_friend_to_rewatch_session of RewatchSessionFriendData
        | Remove_friend_from_rewatch_session of RewatchSessionFriendData
        | Mark_episode_watched of EpisodeWatchedData
        | Mark_episode_unwatched of EpisodeUnwatchedData
        | Mark_season_watched of SeasonMarkedWatchedData
        | Mark_episodes_watched_up_to of EpisodesWatchedUpToData
        | Mark_season_unwatched of SeasonMarkedUnwatchedData
        | Change_episode_watched_date of EpisodeWatchedDateChangedData

    // Evolve

    let evolve (state: SeriesState) (event: SeriesEvent) : SeriesState =
        match state, event with
        | Not_created, Series_added_to_library data ->
            let seasons =
                data.Seasons
                |> List.map (fun s ->
                    s.SeasonNumber,
                    {
                        SeasonNumber = s.SeasonNumber
                        Name = s.Name
                        Overview = s.Overview
                        PosterRef = s.PosterRef
                        AirDate = s.AirDate
                        Episodes =
                            s.Episodes
                            |> List.map (fun e ->
                                e.EpisodeNumber,
                                {
                                    EpisodeNumber = e.EpisodeNumber
                                    Name = e.Name
                                    Overview = e.Overview
                                    Runtime = e.Runtime
                                    AirDate = e.AirDate
                                    StillRef = e.StillRef
                                    TmdbRating = e.TmdbRating
                                })
                            |> Map.ofList
                    })
                |> Map.ofList
            let defaultSession = {
                RewatchId = "default"
                Name = None
                IsDefault = true
                Friends = Set.empty
                WatchedEpisodes = Set.empty
            }
            Active {
                Name = data.Name
                Year = data.Year
                Overview = data.Overview
                Genres = data.Genres
                Status = data.Status
                PosterRef = data.PosterRef
                BackdropRef = data.BackdropRef
                TmdbId = data.TmdbId
                TmdbRating = data.TmdbRating
                EpisodeRuntime = data.EpisodeRuntime
                PersonalRating = None
                Seasons = seasons
                RecommendedBy = Set.empty
                WantToWatchWith = Set.empty
                RewatchSessions = Map.ofList [ "default", defaultSession ]
            }
        | Active _, Series_removed_from_library -> Removed
        | Active series, Series_categorized genres ->
            Active { series with Genres = genres }
        | Active series, Series_poster_replaced posterRef ->
            Active { series with PosterRef = Some posterRef }
        | Active series, Series_backdrop_replaced backdropRef ->
            Active { series with BackdropRef = Some backdropRef }
        | Active series, Series_recommended_by friendSlug ->
            Active { series with RecommendedBy = series.RecommendedBy |> Set.add friendSlug }
        | Active series, Series_recommendation_removed friendSlug ->
            Active { series with RecommendedBy = series.RecommendedBy |> Set.remove friendSlug }
        | Active series, Series_want_to_watch_with friendSlug ->
            Active { series with WantToWatchWith = series.WantToWatchWith |> Set.add friendSlug }
        | Active series, Series_removed_want_to_watch_with friendSlug ->
            Active { series with WantToWatchWith = series.WantToWatchWith |> Set.remove friendSlug }
        | Active series, Series_personal_rating_set rating ->
            Active { series with PersonalRating = rating }
        | Active series, Rewatch_session_created data ->
            let session = {
                RewatchId = data.RewatchId
                Name = data.Name
                IsDefault = false
                Friends = data.FriendSlugs |> Set.ofList
                WatchedEpisodes = Set.empty
            }
            Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId session }
        | Active series, Rewatch_session_removed rewatchId ->
            Active { series with RewatchSessions = series.RewatchSessions |> Map.remove rewatchId }
        | Active series, Rewatch_session_friend_added data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with Friends = session.Friends |> Set.add data.FriendSlug } }
            | None -> state
        | Active series, Rewatch_session_friend_removed data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with Friends = session.Friends |> Set.remove data.FriendSlug } }
            | None -> state
        | Active series, Episode_watched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                let watched = session.WatchedEpisodes |> Set.add (data.SeasonNumber, data.EpisodeNumber)
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with WatchedEpisodes = watched } }
            | None -> state
        | Active series, Episode_unwatched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                let watched = session.WatchedEpisodes |> Set.remove (data.SeasonNumber, data.EpisodeNumber)
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with WatchedEpisodes = watched } }
            | None -> state
        | Active series, Season_marked_watched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                match series.Seasons |> Map.tryFind data.SeasonNumber with
                | Some season' ->
                    let episodePairs =
                        season'.Episodes
                        |> Map.toList
                        |> List.map (fun (epNum, _) -> (data.SeasonNumber, epNum))
                        |> Set.ofList
                    let watched = Set.union session.WatchedEpisodes episodePairs
                    Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with WatchedEpisodes = watched } }
                | None -> state
            | None -> state
        | Active series, Episodes_watched_up_to data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                let episodePairs =
                    [ 1 .. data.EpisodeNumber ]
                    |> List.map (fun epNum -> (data.SeasonNumber, epNum))
                    |> Set.ofList
                let watched = Set.union session.WatchedEpisodes episodePairs
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with WatchedEpisodes = watched } }
            | None -> state
        | Active series, Season_marked_unwatched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                let seasonEpisodes =
                    session.WatchedEpisodes
                    |> Set.filter (fun (sn, _) -> sn = data.SeasonNumber)
                let watched = Set.difference session.WatchedEpisodes seasonEpisodes
                Active { series with RewatchSessions = series.RewatchSessions |> Map.add data.RewatchId { session with WatchedEpisodes = watched } }
            | None -> state
        | Active _, Episode_watched_date_changed _ ->
            state // Dates are projection-only; aggregate only tracks watched/unwatched
        | _ -> state

    let reconstitute (events: SeriesEvent list) : SeriesState =
        List.fold evolve Not_created events

    // Decide

    let decide (state: SeriesState) (command: SeriesCommand) : Result<SeriesEvent list, string> =
        match state, command with
        | Not_created, Add_series_to_library data ->
            Ok [ Series_added_to_library data ]
        | Active _, Add_series_to_library _ ->
            Error "Series already exists in library"
        | Active _, Remove_series ->
            Ok [ Series_removed_from_library ]
        | Not_created, Remove_series ->
            Error "Series does not exist"
        | Active series, Categorize_series genres ->
            if series.Genres = genres then Ok []
            else Ok [ Series_categorized genres ]
        | Active _, Replace_series_poster posterRef ->
            Ok [ Series_poster_replaced posterRef ]
        | Active _, Replace_series_backdrop backdropRef ->
            Ok [ Series_backdrop_replaced backdropRef ]
        | Active series, Recommend_series friendSlug ->
            if series.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ Series_recommended_by friendSlug ]
        | Active series, Remove_series_recommendation friendSlug ->
            if series.RecommendedBy |> Set.contains friendSlug then
                Ok [ Series_recommendation_removed friendSlug ]
            else Ok []
        | Active series, Want_to_watch_series_with friendSlug ->
            if series.WantToWatchWith |> Set.contains friendSlug then Ok []
            else Ok [ Series_want_to_watch_with friendSlug ]
        | Active series, Remove_want_to_watch_series_with friendSlug ->
            if series.WantToWatchWith |> Set.contains friendSlug then
                Ok [ Series_removed_want_to_watch_with friendSlug ]
            else Ok []
        | Active series, Set_series_personal_rating rating ->
            if series.PersonalRating = rating then Ok []
            else Ok [ Series_personal_rating_set rating ]
        | Active series, Create_rewatch_session data ->
            if series.RewatchSessions |> Map.containsKey data.RewatchId then
                Error "Rewatch session already exists"
            else
                Ok [ Rewatch_session_created data ]
        | Active series, Remove_rewatch_session rewatchId ->
            if series.RewatchSessions |> Map.containsKey rewatchId then
                match series.RewatchSessions |> Map.tryFind rewatchId with
                | Some session when session.IsDefault ->
                    Error "Cannot remove the default rewatch session"
                | _ -> Ok [ Rewatch_session_removed rewatchId ]
            else Error "Rewatch session does not exist"
        | Active series, Add_friend_to_rewatch_session data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                if session.Friends |> Set.contains data.FriendSlug then Ok []
                else Ok [ Rewatch_session_friend_added data ]
            | None -> Error "Rewatch session does not exist"
        | Active series, Remove_friend_from_rewatch_session data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                if session.Friends |> Set.contains data.FriendSlug then
                    Ok [ Rewatch_session_friend_removed data ]
                else Ok []
            | None -> Error "Rewatch session does not exist"
        | Active series, Mark_episode_watched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                if session.WatchedEpisodes |> Set.contains (data.SeasonNumber, data.EpisodeNumber) then Ok []
                else Ok [ Episode_watched data ]
            | None -> Error "Rewatch session does not exist"
        | Active series, Mark_episode_unwatched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                if session.WatchedEpisodes |> Set.contains (data.SeasonNumber, data.EpisodeNumber) then
                    Ok [ Episode_unwatched data ]
                else Ok []
            | None -> Error "Rewatch session does not exist"
        | Active series, Mark_season_watched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some _ ->
                match series.Seasons |> Map.tryFind data.SeasonNumber with
                | Some _ -> Ok [ Season_marked_watched data ]
                | None -> Error "Season does not exist"
            | None -> Error "Rewatch session does not exist"
        | Active series, Mark_episodes_watched_up_to data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some _ ->
                match series.Seasons |> Map.tryFind data.SeasonNumber with
                | Some _ -> Ok [ Episodes_watched_up_to data ]
                | None -> Error "Season does not exist"
            | None -> Error "Rewatch session does not exist"
        | Active series, Mark_season_unwatched data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some _ ->
                match series.Seasons |> Map.tryFind data.SeasonNumber with
                | Some _ -> Ok [ Season_marked_unwatched data ]
                | None -> Error "Season does not exist"
            | None -> Error "Rewatch session does not exist"
        | Active series, Change_episode_watched_date data ->
            match series.RewatchSessions |> Map.tryFind data.RewatchId with
            | Some session ->
                if session.WatchedEpisodes |> Set.contains (data.SeasonNumber, data.EpisodeNumber) then
                    Ok [ Episode_watched_date_changed data ]
                else Error "Episode is not watched"
            | None -> Error "Rewatch session does not exist"
        | Removed, _ ->
            Error "Series has been removed"
        | Not_created, _ ->
            Error "Series does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Series-%s" slug

    // Serialization

    module Serialization =

        let private encodeEpisodeImportData (data: EpisodeImportData) =
            Encode.object [
                "episodeNumber", Encode.int data.EpisodeNumber
                "name", Encode.string data.Name
                "overview", Encode.string data.Overview
                "runtime", Encode.option Encode.int data.Runtime
                "airDate", Encode.option Encode.string data.AirDate
                "stillRef", Encode.option Encode.string data.StillRef
                "tmdbRating", Encode.option Encode.float data.TmdbRating
            ]

        let private decodeEpisodeImportData: Decoder<EpisodeImportData> =
            Decode.object (fun get -> {
                EpisodeNumber = get.Required.Field "episodeNumber" Decode.int
                Name = get.Required.Field "name" Decode.string
                Overview = get.Required.Field "overview" Decode.string
                Runtime = get.Optional.Field "runtime" Decode.int
                AirDate = get.Optional.Field "airDate" Decode.string
                StillRef = get.Optional.Field "stillRef" Decode.string
                TmdbRating = get.Optional.Field "tmdbRating" Decode.float
            })

        let private encodeSeasonImportData (data: SeasonImportData) =
            Encode.object [
                "seasonNumber", Encode.int data.SeasonNumber
                "name", Encode.string data.Name
                "overview", Encode.string data.Overview
                "posterRef", Encode.option Encode.string data.PosterRef
                "airDate", Encode.option Encode.string data.AirDate
                "episodes", data.Episodes |> List.map encodeEpisodeImportData |> Encode.list
            ]

        let private decodeSeasonImportData: Decoder<SeasonImportData> =
            Decode.object (fun get -> {
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                Name = get.Required.Field "name" Decode.string
                Overview = get.Required.Field "overview" Decode.string
                PosterRef = get.Optional.Field "posterRef" Decode.string
                AirDate = get.Optional.Field "airDate" Decode.string
                Episodes = get.Required.Field "episodes" (Decode.list decodeEpisodeImportData)
            })

        let private encodeSeriesAddedData (data: SeriesAddedData) =
            Encode.object [
                "name", Encode.string data.Name
                "year", Encode.int data.Year
                "overview", Encode.string data.Overview
                "genres", data.Genres |> List.map Encode.string |> Encode.list
                "status", Encode.string data.Status
                "posterRef", Encode.option Encode.string data.PosterRef
                "backdropRef", Encode.option Encode.string data.BackdropRef
                "tmdbId", Encode.int data.TmdbId
                "tmdbRating", Encode.option Encode.float data.TmdbRating
                "episodeRuntime", Encode.option Encode.int data.EpisodeRuntime
                "seasons", data.Seasons |> List.map encodeSeasonImportData |> Encode.list
            ]

        let private decodeSeriesAddedData: Decoder<SeriesAddedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Year = get.Required.Field "year" Decode.int
                Overview = get.Required.Field "overview" Decode.string
                Genres = get.Required.Field "genres" (Decode.list Decode.string)
                Status = get.Required.Field "status" Decode.string
                PosterRef = get.Optional.Field "posterRef" Decode.string
                BackdropRef = get.Optional.Field "backdropRef" Decode.string
                TmdbId = get.Required.Field "tmdbId" Decode.int
                TmdbRating = get.Optional.Field "tmdbRating" Decode.float
                EpisodeRuntime = get.Optional.Field "episodeRuntime" Decode.int
                Seasons = get.Required.Field "seasons" (Decode.list decodeSeasonImportData)
            })

        let private encodeRewatchSessionCreatedData (data: RewatchSessionCreatedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "name", Encode.option Encode.string data.Name
                "friendSlugs", data.FriendSlugs |> List.map Encode.string |> Encode.list
            ]

        let private decodeRewatchSessionCreatedData: Decoder<RewatchSessionCreatedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                Name = get.Optional.Field "name" Decode.string
                FriendSlugs = get.Required.Field "friendSlugs" (Decode.list Decode.string)
            })

        let private encodeRewatchSessionFriendData (data: RewatchSessionFriendData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "friendSlug", Encode.string data.FriendSlug
            ]

        let private decodeRewatchSessionFriendData: Decoder<RewatchSessionFriendData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                FriendSlug = get.Required.Field "friendSlug" Decode.string
            })

        let private encodeEpisodeWatchedData (data: EpisodeWatchedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
                "episodeNumber", Encode.int data.EpisodeNumber
                "date", Encode.string data.Date
            ]

        let private decodeEpisodeWatchedData: Decoder<EpisodeWatchedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                EpisodeNumber = get.Required.Field "episodeNumber" Decode.int
                Date = get.Required.Field "date" Decode.string
            })

        let private encodeEpisodeUnwatchedData (data: EpisodeUnwatchedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
                "episodeNumber", Encode.int data.EpisodeNumber
            ]

        let private decodeEpisodeUnwatchedData: Decoder<EpisodeUnwatchedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                EpisodeNumber = get.Required.Field "episodeNumber" Decode.int
            })

        let private encodeSeasonMarkedWatchedData (data: SeasonMarkedWatchedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
                "date", Encode.string data.Date
            ]

        let private decodeSeasonMarkedWatchedData: Decoder<SeasonMarkedWatchedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                Date = get.Required.Field "date" Decode.string
            })

        let private encodeEpisodesWatchedUpToData (data: EpisodesWatchedUpToData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
                "episodeNumber", Encode.int data.EpisodeNumber
                "date", Encode.string data.Date
            ]

        let private decodeEpisodesWatchedUpToData: Decoder<EpisodesWatchedUpToData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                EpisodeNumber = get.Required.Field "episodeNumber" Decode.int
                Date = get.Required.Field "date" Decode.string
            })

        let private encodeSeasonMarkedUnwatchedData (data: SeasonMarkedUnwatchedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
            ]

        let private decodeSeasonMarkedUnwatchedData: Decoder<SeasonMarkedUnwatchedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
            })

        let private encodeEpisodeWatchedDateChangedData (data: EpisodeWatchedDateChangedData) =
            Encode.object [
                "rewatchId", Encode.string data.RewatchId
                "seasonNumber", Encode.int data.SeasonNumber
                "episodeNumber", Encode.int data.EpisodeNumber
                "date", Encode.string data.Date
            ]

        let private decodeEpisodeWatchedDateChangedData: Decoder<EpisodeWatchedDateChangedData> =
            Decode.object (fun get -> {
                RewatchId = get.Required.Field "rewatchId" Decode.string
                SeasonNumber = get.Required.Field "seasonNumber" Decode.int
                EpisodeNumber = get.Required.Field "episodeNumber" Decode.int
                Date = get.Required.Field "date" Decode.string
            })

        let serialize (event: SeriesEvent) : string * string =
            match event with
            | Series_added_to_library data ->
                "Series_added_to_library", Encode.toString 0 (encodeSeriesAddedData data)
            | Series_removed_from_library ->
                "Series_removed_from_library", "{}"
            | Series_categorized genres ->
                "Series_categorized", Encode.toString 0 (Encode.object [ "genres", genres |> List.map Encode.string |> Encode.list ])
            | Series_poster_replaced posterRef ->
                "Series_poster_replaced", Encode.toString 0 (Encode.object [ "posterRef", Encode.string posterRef ])
            | Series_backdrop_replaced backdropRef ->
                "Series_backdrop_replaced", Encode.toString 0 (Encode.object [ "backdropRef", Encode.string backdropRef ])
            | Series_recommended_by friendSlug ->
                "Series_recommended_by", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Series_recommendation_removed friendSlug ->
                "Series_recommendation_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Series_want_to_watch_with friendSlug ->
                "Series_want_to_watch_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Series_removed_want_to_watch_with friendSlug ->
                "Series_removed_want_to_watch_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Series_personal_rating_set rating ->
                "Series_personal_rating_set", Encode.toString 0 (Encode.object [ "rating", Encode.option Encode.int rating ])
            | Rewatch_session_created data ->
                "Rewatch_session_created", Encode.toString 0 (encodeRewatchSessionCreatedData data)
            | Rewatch_session_removed rewatchId ->
                "Rewatch_session_removed", Encode.toString 0 (Encode.object [ "rewatchId", Encode.string rewatchId ])
            | Rewatch_session_friend_added data ->
                "Rewatch_session_friend_added", Encode.toString 0 (encodeRewatchSessionFriendData data)
            | Rewatch_session_friend_removed data ->
                "Rewatch_session_friend_removed", Encode.toString 0 (encodeRewatchSessionFriendData data)
            | Episode_watched data ->
                "Episode_watched", Encode.toString 0 (encodeEpisodeWatchedData data)
            | Episode_unwatched data ->
                "Episode_unwatched", Encode.toString 0 (encodeEpisodeUnwatchedData data)
            | Season_marked_watched data ->
                "Season_marked_watched", Encode.toString 0 (encodeSeasonMarkedWatchedData data)
            | Episodes_watched_up_to data ->
                "Episodes_watched_up_to", Encode.toString 0 (encodeEpisodesWatchedUpToData data)
            | Season_marked_unwatched data ->
                "Season_marked_unwatched", Encode.toString 0 (encodeSeasonMarkedUnwatchedData data)
            | Episode_watched_date_changed data ->
                "Episode_watched_date_changed", Encode.toString 0 (encodeEpisodeWatchedDateChangedData data)

        let deserialize (eventType: string) (data: string) : SeriesEvent option =
            match eventType with
            | "Series_added_to_library" ->
                Decode.fromString decodeSeriesAddedData data
                |> Result.toOption
                |> Option.map Series_added_to_library
            | "Series_removed_from_library" ->
                Some Series_removed_from_library
            | "Series_categorized" ->
                Decode.fromString (Decode.field "genres" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map Series_categorized
            | "Series_poster_replaced" ->
                Decode.fromString (Decode.field "posterRef" Decode.string) data
                |> Result.toOption
                |> Option.map Series_poster_replaced
            | "Series_backdrop_replaced" ->
                Decode.fromString (Decode.field "backdropRef" Decode.string) data
                |> Result.toOption
                |> Option.map Series_backdrop_replaced
            | "Series_recommended_by" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Series_recommended_by
            | "Series_recommendation_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Series_recommendation_removed
            | "Series_want_to_watch_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Series_want_to_watch_with
            | "Series_removed_want_to_watch_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Series_removed_want_to_watch_with
            | "Series_personal_rating_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "rating" Decode.int)) data
                |> Result.toOption
                |> Option.map Series_personal_rating_set
            | "Rewatch_session_created" ->
                Decode.fromString decodeRewatchSessionCreatedData data
                |> Result.toOption
                |> Option.map Rewatch_session_created
            | "Rewatch_session_removed" ->
                Decode.fromString (Decode.field "rewatchId" Decode.string) data
                |> Result.toOption
                |> Option.map Rewatch_session_removed
            | "Rewatch_session_friend_added" ->
                Decode.fromString decodeRewatchSessionFriendData data
                |> Result.toOption
                |> Option.map Rewatch_session_friend_added
            | "Rewatch_session_friend_removed" ->
                Decode.fromString decodeRewatchSessionFriendData data
                |> Result.toOption
                |> Option.map Rewatch_session_friend_removed
            | "Episode_watched" ->
                Decode.fromString decodeEpisodeWatchedData data
                |> Result.toOption
                |> Option.map Episode_watched
            | "Episode_unwatched" ->
                Decode.fromString decodeEpisodeUnwatchedData data
                |> Result.toOption
                |> Option.map Episode_unwatched
            | "Season_marked_watched" ->
                Decode.fromString decodeSeasonMarkedWatchedData data
                |> Result.toOption
                |> Option.map Season_marked_watched
            | "Episodes_watched_up_to" ->
                Decode.fromString decodeEpisodesWatchedUpToData data
                |> Result.toOption
                |> Option.map Episodes_watched_up_to
            | "Season_marked_unwatched" ->
                Decode.fromString decodeSeasonMarkedUnwatchedData data
                |> Result.toOption
                |> Option.map Season_marked_unwatched
            | "Episode_watched_date_changed" ->
                Decode.fromString decodeEpisodeWatchedDateChangedData data
                |> Result.toOption
                |> Option.map Episode_watched_date_changed
            | _ -> None

        let toEventData (event: SeriesEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : SeriesEvent option =
            deserialize storedEvent.EventType storedEvent.Data
