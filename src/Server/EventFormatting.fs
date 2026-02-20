namespace Mediatheca.Server

open System
open Mediatheca.Shared
open Thoth.Json.Net

module EventFormatting =

    let private tryField (fieldName: string) (data: string) : string option =
        match Decode.fromString (Decode.field fieldName Decode.string) data with
        | Ok v -> Some v
        | Error _ -> None

    let private tryFieldInt (fieldName: string) (data: string) : int option =
        match Decode.fromString (Decode.field fieldName Decode.int) data with
        | Ok v -> Some v
        | Error _ -> None

    let private tryFieldFloat (fieldName: string) (data: string) : float option =
        match Decode.fromString (Decode.field fieldName Decode.float) data with
        | Ok v -> Some v
        | Error _ -> None

    let private tryFieldOptionalInt (fieldName: string) (data: string) : int option =
        match Decode.fromString (Decode.field fieldName (Decode.option Decode.int)) data with
        | Ok (Some v) -> Some v
        | _ -> None

    let private tryFieldOptionalFloat (fieldName: string) (data: string) : float option =
        match Decode.fromString (Decode.field fieldName (Decode.option Decode.float)) data with
        | Ok (Some v) -> Some v
        | _ -> None

    let private tryFieldStringList (fieldName: string) (data: string) : string list =
        match Decode.fromString (Decode.field fieldName (Decode.list Decode.string)) data with
        | Ok v -> v
        | Error _ -> []

    let private formatGameStatus (status: string) =
        match status with
        | "Backlog" -> "Backlog"
        | "InFocus" -> "In Focus"
        | "Playing" -> "Playing"
        | "Completed" -> "Completed"
        | "Abandoned" -> "Abandoned"
        | "OnHold" -> "On Hold"
        | other -> other

    let formatMovieEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Movie_added_to_library" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            let year = tryFieldInt "year" data |> Option.map string |> Option.defaultValue ""
            Some { Timestamp = ts; Label = "Added to library"; Details = [ $"{name} ({year})" ] }
        | "Movie_removed_from_library" ->
            Some { Timestamp = ts; Label = "Removed from library"; Details = [] }
        | "Movie_categorized" ->
            let genres = tryFieldStringList "genres" data
            Some { Timestamp = ts; Label = "Genres updated"; Details = [ String.Join(", ", genres) ] }
        | "Movie_poster_replaced" ->
            Some { Timestamp = ts; Label = "Poster replaced"; Details = [] }
        | "Movie_backdrop_replaced" ->
            Some { Timestamp = ts; Label = "Backdrop replaced"; Details = [] }
        | "Movie_recommended_by" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation added"; Details = [ $"By: {friend}" ] }
        | "Recommendation_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation removed"; Details = [ $"From: {friend}" ] }
        | "Want_to_watch_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Added to watch list"; Details = [ $"With: {friend}" ] }
        | "Removed_want_to_watch_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Removed from watch list"; Details = [ $"With: {friend}" ] }
        | "Watch_session_recorded" ->
            let date = tryField "date" data |> Option.defaultValue "?"
            let friends = tryFieldStringList "friendSlugs" data
            let friendsStr = String.Join(", ", friends)
            let details = [ $"Date: {date}" ] @ (if friends.IsEmpty then [] else [ $"With: {friendsStr}" ])
            Some { Timestamp = ts; Label = "Watch session recorded"; Details = details }
        | "Watch_session_date_changed" ->
            let date = tryField "date" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Watch session date changed"; Details = [ $"New date: {date}" ] }
        | "Friend_added_to_watch_session" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend added to watch session"; Details = [ friend ] }
        | "Friend_removed_from_watch_session" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend removed from watch session"; Details = [ friend ] }
        | "Watch_session_removed" ->
            Some { Timestamp = ts; Label = "Watch session removed"; Details = [] }
        | "Personal_rating_set" ->
            let rating = tryFieldOptionalInt "rating" data
            match rating with
            | Some r -> Some { Timestamp = ts; Label = "Personal rating set"; Details = [ $"Rating: {r}" ] }
            | None -> Some { Timestamp = ts; Label = "Personal rating cleared"; Details = [] }
        | "Movie_in_focus_set" ->
            Some { Timestamp = ts; Label = "Marked as In Focus"; Details = [] }
        | "Movie_in_focus_cleared" ->
            Some { Timestamp = ts; Label = "Removed from In Focus"; Details = [] }
        | _ -> None

    let formatSeriesEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Series_added_to_library" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            let year = tryFieldInt "year" data |> Option.map string |> Option.defaultValue ""
            Some { Timestamp = ts; Label = "Added to library"; Details = [ $"{name} ({year})" ] }
        | "Series_removed_from_library" ->
            Some { Timestamp = ts; Label = "Removed from library"; Details = [] }
        | "Series_categorized" ->
            let genres = tryFieldStringList "genres" data
            Some { Timestamp = ts; Label = "Genres updated"; Details = [ String.Join(", ", genres) ] }
        | "Series_poster_replaced" ->
            Some { Timestamp = ts; Label = "Poster replaced"; Details = [] }
        | "Series_backdrop_replaced" ->
            Some { Timestamp = ts; Label = "Backdrop replaced"; Details = [] }
        | "Series_recommended_by" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation added"; Details = [ $"By: {friend}" ] }
        | "Series_recommendation_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation removed"; Details = [ $"From: {friend}" ] }
        | "Series_want_to_watch_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Added to watch list"; Details = [ $"With: {friend}" ] }
        | "Series_removed_want_to_watch_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Removed from watch list"; Details = [ $"With: {friend}" ] }
        | "Series_personal_rating_set" ->
            let rating = tryFieldOptionalInt "rating" data
            match rating with
            | Some r -> Some { Timestamp = ts; Label = "Personal rating set"; Details = [ $"Rating: {r}" ] }
            | None -> Some { Timestamp = ts; Label = "Personal rating cleared"; Details = [] }
        | "Rewatch_session_created" ->
            let name = tryField "name" data
            let details = match name with Some n -> [ $"Name: {n}" ] | None -> []
            Some { Timestamp = ts; Label = "Rewatch session created"; Details = details }
        | "Rewatch_session_removed" ->
            Some { Timestamp = ts; Label = "Rewatch session removed"; Details = [] }
        | "Default_rewatch_session_changed" ->
            Some { Timestamp = ts; Label = "Default rewatch session changed"; Details = [] }
        | "Rewatch_session_friend_added" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend added to rewatch session"; Details = [ friend ] }
        | "Rewatch_session_friend_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend removed from rewatch session"; Details = [ friend ] }
        | "Episode_watched" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            let e = tryFieldInt "episodeNumber" data |> Option.map string |> Option.defaultValue "?"
            let date = tryField "date" data |> Option.defaultValue ""
            Some { Timestamp = ts; Label = $"Episode watched (S{s}E{e})"; Details = if date <> "" then [ $"Date: {date}" ] else [] }
        | "Episode_unwatched" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            let e = tryFieldInt "episodeNumber" data |> Option.map string |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = $"Episode unwatched (S{s}E{e})"; Details = [] }
        | "Season_marked_watched" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = $"Season {s} marked as watched"; Details = [] }
        | "Episodes_watched_up_to" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            let e = tryFieldInt "episodeNumber" data |> Option.map string |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = $"Watched up to S{s}E{e}"; Details = [] }
        | "Season_marked_unwatched" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = $"Season {s} marked as unwatched"; Details = [] }
        | "Episode_watched_date_changed" ->
            let s = tryFieldInt "seasonNumber" data |> Option.map string |> Option.defaultValue "?"
            let e = tryFieldInt "episodeNumber" data |> Option.map string |> Option.defaultValue "?"
            let date = tryField "date" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = $"Watch date changed (S{s}E{e})"; Details = [ $"New date: {date}" ] }
        | "Series_abandoned" ->
            Some { Timestamp = ts; Label = "Series abandoned"; Details = [] }
        | "Series_unabandoned" ->
            Some { Timestamp = ts; Label = "Series unabandoned"; Details = [] }
        | "Series_in_focus_set" ->
            Some { Timestamp = ts; Label = "Marked as In Focus"; Details = [] }
        | "Series_in_focus_cleared" ->
            Some { Timestamp = ts; Label = "Removed from In Focus"; Details = [] }
        | _ -> None

    let formatGameEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Game_added_to_library" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            let year = tryFieldInt "year" data |> Option.map string |> Option.defaultValue ""
            Some { Timestamp = ts; Label = "Added to library"; Details = [ $"{name} ({year})" ] }
        | "Game_removed_from_library" ->
            Some { Timestamp = ts; Label = "Removed from library"; Details = [] }
        | "Game_categorized" ->
            let genres = tryFieldStringList "genres" data
            Some { Timestamp = ts; Label = "Genres updated"; Details = [ String.Join(", ", genres) ] }
        | "Game_cover_replaced" ->
            Some { Timestamp = ts; Label = "Cover replaced"; Details = [] }
        | "Game_backdrop_replaced" ->
            Some { Timestamp = ts; Label = "Backdrop replaced"; Details = [] }
        | "Game_personal_rating_set" ->
            let rating = tryFieldOptionalInt "rating" data
            match rating with
            | Some r -> Some { Timestamp = ts; Label = "Personal rating set"; Details = [ $"Rating: {r}" ] }
            | None -> Some { Timestamp = ts; Label = "Personal rating cleared"; Details = [] }
        | "Game_status_changed" ->
            // GameStatus is serialized as a DU, try reading the Case field
            let statusStr =
                match Decode.fromString (Decode.field "Case" Decode.string) data with
                | Ok s -> formatGameStatus s
                | Error _ ->
                    match tryField "status" data with
                    | Some s -> formatGameStatus s
                    | None -> "?"
            Some { Timestamp = ts; Label = "Status changed"; Details = [ $"New status: {statusStr}" ] }
        | "Game_hltb_hours_set" ->
            let hours = tryFieldOptionalFloat "hours" data
            match hours with
            | Some h -> Some { Timestamp = ts; Label = "HLTB hours set"; Details = [ $"{h} hours" ] }
            | None -> Some { Timestamp = ts; Label = "HLTB hours cleared"; Details = [] }
        | "Game_store_added" ->
            let store = tryField "store" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Store added"; Details = [ store ] }
        | "Game_store_removed" ->
            let store = tryField "store" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Store removed"; Details = [ store ] }
        | "Game_family_owner_added" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Family owner added"; Details = [ friend ] }
        | "Game_family_owner_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Family owner removed"; Details = [ friend ] }
        | "Game_recommended_by" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation added"; Details = [ $"By: {friend}" ] }
        | "Game_recommendation_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Recommendation removed"; Details = [ $"From: {friend}" ] }
        | "Want_to_play_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Added to play list"; Details = [ $"With: {friend}" ] }
        | "Removed_want_to_play_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Removed from play list"; Details = [ $"With: {friend}" ] }
        | "Game_played_with" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Played with"; Details = [ friend ] }
        | "Game_played_with_removed" ->
            let friend = tryField "friendSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Played with removed"; Details = [ friend ] }
        | "Game_steam_app_id_set" ->
            let appId = tryFieldInt "steamAppId" data |> Option.map string |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Steam App ID set"; Details = [ appId ] }
        | "Game_play_time_set" ->
            let mins = tryFieldInt "totalMinutes" data |> Option.defaultValue 0
            let hours = float mins / 60.0
            Some { Timestamp = ts; Label = "Play time updated"; Details = [ $"{hours:F1} hours ({mins} min)" ] }
        | "Game_description_set" ->
            Some { Timestamp = ts; Label = "Description updated"; Details = [] }
        | "Game_short_description_set" ->
            Some { Timestamp = ts; Label = "Short description updated"; Details = [] }
        | "Game_website_url_set" ->
            Some { Timestamp = ts; Label = "Website URL updated"; Details = [] }
        | "Game_play_mode_added" ->
            let mode = tryField "playMode" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Play mode added"; Details = [ mode ] }
        | "Game_play_mode_removed" ->
            let mode = tryField "playMode" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Play mode removed"; Details = [ mode ] }
        | "Game_steam_library_date_set" ->
            Some { Timestamp = ts; Label = "Steam library date updated"; Details = [] }
        | "Game_steam_last_played_set" ->
            Some { Timestamp = ts; Label = "Steam last played updated"; Details = [] }
        | "Game_marked_as_owned" ->
            Some { Timestamp = ts; Label = "Marked as owned"; Details = [] }
        | "Game_ownership_removed" ->
            Some { Timestamp = ts; Label = "Ownership removed"; Details = [] }
        | _ -> None

    let formatFriendEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Friend_added" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend added"; Details = [ name ] }
        | "Friend_updated" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Friend updated"; Details = [ name ] }
        | "Friend_removed" ->
            Some { Timestamp = ts; Label = "Friend removed"; Details = [] }
        | _ -> None

    let formatCatalogEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Catalog_created" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Catalog created"; Details = [ name ] }
        | "Catalog_updated" ->
            let name = tryField "name" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Catalog updated"; Details = [ name ] }
        | "Catalog_removed" ->
            Some { Timestamp = ts; Label = "Catalog removed"; Details = [] }
        | "Entry_added" ->
            let movieSlug = tryField "movieSlug" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Entry added"; Details = [ movieSlug ] }
        | "Entry_updated" ->
            Some { Timestamp = ts; Label = "Entry updated"; Details = [] }
        | "Entry_removed" ->
            let entryId = tryField "entryId" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Entry removed"; Details = [ entryId ] }
        | "Entries_reordered" ->
            Some { Timestamp = ts; Label = "Entries reordered"; Details = [] }
        | _ -> None

    let formatContentBlockEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let ts = storedEvent.Timestamp.ToString("yyyy-MM-dd HH:mm")
        let data = storedEvent.Data
        match storedEvent.EventType with
        | "Content_block_added" ->
            let blockType = tryField "blockType" data |> Option.defaultValue "text"
            Some { Timestamp = ts; Label = $"Content block added ({blockType})"; Details = [] }
        | "Content_block_updated" ->
            Some { Timestamp = ts; Label = "Content block updated"; Details = [] }
        | "Content_block_removed" ->
            Some { Timestamp = ts; Label = "Content block removed"; Details = [] }
        | "Content_block_type_changed" ->
            let blockType = tryField "blockType" data |> Option.defaultValue "?"
            Some { Timestamp = ts; Label = "Content block type changed"; Details = [ $"New type: {blockType}" ] }
        | "Content_blocks_reordered" ->
            Some { Timestamp = ts; Label = "Content blocks reordered"; Details = [] }
        | "Content_blocks_row_grouped" ->
            Some { Timestamp = ts; Label = "Content blocks grouped into row"; Details = [] }
        | "Content_block_row_ungrouped" ->
            Some { Timestamp = ts; Label = "Content block ungrouped"; Details = [] }
        | _ -> None

    /// Determine which formatter to use based on stream ID prefix
    let formatEvent (storedEvent: EventStore.StoredEvent) : EventHistoryEntry option =
        let streamId = storedEvent.StreamId
        if streamId.StartsWith("Movie-") then formatMovieEvent storedEvent
        elif streamId.StartsWith("Series-") then formatSeriesEvent storedEvent
        elif streamId.StartsWith("Game-") then formatGameEvent storedEvent
        elif streamId.StartsWith("Friend-") then formatFriendEvent storedEvent
        elif streamId.StartsWith("Catalog-") then formatCatalogEvent storedEvent
        elif streamId.StartsWith("ContentBlocks-") then formatContentBlockEvent storedEvent
        else None

    /// Read events from one or more stream IDs, merge chronologically, and format
    let getStreamEvents (conn: Microsoft.Data.Sqlite.SqliteConnection) (streamIds: string list) : EventHistoryEntry list =
        streamIds
        |> List.collect (EventStore.readStream conn)
        |> List.sortBy (fun e -> e.Timestamp)
        |> List.choose formatEvent
