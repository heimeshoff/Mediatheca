namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net
open Mediatheca.Shared

module GameProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS game_list (
                slug            TEXT PRIMARY KEY,
                name            TEXT NOT NULL,
                year            INTEGER NOT NULL,
                cover_ref       TEXT,
                genres          TEXT NOT NULL DEFAULT '[]',
                status          TEXT NOT NULL DEFAULT 'Backlog',
                total_play_time INTEGER NOT NULL DEFAULT 0,
                hltb_hours      REAL,
                personal_rating INTEGER,
                rawg_rating     REAL,
                steam_app_id    INTEGER
            );

            CREATE TABLE IF NOT EXISTS game_detail (
                slug              TEXT PRIMARY KEY,
                name              TEXT NOT NULL,
                year              INTEGER NOT NULL,
                description       TEXT NOT NULL DEFAULT '',
                short_description TEXT NOT NULL DEFAULT '',
                website_url       TEXT,
                cover_ref         TEXT,
                backdrop_ref      TEXT,
                genres            TEXT NOT NULL DEFAULT '[]',
                status            TEXT NOT NULL DEFAULT 'Backlog',
                rawg_id           INTEGER,
                rawg_rating       REAL,
                hltb_hours        REAL,
                hltb_main_plus_hours REAL,
                hltb_completionist_hours REAL,
                personal_rating   INTEGER,
                steam_app_id      INTEGER,
                play_modes        TEXT NOT NULL DEFAULT '[]',
                family_owners     TEXT NOT NULL DEFAULT '[]',
                recommended_by    TEXT NOT NULL DEFAULT '[]',
                want_to_play_with TEXT NOT NULL DEFAULT '[]',
                played_with       TEXT NOT NULL DEFAULT '[]',
                total_play_time       INTEGER NOT NULL DEFAULT 0,
                steam_library_date    TEXT,
                steam_last_played     TEXT,
                is_owned              INTEGER NOT NULL DEFAULT 0
            );
        """
        |> Db.exec

        // Migration for existing databases
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN is_owned INTEGER NOT NULL DEFAULT 0" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN hltb_main_plus_hours REAL" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN hltb_completionist_hours REAL" |> Db.exec
        with _ -> ()

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS game_list;
            DROP TABLE IF EXISTS game_detail;
        """
        |> Db.exec

    let private encodeGameStatus (status: GameStatus) =
        match status with
        | Backlog -> "Backlog"
        | InFocus -> "InFocus"
        | Playing -> "Playing"
        | Completed -> "Completed"
        | Abandoned -> "Abandoned"
        | OnHold -> "OnHold"
        | Dismissed -> "Dismissed"

    let private parseGameStatus (s: string) : GameStatus =
        match s with
        | "Backlog" -> Backlog
        | "InFocus" -> InFocus
        | "Playing" -> Playing
        | "Completed" -> Completed
        | "Abandoned" -> Abandoned
        | "OnHold" -> OnHold
        | "Dismissed" -> Dismissed
        | _ -> Backlog

    let private updateJsonList (conn: SqliteConnection) (table: string) (column: string) (slug: string) (add: bool) (value: string) : unit =
        let currentJson =
            conn
            |> Db.newCommand (sprintf "SELECT %s FROM %s WHERE slug = @slug" column table)
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString column)
            |> Option.defaultValue "[]"
        let current =
            Decode.fromString (Decode.list Decode.string) currentJson
            |> Result.defaultValue []
        let updated =
            if add then current @ [ value ] |> List.distinct
            else current |> List.filter (fun s -> s <> value)
        let updatedJson = updated |> List.map Encode.string |> Encode.list |> Encode.toString 0
        conn
        |> Db.newCommand (sprintf "UPDATE %s SET %s = @value WHERE slug = @slug" table column)
        |> Db.setParams [ "slug", SqlType.String slug; "value", SqlType.String updatedJson ]
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Game-")) then ()
        else
            let slug = event.StreamId.Substring(5) // Remove "Game-" prefix
            match Games.Serialization.fromStoredEvent event with
            | None -> ()
            | Some gameEvent ->
                match gameEvent with
                | Games.Game_added_to_library data ->
                    let genresJson = data.Genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO game_list (slug, name, year, cover_ref, genres, status, total_play_time, hltb_hours, personal_rating, rawg_rating)
                        VALUES (@slug, @name, @year, @cover_ref, @genres, 'Backlog', 0, NULL, NULL, @rawg_rating)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "cover_ref", match data.CoverRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "genres", SqlType.String genresJson
                        "rawg_rating", match data.RawgRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec

                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO game_detail (slug, name, year, description, short_description, website_url, cover_ref, backdrop_ref, genres, status, rawg_id, rawg_rating, hltb_hours, personal_rating, play_modes, family_owners, recommended_by, want_to_play_with, played_with, total_play_time, steam_library_date, steam_last_played)
                        VALUES (@slug, @name, @year, @description, @short_description, @website_url, @cover_ref, @backdrop_ref, @genres, 'Backlog', @rawg_id, @rawg_rating, NULL, NULL, '[]', '[]', '[]', '[]', '[]', 0, NULL, NULL)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
                        "description", SqlType.String data.Description
                        "short_description", SqlType.String data.ShortDescription
                        "website_url", match data.WebsiteUrl with Some u -> SqlType.String u | None -> SqlType.Null
                        "cover_ref", match data.CoverRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "backdrop_ref", match data.BackdropRef with Some r -> SqlType.String r | None -> SqlType.Null
                        "genres", SqlType.String genresJson
                        "rawg_id", match data.RawgId with Some r -> SqlType.Int32 r | None -> SqlType.Null
                        "rawg_rating", match data.RawgRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_removed_from_library ->
                    conn
                    |> Db.newCommand "DELETE FROM game_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM game_detail WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Games.Game_categorized genres ->
                    let genresJson = genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand "UPDATE game_list SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET genres = @genres WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "genres", SqlType.String genresJson ]
                    |> Db.exec

                | Games.Game_cover_replaced coverRef ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET cover_ref = @cover_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "cover_ref", SqlType.String coverRef ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET cover_ref = @cover_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "cover_ref", SqlType.String coverRef ]
                    |> Db.exec

                | Games.Game_backdrop_replaced backdropRef ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET backdrop_ref = @backdrop_ref WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "backdrop_ref", SqlType.String backdropRef ]
                    |> Db.exec

                | Games.Game_personal_rating_set rating ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET personal_rating = @personal_rating WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "personal_rating", match rating with Some r -> SqlType.Int32 r | None -> SqlType.Null
                    ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET personal_rating = @personal_rating WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "personal_rating", match rating with Some r -> SqlType.Int32 r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_status_changed status ->
                    let statusStr = encodeGameStatus status
                    conn
                    |> Db.newCommand "UPDATE game_list SET status = @status WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "status", SqlType.String statusStr ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET status = @status WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "status", SqlType.String statusStr ]
                    |> Db.exec

                | Games.Game_hltb_hours_set (hours, mainPlusHours, completionistHours) ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET hltb_hours = @hltb_hours WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "hltb_hours", match hours with Some h -> SqlType.Double h | None -> SqlType.Null
                    ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET hltb_hours = @hltb_hours, hltb_main_plus_hours = @hltb_main_plus_hours, hltb_completionist_hours = @hltb_completionist_hours WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "hltb_hours", match hours with Some h -> SqlType.Double h | None -> SqlType.Null
                        "hltb_main_plus_hours", match mainPlusHours with Some h -> SqlType.Double h | None -> SqlType.Null
                        "hltb_completionist_hours", match completionistHours with Some h -> SqlType.Double h | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_store_added _ -> () // legacy event, ignored
                | Games.Game_store_removed _ -> () // legacy event, ignored

                | Games.Game_family_owner_added friendSlug ->
                    updateJsonList conn "game_detail" "family_owners" slug true friendSlug

                | Games.Game_family_owner_removed friendSlug ->
                    updateJsonList conn "game_detail" "family_owners" slug false friendSlug

                | Games.Game_recommended_by friendSlug ->
                    updateJsonList conn "game_detail" "recommended_by" slug true friendSlug

                | Games.Game_recommendation_removed friendSlug ->
                    updateJsonList conn "game_detail" "recommended_by" slug false friendSlug

                | Games.Want_to_play_with friendSlug ->
                    updateJsonList conn "game_detail" "want_to_play_with" slug true friendSlug

                | Games.Removed_want_to_play_with friendSlug ->
                    updateJsonList conn "game_detail" "want_to_play_with" slug false friendSlug

                | Games.Game_played_with friendSlug ->
                    updateJsonList conn "game_detail" "played_with" slug true friendSlug

                | Games.Game_played_with_removed friendSlug ->
                    updateJsonList conn "game_detail" "played_with" slug false friendSlug

                | Games.Game_steam_app_id_set steamAppId ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET steam_app_id = @steam_app_id WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "steam_app_id", SqlType.Int32 steamAppId ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET steam_app_id = @steam_app_id WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "steam_app_id", SqlType.Int32 steamAppId ]
                    |> Db.exec

                | Games.Game_play_time_set totalMinutes ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET total_play_time = @total_play_time WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "total_play_time", SqlType.Int32 totalMinutes ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET total_play_time = @total_play_time WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "total_play_time", SqlType.Int32 totalMinutes ]
                    |> Db.exec

                | Games.Game_description_set description ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET description = @description WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "description", SqlType.String description ]
                    |> Db.exec

                | Games.Game_short_description_set shortDescription ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET short_description = @short_description WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "short_description", SqlType.String shortDescription ]
                    |> Db.exec

                | Games.Game_website_url_set websiteUrl ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET website_url = @website_url WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "website_url", match websiteUrl with Some u -> SqlType.String u | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_play_mode_added playMode ->
                    updateJsonList conn "game_detail" "play_modes" slug true playMode

                | Games.Game_play_mode_removed playMode ->
                    updateJsonList conn "game_detail" "play_modes" slug false playMode

                | Games.Game_steam_library_date_set dateAdded ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET steam_library_date = @val WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "val", match dateAdded with Some d -> SqlType.String d | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_steam_last_played_set lastPlayed ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET steam_last_played = @val WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "val", match lastPlayed with Some d -> SqlType.String d | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_marked_as_owned ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET is_owned = 1 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Games.Game_ownership_removed ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET is_owned = 0 WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

    let handler: Projection.ProjectionHandler = {
        Name = "GameProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let private resolveFriendRefs (conn: SqliteConnection) (slugs: string list) : FriendRef list =
        if List.isEmpty slugs then []
        else
            let friendMap =
                conn
                |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list"
                |> Db.query (fun (rd: IDataReader) ->
                    rd.ReadString "slug",
                    (rd.ReadString "name",
                     if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                     else Some (rd.ReadString "image_ref")))
                |> Map.ofList
            slugs |> List.map (fun s ->
                let name, imageRef =
                    friendMap |> Map.tryFind s |> Option.defaultValue (s, None)
                { FriendRef.Slug = s
                  Name = name
                  ImageRef = imageRef })

    let search (conn: SqliteConnection) (query: string) : LibrarySearchResult list =
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref FROM game_list WHERE name LIKE @q ORDER BY name LIMIT 10"
        |> Db.setParams [ "q", SqlType.String ("%" + query + "%") ]
        |> Db.query (fun (rd: IDataReader) ->
            { LibrarySearchResult.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              MediaType = Game }
        )

    let getAll (conn: SqliteConnection) : GameListItem list =
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref, genres, status, total_play_time, hltb_hours, personal_rating, rawg_rating FROM game_list ORDER BY name"
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            { GameListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              Genres = genres
              Status = parseGameStatus (rd.ReadString "status")
              TotalPlayTimeMinutes = rd.ReadInt32 "total_play_time"
              HltbHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                else Some (rd.ReadDouble "hltb_hours")
              PersonalRating =
                if rd.IsDBNull(rd.GetOrdinal("personal_rating")) then None
                else Some (rd.ReadInt32 "personal_rating")
              RawgRating =
                if rd.IsDBNull(rd.GetOrdinal("rawg_rating")) then None
                else Some (rd.ReadDouble "rawg_rating") }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) : GameDetail option =
        conn
        |> Db.newCommand "SELECT slug, name, year, description, short_description, website_url, cover_ref, backdrop_ref, genres, status, rawg_id, rawg_rating, hltb_hours, hltb_main_plus_hours, hltb_completionist_hours, personal_rating, steam_app_id, play_modes, family_owners, recommended_by, want_to_play_with, played_with, total_play_time, steam_library_date, steam_last_played, is_owned FROM game_detail WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            let playModesJson = rd.ReadString "play_modes"
            let playModes =
                Decode.fromString (Decode.list Decode.string) playModesJson
                |> Result.defaultValue []
            let familyOwnersJson = rd.ReadString "family_owners"
            let familyOwnerSlugs =
                Decode.fromString (Decode.list Decode.string) familyOwnersJson
                |> Result.defaultValue []
            let recommendedByJson = rd.ReadString "recommended_by"
            let recommendedBySlugs =
                Decode.fromString (Decode.list Decode.string) recommendedByJson
                |> Result.defaultValue []
            let wantToPlayWithJson = rd.ReadString "want_to_play_with"
            let wantToPlayWithSlugs =
                Decode.fromString (Decode.list Decode.string) wantToPlayWithJson
                |> Result.defaultValue []
            let playedWithJson = rd.ReadString "played_with"
            let playedWithSlugs =
                Decode.fromString (Decode.list Decode.string) playedWithJson
                |> Result.defaultValue []
            { GameDetail.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              Description = rd.ReadString "description"
              ShortDescription = rd.ReadString "short_description"
              WebsiteUrl =
                if rd.IsDBNull(rd.GetOrdinal("website_url")) then None
                else Some (rd.ReadString "website_url")
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              BackdropRef =
                if rd.IsDBNull(rd.GetOrdinal("backdrop_ref")) then None
                else Some (rd.ReadString "backdrop_ref")
              Genres = genres
              Status = parseGameStatus (rd.ReadString "status")
              RawgId =
                if rd.IsDBNull(rd.GetOrdinal("rawg_id")) then None
                else Some (rd.ReadInt32 "rawg_id")
              RawgRating =
                if rd.IsDBNull(rd.GetOrdinal("rawg_rating")) then None
                else Some (rd.ReadDouble "rawg_rating")
              HltbHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                else Some (rd.ReadDouble "hltb_hours")
              HltbMainPlusHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_main_plus_hours")) then None
                else Some (rd.ReadDouble "hltb_main_plus_hours")
              HltbCompletionistHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_completionist_hours")) then None
                else Some (rd.ReadDouble "hltb_completionist_hours")
              PersonalRating =
                if rd.IsDBNull(rd.GetOrdinal("personal_rating")) then None
                else Some (rd.ReadInt32 "personal_rating")
              SteamAppId =
                if rd.IsDBNull(rd.GetOrdinal("steam_app_id")) then None
                else Some (rd.ReadInt32 "steam_app_id")
              SteamLibraryDate =
                if rd.IsDBNull(rd.GetOrdinal("steam_library_date")) then None
                else Some (rd.ReadString "steam_library_date")
              SteamLastPlayed =
                if rd.IsDBNull(rd.GetOrdinal("steam_last_played")) then None
                else Some (rd.ReadString "steam_last_played")
              TotalPlayTimeMinutes = rd.ReadInt32 "total_play_time"
              PlayModes = playModes
              IsOwnedByMe = rd.ReadInt32 "is_owned" <> 0
              FamilyOwners = resolveFriendRefs conn familyOwnerSlugs
              RecommendedBy = resolveFriendRefs conn recommendedBySlugs
              WantToPlayWith = resolveFriendRefs conn wantToPlayWithSlugs
              PlayedWith = resolveFriendRefs conn playedWithSlugs
              ContentBlocks = ContentBlockProjection.getForMovieDetail conn slug }
        )

    let getGamesRecommendedByFriend (conn: SqliteConnection) (friendSlug: string) : FriendMediaItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref FROM game_detail WHERE recommended_by LIKE @pattern ORDER BY name"
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            { FriendMediaItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              MediaType = Game }
        )

    let getGamesWantToPlayWithFriend (conn: SqliteConnection) (friendSlug: string) : FriendMediaItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref FROM game_detail WHERE want_to_play_with LIKE @pattern ORDER BY name"
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            { FriendMediaItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              MediaType = Game }
        )

    let getGamesPlayedWithFriend (conn: SqliteConnection) (friendSlug: string) : FriendMediaItem list =
        let pattern = sprintf "%%\"%s\"%%" friendSlug
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref FROM game_detail WHERE played_with LIKE @pattern ORDER BY name"
        |> Db.setParams [ "pattern", SqlType.String pattern ]
        |> Db.query (fun (rd: IDataReader) ->
            { FriendMediaItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              MediaType = Game }
        )

    let getAllPlayModes (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT je.value FROM game_detail, json_each(game_detail.play_modes) AS je ORDER BY je.value"
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "value")

    let findBySteamAppId (conn: SqliteConnection) (appId: int) : string option =
        conn
        |> Db.newCommand "SELECT slug FROM game_detail WHERE steam_app_id = @app_id LIMIT 1"
        |> Db.setParams [ "app_id", SqlType.Int32 appId ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "slug")

    let findGamesWithEmptyDescriptionAndSteamAppId (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand "SELECT slug, steam_app_id FROM game_detail WHERE steam_app_id IS NOT NULL AND (description IS NULL OR description = '') AND (short_description IS NULL OR short_description = '')"
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "slug",
            rd.ReadInt32 "steam_app_id"
        )

    let findByName (conn: SqliteConnection) (name: string) : (string * int option) list =
        conn
        |> Db.newCommand "SELECT slug, steam_app_id FROM game_detail WHERE name = @name COLLATE NOCASE"
        |> Db.setParams [ "name", SqlType.String name ]
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "slug",
            if rd.IsDBNull(rd.GetOrdinal("steam_app_id")) then None
            else Some (rd.ReadInt32 "steam_app_id")
        )

    // Dashboard queries

    let getGamesInFocus (conn: SqliteConnection) : DashboardGameInFocus list =
        conn
        |> Db.newCommand "SELECT slug, name, year, cover_ref FROM game_list WHERE status = 'InFocus' ORDER BY rowid DESC"
        |> Db.query (fun (rd: IDataReader) ->
            { DashboardGameInFocus.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref") }
        )

    let getGamesRecentlyPlayed (conn: SqliteConnection) (limit: int) : DashboardGameRecentlyPlayed list =
        conn
        |> Db.newCommand """
            SELECT ps.game_slug, gl.name, gl.cover_ref, gl.total_play_time, gl.hltb_hours, MAX(ps.date) as last_played
            FROM game_play_session ps
            JOIN game_list gl ON gl.slug = ps.game_slug
            GROUP BY ps.game_slug
            ORDER BY last_played DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 limit ]
        |> Db.query (fun (rd: IDataReader) ->
            { DashboardGameRecentlyPlayed.Slug = rd.ReadString "game_slug"
              Name = rd.ReadString "name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              TotalPlayTimeMinutes = rd.ReadInt32 "total_play_time"
              LastPlayedDate = rd.ReadString "last_played"
              HltbHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                else Some (rd.ReadDouble "hltb_hours") }
        )

    let getRecentlyAddedGames (conn: SqliteConnection) (limit: int) : GameListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, name, year, cover_ref, genres, status, total_play_time, hltb_hours, personal_rating, rawg_rating
            FROM game_list
            ORDER BY rowid DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 limit ]
        |> Db.query (fun (rd: IDataReader) ->
            let genresJson = rd.ReadString "genres"
            let genres =
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue []
            { GameListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              Genres = genres
              Status = parseGameStatus (rd.ReadString "status")
              TotalPlayTimeMinutes = rd.ReadInt32 "total_play_time"
              HltbHours =
                if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                else Some (rd.ReadDouble "hltb_hours")
              PersonalRating =
                if rd.IsDBNull(rd.GetOrdinal("personal_rating")) then None
                else Some (rd.ReadInt32 "personal_rating")
              RawgRating =
                if rd.IsDBNull(rd.GetOrdinal("rawg_rating")) then None
                else Some (rd.ReadDouble "rawg_rating") }
        )

    let getDashboardNewGames (conn: SqliteConnection) (limit: int) : Mediatheca.Shared.DashboardNewGame list =
        conn
        |> Db.newCommand """
            SELECT gd.slug, gd.name, gd.year, gd.cover_ref, gd.family_owners,
                   COALESCE(gd.steam_library_date, '') as added_date
            FROM game_detail gd
            ORDER BY gd.steam_library_date DESC, gd.rowid DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 limit ]
        |> Db.query (fun (rd: IDataReader) ->
            let familyOwnersJson = rd.ReadString "family_owners"
            let familyOwnerSlugs =
                Decode.fromString (Decode.list Decode.string) familyOwnersJson
                |> Result.defaultValue []
            { Mediatheca.Shared.DashboardNewGame.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Year = rd.ReadInt32 "year"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              AddedDate = rd.ReadString "added_date"
              FamilyOwners = resolveFriendRefs conn familyOwnerSlugs }
        )

    // ── Dashboard Stats Queries ──

    let getGameStatusDistribution (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT status, COUNT(*) as count
            FROM game_list
            GROUP BY status
            ORDER BY count DESC
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "status", rd.ReadInt32 "count")

    let getAverageGameRating (conn: SqliteConnection) : float option =
        conn
        |> Db.newCommand "SELECT AVG(CAST(personal_rating AS REAL)) as avg_rating FROM game_list WHERE personal_rating IS NOT NULL"
        |> Db.querySingle (fun (rd: IDataReader) ->
            if rd.IsDBNull(rd.GetOrdinal("avg_rating")) then None
            else Some (rd.ReadDouble "avg_rating"))
        |> Option.flatten

    let getGameCompletionRate (conn: SqliteConnection) : float option =
        let completed =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Completed'"
            |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
        let nonBacklog =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status NOT IN ('Backlog', 'Dismissed')"
            |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
        if nonBacklog = 0 then None
        else Some (float completed / float nonBacklog * 100.0)

    let getBacklogStats (conn: SqliteConnection) : float * int * int =
        let backlogGames =
            conn
            |> Db.newCommand """
                SELECT hltb_hours FROM game_list
                WHERE status IN ('Backlog', 'InFocus')
            """
            |> Db.query (fun (rd: IDataReader) ->
                if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                else Some (rd.ReadDouble "hltb_hours"))
        let totalHours =
            backlogGames
            |> List.choose id
            |> List.sumBy id
        let totalCount = List.length backlogGames
        let withoutHltb = backlogGames |> List.filter Option.isNone |> List.length
        (totalHours, totalCount, withoutHltb)

    let getGameRatingDistribution (conn: SqliteConnection) : (int * int) list =
        conn
        |> Db.newCommand """
            SELECT personal_rating, COUNT(*) as count
            FROM game_list
            WHERE personal_rating IS NOT NULL
            GROUP BY personal_rating
            ORDER BY personal_rating
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "personal_rating", rd.ReadInt32 "count")

    let getGameGenreDistribution (conn: SqliteConnection) : (string * int) list =
        let allGenres =
            conn
            |> Db.newCommand "SELECT genres FROM game_list"
            |> Db.query (fun (rd: IDataReader) ->
                let genresJson = rd.ReadString "genres"
                Decode.fromString (Decode.list Decode.string) genresJson
                |> Result.defaultValue [])
        allGenres
        |> List.concat
        |> List.countBy id
        |> List.sortByDescending snd
        |> List.truncate 10

    let getMonthlyPlayTime (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT strftime('%Y-%m', date) as month, SUM(minutes_played) as total_minutes
            FROM game_play_session
            WHERE date >= date('now', '-12 months')
            GROUP BY month
            ORDER BY month
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "month", rd.ReadInt32 "total_minutes")

    let getHltbComparisons (conn: SqliteConnection) : Mediatheca.Shared.DashboardHltbComparison list =
        conn
        |> Db.newCommand """
            SELECT gl.slug, gl.name, gl.cover_ref, gl.total_play_time, gl.hltb_hours
            FROM game_list gl
            WHERE gl.status = 'Completed'
              AND gl.hltb_hours IS NOT NULL
              AND gl.total_play_time > 0
            ORDER BY gl.rowid DESC
            LIMIT 10
        """
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.DashboardHltbComparison.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              PlayMinutes = rd.ReadInt32 "total_play_time"
              HltbMainHours = rd.ReadDouble "hltb_hours" })

    let getGamesCompletedPerYear (conn: SqliteConnection) : (int * int) list =
        // Approximate completion year using last play session date for completed games
        conn
        |> Db.newCommand """
            SELECT CAST(strftime('%Y', COALESCE(
                (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug),
                gd.steam_last_played
            )) AS INTEGER) as completion_year, COUNT(*) as count
            FROM game_list gl
            LEFT JOIN game_detail gd ON gd.slug = gl.slug
            WHERE gl.status = 'Completed'
              AND COALESCE(
                (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug),
                gd.steam_last_played
              ) IS NOT NULL
            GROUP BY completion_year
            ORDER BY completion_year
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "completion_year", rd.ReadInt32 "count")

    // Cross-media: Total game play time in minutes
    let getTotalGamePlayTimeMinutes (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COALESCE(SUM(total_play_time), 0) as total FROM game_list"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
        |> Option.defaultValue 0

    // Cross-media: Games beaten this year (approximate using last play session date)
    let getGamesBeatenThisYear (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand """
            SELECT COUNT(*) as cnt
            FROM game_list gl
            LEFT JOIN game_detail gd ON gd.slug = gl.slug
            WHERE gl.status = 'Completed'
              AND COALESCE(
                (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug),
                gd.steam_last_played
              ) >= strftime('%Y-01-01', 'now')
        """
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Games played this month (distinct games with play sessions)
    let getGamesPlayedThisMonth (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(DISTINCT game_slug) as cnt FROM game_play_session WHERE date >= strftime('%Y-%m-01', 'now')"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Game minutes this week (last 7 days)
    let getGameMinutesThisWeek (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COALESCE(SUM(minutes_played), 0) as total FROM game_play_session WHERE date >= date('now', '-7 days')"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
        |> Option.defaultValue 0

    // Cross-media: Active games count (currently playing)
    let getActiveGamesCount (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Playing'"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Cross-media: Daily game activity for last 365 days
    let getDailyGameActivity (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT date, COUNT(DISTINCT game_slug) as count
            FROM game_play_session
            WHERE date >= date('now', '-365 days')
            GROUP BY date
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "date", rd.ReadInt32 "count")

    // Cross-media: Monthly game minutes for last 12 months
    let getMonthlyGameMinutes (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT strftime('%Y-%m', date) as month, SUM(minutes_played) as total_minutes
            FROM game_play_session
            WHERE date >= date('now', '-12 months')
            GROUP BY month
            ORDER BY month
        """
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadString "month", rd.ReadInt32 "total_minutes")
