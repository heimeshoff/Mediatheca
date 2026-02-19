namespace Mediatheca.Server

open Thoth.Json.Net

module Movies =

    // Data records for events

    type MovieAddedData = {
        Name: string
        Year: int
        Runtime: int option
        Overview: string
        Genres: string list
        PosterRef: string option
        BackdropRef: string option
        TmdbId: int
        TmdbRating: float option
    }

    type WatchSessionRecordedData = {
        SessionId: string
        Date: string
        Duration: int option
        FriendSlugs: string list
    }

    // Events

    type MovieEvent =
        | Movie_added_to_library of MovieAddedData
        | Movie_removed_from_library
        | Movie_categorized of genres: string list
        | Movie_poster_replaced of posterRef: string
        | Movie_backdrop_replaced of backdropRef: string
        | Movie_recommended_by of friendSlug: string
        | Recommendation_removed of friendSlug: string
        | Want_to_watch_with of friendSlug: string
        | Removed_want_to_watch_with of friendSlug: string
        | Watch_session_recorded of WatchSessionRecordedData
        | Watch_session_date_changed of sessionId: string * date: string
        | Friend_added_to_watch_session of sessionId: string * friendSlug: string
        | Friend_removed_from_watch_session of sessionId: string * friendSlug: string
        | Watch_session_removed of sessionId: string
        | Personal_rating_set of rating: int option
        | Movie_in_focus_set
        | Movie_in_focus_cleared

    // State

    type WatchSessionState = {
        Date: string
        Duration: int option
        Friends: Set<string>
    }

    type ActiveMovie = {
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
        RecommendedBy: Set<string>
        Want_to_watch_with: Set<string>
        WatchSessions: Map<string, WatchSessionState>
        InFocus: bool
    }

    type MovieState =
        | Not_created
        | Active of ActiveMovie
        | Removed

    // Commands

    type MovieCommand =
        | Add_movie_to_library of MovieAddedData
        | Remove_movie_from_library
        | Categorize_movie of genres: string list
        | Replace_poster of posterRef: string
        | Replace_backdrop of backdropRef: string
        | Recommend_by of friendSlug: string
        | Remove_recommendation of friendSlug: string
        | Add_want_to_watch_with of friendSlug: string
        | Remove_from_want_to_watch_with of friendSlug: string
        | Record_watch_session of WatchSessionRecordedData
        | Change_watch_session_date of sessionId: string * date: string
        | Add_friend_to_watch_session of sessionId: string * friendSlug: string
        | Remove_friend_from_watch_session of sessionId: string * friendSlug: string
        | Remove_watch_session of sessionId: string
        | Set_personal_rating of rating: int option
        | Set_movie_in_focus
        | Clear_movie_in_focus

    // Evolve

    let evolve (state: MovieState) (event: MovieEvent) : MovieState =
        match state, event with
        | Not_created, Movie_added_to_library data ->
            Active {
                Name = data.Name
                Year = data.Year
                Runtime = data.Runtime
                Overview = data.Overview
                Genres = data.Genres
                PosterRef = data.PosterRef
                BackdropRef = data.BackdropRef
                TmdbId = data.TmdbId
                TmdbRating = data.TmdbRating
                PersonalRating = None
                RecommendedBy = Set.empty
                Want_to_watch_with = Set.empty
                WatchSessions = Map.empty
                InFocus = false
            }
        | Active _, Movie_removed_from_library -> Removed
        | Active movie, Movie_categorized genres ->
            Active { movie with Genres = genres }
        | Active movie, Movie_poster_replaced posterRef ->
            Active { movie with PosterRef = Some posterRef }
        | Active movie, Movie_backdrop_replaced backdropRef ->
            Active { movie with BackdropRef = Some backdropRef }
        | Active movie, Movie_recommended_by friendSlug ->
            Active { movie with RecommendedBy = movie.RecommendedBy |> Set.add friendSlug }
        | Active movie, Recommendation_removed friendSlug ->
            Active { movie with RecommendedBy = movie.RecommendedBy |> Set.remove friendSlug }
        | Active movie, Want_to_watch_with friendSlug ->
            Active { movie with Want_to_watch_with = movie.Want_to_watch_with |> Set.add friendSlug }
        | Active movie, Removed_want_to_watch_with friendSlug ->
            Active { movie with Want_to_watch_with = movie.Want_to_watch_with |> Set.remove friendSlug }
        | Active movie, Watch_session_recorded data ->
            let session = {
                Date = data.Date
                Duration = data.Duration
                Friends = data.FriendSlugs |> Set.ofList
            }
            let updatedWantToWatch =
                data.FriendSlugs |> List.fold (fun acc slug -> acc |> Set.remove slug) movie.Want_to_watch_with
            Active {
                movie with
                    WatchSessions = movie.WatchSessions |> Map.add data.SessionId session
                    Want_to_watch_with = updatedWantToWatch
            }
        | Active movie, Movie_in_focus_set ->
            Active { movie with InFocus = true }
        | Active movie, Movie_in_focus_cleared ->
            Active { movie with InFocus = false }
        | Active movie, Watch_session_date_changed (sessionId, date) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some session ->
                Active { movie with WatchSessions = movie.WatchSessions |> Map.add sessionId { session with Date = date } }
            | None -> state
        | Active movie, Friend_added_to_watch_session (sessionId, friendSlug) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some session ->
                Active {
                    movie with
                        WatchSessions = movie.WatchSessions |> Map.add sessionId { session with Friends = session.Friends |> Set.add friendSlug }
                        Want_to_watch_with = movie.Want_to_watch_with |> Set.remove friendSlug
                }
            | None -> state
        | Active movie, Friend_removed_from_watch_session (sessionId, friendSlug) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some session ->
                Active { movie with WatchSessions = movie.WatchSessions |> Map.add sessionId { session with Friends = session.Friends |> Set.remove friendSlug } }
            | None -> state
        | Active movie, Watch_session_removed sessionId ->
            Active { movie with WatchSessions = movie.WatchSessions |> Map.remove sessionId }
        | Active movie, Personal_rating_set rating ->
            Active { movie with PersonalRating = rating }
        | _ -> state

    let reconstitute (events: MovieEvent list) : MovieState =
        List.fold evolve Not_created events

    // Decide

    let decide (state: MovieState) (command: MovieCommand) : Result<MovieEvent list, string> =
        match state, command with
        | Not_created, Add_movie_to_library data ->
            Ok [ Movie_added_to_library data ]
        | Active _, Add_movie_to_library _ ->
            Error "Movie already exists in library"
        | Active movie, Remove_movie_from_library ->
            Ok [ Movie_removed_from_library ]
        | Not_created, Remove_movie_from_library ->
            Error "Movie does not exist"
        | Active movie, Categorize_movie genres ->
            if movie.Genres = genres then Ok []
            else Ok [ Movie_categorized genres ]
        | Active movie, Replace_poster posterRef ->
            Ok [ Movie_poster_replaced posterRef ]
        | Active movie, Replace_backdrop backdropRef ->
            Ok [ Movie_backdrop_replaced backdropRef ]
        | Active movie, Recommend_by friendSlug ->
            if movie.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ Movie_recommended_by friendSlug ]
        | Active movie, Remove_recommendation friendSlug ->
            if movie.RecommendedBy |> Set.contains friendSlug then
                Ok [ Recommendation_removed friendSlug ]
            else Ok []
        | Active movie, Add_want_to_watch_with friendSlug ->
            if movie.Want_to_watch_with |> Set.contains friendSlug then Ok []
            else Ok [ Want_to_watch_with friendSlug ]
        | Active movie, Remove_from_want_to_watch_with friendSlug ->
            if movie.Want_to_watch_with |> Set.contains friendSlug then
                Ok [ Removed_want_to_watch_with friendSlug ]
            else Ok []
        | Active movie, Record_watch_session data ->
            if movie.WatchSessions |> Map.containsKey data.SessionId then
                Error "Watch session already exists"
            else
                let events = [ Watch_session_recorded data ]
                let events = if movie.InFocus then events @ [ Movie_in_focus_cleared ] else events
                Ok events
        | Active movie, Change_watch_session_date (sessionId, date) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some _ -> Ok [ Watch_session_date_changed (sessionId, date) ]
            | None -> Error "Watch session does not exist"
        | Active movie, Add_friend_to_watch_session (sessionId, friendSlug) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some session ->
                if session.Friends |> Set.contains friendSlug then Ok []
                else Ok [ Friend_added_to_watch_session (sessionId, friendSlug) ]
            | None -> Error "Watch session does not exist"
        | Active movie, Remove_friend_from_watch_session (sessionId, friendSlug) ->
            match movie.WatchSessions |> Map.tryFind sessionId with
            | Some session ->
                if session.Friends |> Set.contains friendSlug then
                    Ok [ Friend_removed_from_watch_session (sessionId, friendSlug) ]
                else Ok []
            | None -> Error "Watch session does not exist"
        | Active movie, Remove_watch_session sessionId ->
            if movie.WatchSessions |> Map.containsKey sessionId then
                Ok [ Watch_session_removed sessionId ]
            else Error "Watch session does not exist"
        | Active movie, Set_personal_rating rating ->
            if movie.PersonalRating = rating then Ok []
            else Ok [ Personal_rating_set rating ]
        | Active movie, Set_movie_in_focus ->
            if movie.InFocus then Ok []
            else Ok [ Movie_in_focus_set ]
        | Active movie, Clear_movie_in_focus ->
            if movie.InFocus then Ok [ Movie_in_focus_cleared ]
            else Ok []
        | Removed, _ ->
            Error "Movie has been removed"
        | Not_created, _ ->
            Error "Movie does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Movie-%s" slug

    // Serialization

    module Serialization =

        let private encodeMovieAddedData (data: MovieAddedData) =
            Encode.object [
                "name", Encode.string data.Name
                "year", Encode.int data.Year
                "runtime", Encode.option Encode.int data.Runtime
                "overview", Encode.string data.Overview
                "genres", data.Genres |> List.map Encode.string |> Encode.list
                "posterRef", Encode.option Encode.string data.PosterRef
                "backdropRef", Encode.option Encode.string data.BackdropRef
                "tmdbId", Encode.int data.TmdbId
                "tmdbRating", Encode.option Encode.float data.TmdbRating
            ]

        let private decodeMovieAddedData: Decoder<MovieAddedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Year = get.Required.Field "year" Decode.int
                Runtime = get.Optional.Field "runtime" Decode.int
                Overview = get.Required.Field "overview" Decode.string
                Genres = get.Required.Field "genres" (Decode.list Decode.string)
                PosterRef = get.Optional.Field "posterRef" Decode.string
                BackdropRef = get.Optional.Field "backdropRef" Decode.string
                TmdbId = get.Required.Field "tmdbId" Decode.int
                TmdbRating = get.Optional.Field "tmdbRating" Decode.float
            })

        let private encodeWatchSessionRecordedData (data: WatchSessionRecordedData) =
            Encode.object [
                "sessionId", Encode.string data.SessionId
                "date", Encode.string data.Date
                "duration", Encode.option Encode.int data.Duration
                "friendSlugs", data.FriendSlugs |> List.map Encode.string |> Encode.list
            ]

        let private decodeWatchSessionRecordedData: Decoder<WatchSessionRecordedData> =
            Decode.object (fun get -> {
                SessionId = get.Required.Field "sessionId" Decode.string
                Date = get.Required.Field "date" Decode.string
                Duration = get.Optional.Field "duration" Decode.int
                FriendSlugs = get.Required.Field "friendSlugs" (Decode.list Decode.string)
            })

        let serialize (event: MovieEvent) : string * string =
            match event with
            | Movie_added_to_library data ->
                "Movie_added_to_library", Encode.toString 0 (encodeMovieAddedData data)
            | Movie_removed_from_library ->
                "Movie_removed_from_library", "{}"
            | Movie_categorized genres ->
                "Movie_categorized", Encode.toString 0 (Encode.object [ "genres", genres |> List.map Encode.string |> Encode.list ])
            | Movie_poster_replaced posterRef ->
                "Movie_poster_replaced", Encode.toString 0 (Encode.object [ "posterRef", Encode.string posterRef ])
            | Movie_backdrop_replaced backdropRef ->
                "Movie_backdrop_replaced", Encode.toString 0 (Encode.object [ "backdropRef", Encode.string backdropRef ])
            | Movie_recommended_by friendSlug ->
                "Movie_recommended_by", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Recommendation_removed friendSlug ->
                "Recommendation_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Want_to_watch_with friendSlug ->
                "Want_to_watch_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Removed_want_to_watch_with friendSlug ->
                "Removed_want_to_watch_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Watch_session_recorded data ->
                "Watch_session_recorded", Encode.toString 0 (encodeWatchSessionRecordedData data)
            | Watch_session_date_changed (sessionId, date) ->
                "Watch_session_date_changed", Encode.toString 0 (Encode.object [ "sessionId", Encode.string sessionId; "date", Encode.string date ])
            | Friend_added_to_watch_session (sessionId, friendSlug) ->
                "Friend_added_to_watch_session", Encode.toString 0 (Encode.object [ "sessionId", Encode.string sessionId; "friendSlug", Encode.string friendSlug ])
            | Friend_removed_from_watch_session (sessionId, friendSlug) ->
                "Friend_removed_from_watch_session", Encode.toString 0 (Encode.object [ "sessionId", Encode.string sessionId; "friendSlug", Encode.string friendSlug ])
            | Watch_session_removed sessionId ->
                "Watch_session_removed", Encode.toString 0 (Encode.object [ "sessionId", Encode.string sessionId ])
            | Personal_rating_set rating ->
                "Personal_rating_set", Encode.toString 0 (Encode.object [ "rating", Encode.option Encode.int rating ])
            | Movie_in_focus_set ->
                "Movie_in_focus_set", "{}"
            | Movie_in_focus_cleared ->
                "Movie_in_focus_cleared", "{}"

        let deserialize (eventType: string) (data: string) : MovieEvent option =
            match eventType with
            | "Movie_added_to_library" ->
                Decode.fromString decodeMovieAddedData data
                |> Result.toOption
                |> Option.map Movie_added_to_library
            | "Movie_removed_from_library" ->
                Some Movie_removed_from_library
            | "Movie_categorized" ->
                Decode.fromString (Decode.field "genres" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map Movie_categorized
            | "Movie_poster_replaced" ->
                Decode.fromString (Decode.field "posterRef" Decode.string) data
                |> Result.toOption
                |> Option.map Movie_poster_replaced
            | "Movie_backdrop_replaced" ->
                Decode.fromString (Decode.field "backdropRef" Decode.string) data
                |> Result.toOption
                |> Option.map Movie_backdrop_replaced
            | "Movie_recommended_by" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Movie_recommended_by
            | "Recommendation_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Recommendation_removed
            | "Want_to_watch_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Want_to_watch_with
            | "Removed_want_to_watch_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Removed_want_to_watch_with
            | "Watch_session_recorded" ->
                Decode.fromString decodeWatchSessionRecordedData data
                |> Result.toOption
                |> Option.map Watch_session_recorded
            | "Watch_session_date_changed" ->
                Decode.fromString (Decode.object (fun get ->
                    get.Required.Field "sessionId" Decode.string,
                    get.Required.Field "date" Decode.string)) data
                |> Result.toOption
                |> Option.map Watch_session_date_changed
            | "Friend_added_to_watch_session" ->
                Decode.fromString (Decode.object (fun get ->
                    get.Required.Field "sessionId" Decode.string,
                    get.Required.Field "friendSlug" Decode.string)) data
                |> Result.toOption
                |> Option.map Friend_added_to_watch_session
            | "Friend_removed_from_watch_session" ->
                Decode.fromString (Decode.object (fun get ->
                    get.Required.Field "sessionId" Decode.string,
                    get.Required.Field "friendSlug" Decode.string)) data
                |> Result.toOption
                |> Option.map Friend_removed_from_watch_session
            | "Watch_session_removed" ->
                Decode.fromString (Decode.field "sessionId" Decode.string) data
                |> Result.toOption
                |> Option.map Watch_session_removed
            | "Personal_rating_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "rating" Decode.int)) data
                |> Result.toOption
                |> Option.map Personal_rating_set
            | "Movie_in_focus_set" ->
                Some Movie_in_focus_set
            | "Movie_in_focus_cleared" ->
                Some Movie_in_focus_cleared
            | _ -> None

        let toEventData (event: MovieEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : MovieEvent option =
            deserialize storedEvent.EventType storedEvent.Data
