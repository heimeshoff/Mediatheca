namespace Mediatheca.Server

open Thoth.Json.Net

module Catalog =

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

    // Events

    type MovieEvent =
        | MovieAddedToLibrary of MovieAddedData
        | MovieRemovedFromLibrary
        | MovieCategorized of genres: string list
        | MoviePosterReplaced of posterRef: string
        | MovieBackdropReplaced of backdropRef: string
        | MovieRecommendedBy of friendSlug: string
        | RecommendationRemoved of friendSlug: string
        | WantToWatchWith of friendSlug: string
        | RemovedWantToWatchWith of friendSlug: string

    // State

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
        RecommendedBy: Set<string>
        WantToWatchWith: Set<string>
    }

    type MovieState =
        | NotCreated
        | Active of ActiveMovie
        | Removed

    // Commands

    type MovieCommand =
        | AddMovieToLibrary of MovieAddedData
        | RemoveMovieFromLibrary
        | CategorizeMovie of genres: string list
        | ReplacePoster of posterRef: string
        | ReplaceBackdrop of backdropRef: string
        | RecommendBy of friendSlug: string
        | RemoveRecommendation of friendSlug: string
        | AddWantToWatchWith of friendSlug: string
        | RemoveFromWantToWatchWith of friendSlug: string

    // Evolve

    let evolve (state: MovieState) (event: MovieEvent) : MovieState =
        match state, event with
        | NotCreated, MovieAddedToLibrary data ->
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
                RecommendedBy = Set.empty
                WantToWatchWith = Set.empty
            }
        | Active _, MovieRemovedFromLibrary -> Removed
        | Active movie, MovieCategorized genres ->
            Active { movie with Genres = genres }
        | Active movie, MoviePosterReplaced posterRef ->
            Active { movie with PosterRef = Some posterRef }
        | Active movie, MovieBackdropReplaced backdropRef ->
            Active { movie with BackdropRef = Some backdropRef }
        | Active movie, MovieRecommendedBy friendSlug ->
            Active { movie with RecommendedBy = movie.RecommendedBy |> Set.add friendSlug }
        | Active movie, RecommendationRemoved friendSlug ->
            Active { movie with RecommendedBy = movie.RecommendedBy |> Set.remove friendSlug }
        | Active movie, WantToWatchWith friendSlug ->
            Active { movie with WantToWatchWith = movie.WantToWatchWith |> Set.add friendSlug }
        | Active movie, RemovedWantToWatchWith friendSlug ->
            Active { movie with WantToWatchWith = movie.WantToWatchWith |> Set.remove friendSlug }
        | _ -> state

    let reconstitute (events: MovieEvent list) : MovieState =
        List.fold evolve NotCreated events

    // Decide

    let decide (state: MovieState) (command: MovieCommand) : Result<MovieEvent list, string> =
        match state, command with
        | NotCreated, AddMovieToLibrary data ->
            Ok [ MovieAddedToLibrary data ]
        | Active _, AddMovieToLibrary _ ->
            Error "Movie already exists in library"
        | Active movie, RemoveMovieFromLibrary ->
            Ok [ MovieRemovedFromLibrary ]
        | NotCreated, RemoveMovieFromLibrary ->
            Error "Movie does not exist"
        | Active movie, CategorizeMovie genres ->
            if movie.Genres = genres then Ok []
            else Ok [ MovieCategorized genres ]
        | Active movie, ReplacePoster posterRef ->
            Ok [ MoviePosterReplaced posterRef ]
        | Active movie, ReplaceBackdrop backdropRef ->
            Ok [ MovieBackdropReplaced backdropRef ]
        | Active movie, RecommendBy friendSlug ->
            if movie.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ MovieRecommendedBy friendSlug ]
        | Active movie, RemoveRecommendation friendSlug ->
            if movie.RecommendedBy |> Set.contains friendSlug then
                Ok [ RecommendationRemoved friendSlug ]
            else Ok []
        | Active movie, AddWantToWatchWith friendSlug ->
            if movie.WantToWatchWith |> Set.contains friendSlug then Ok []
            else Ok [ WantToWatchWith friendSlug ]
        | Active movie, RemoveFromWantToWatchWith friendSlug ->
            if movie.WantToWatchWith |> Set.contains friendSlug then
                Ok [ RemovedWantToWatchWith friendSlug ]
            else Ok []
        | Removed, _ ->
            Error "Movie has been removed"
        | NotCreated, _ ->
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

        let serialize (event: MovieEvent) : string * string =
            match event with
            | MovieAddedToLibrary data ->
                "MovieAddedToLibrary", Encode.toString 0 (encodeMovieAddedData data)
            | MovieRemovedFromLibrary ->
                "MovieRemovedFromLibrary", "{}"
            | MovieCategorized genres ->
                "MovieCategorized", Encode.toString 0 (Encode.object [ "genres", genres |> List.map Encode.string |> Encode.list ])
            | MoviePosterReplaced posterRef ->
                "MoviePosterReplaced", Encode.toString 0 (Encode.object [ "posterRef", Encode.string posterRef ])
            | MovieBackdropReplaced backdropRef ->
                "MovieBackdropReplaced", Encode.toString 0 (Encode.object [ "backdropRef", Encode.string backdropRef ])
            | MovieRecommendedBy friendSlug ->
                "MovieRecommendedBy", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | RecommendationRemoved friendSlug ->
                "RecommendationRemoved", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | WantToWatchWith friendSlug ->
                "WantToWatchWith", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | RemovedWantToWatchWith friendSlug ->
                "RemovedWantToWatchWith", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])

        let deserialize (eventType: string) (data: string) : MovieEvent option =
            match eventType with
            | "MovieAddedToLibrary" ->
                Decode.fromString decodeMovieAddedData data
                |> Result.toOption
                |> Option.map MovieAddedToLibrary
            | "MovieRemovedFromLibrary" ->
                Some MovieRemovedFromLibrary
            | "MovieCategorized" ->
                Decode.fromString (Decode.field "genres" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map MovieCategorized
            | "MoviePosterReplaced" ->
                Decode.fromString (Decode.field "posterRef" Decode.string) data
                |> Result.toOption
                |> Option.map MoviePosterReplaced
            | "MovieBackdropReplaced" ->
                Decode.fromString (Decode.field "backdropRef" Decode.string) data
                |> Result.toOption
                |> Option.map MovieBackdropReplaced
            | "MovieRecommendedBy" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map MovieRecommendedBy
            | "RecommendationRemoved" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map RecommendationRemoved
            | "WantToWatchWith" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map WantToWatchWith
            | "RemovedWantToWatchWith" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map RemovedWantToWatchWith
            | _ -> None

        let toEventData (event: MovieEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : MovieEvent option =
            deserialize storedEvent.EventType storedEvent.Data
