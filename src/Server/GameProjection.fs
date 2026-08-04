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
                personal_rating INTEGER,
                rawg_rating     REAL,
                steam_app_id    INTEGER
            );

            CREATE TABLE IF NOT EXISTS game_detail (
                slug              TEXT PRIMARY KEY,
                name              TEXT NOT NULL,
                year              INTEGER NOT NULL,
                cover_ref         TEXT,
                backdrop_ref      TEXT,
                genres            TEXT NOT NULL DEFAULT '[]',
                status            TEXT NOT NULL DEFAULT 'Backlog',
                rawg_id           INTEGER,
                rawg_rating       REAL,
                personal_rating   INTEGER,
                steam_app_id      INTEGER,
                family_owners     TEXT NOT NULL DEFAULT '[]',
                recommended_by    TEXT NOT NULL DEFAULT '[]',
                want_to_play_with TEXT NOT NULL DEFAULT '[]',
                played_with       TEXT NOT NULL DEFAULT '[]',
                total_play_time       INTEGER NOT NULL DEFAULT 0,
                prior_play_time       INTEGER NOT NULL DEFAULT 0,
                steam_library_date    TEXT,
                is_owned              INTEGER NOT NULL DEFAULT 0
            );
        """
        |> Db.exec

        // Migration for existing databases
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN is_owned INTEGER NOT NULL DEFAULT 0" |> Db.exec
        with _ -> ()
        // games-v4nqe-2 (ADR-0055): defensive re-add for any database that
        // ran iteration 1 of games-v4nqe (which dropped `genres` from both
        // tables before this reversal landed) — `CREATE TABLE IF NOT EXISTS`
        // above only helps a fresh install, not a table that already exists
        // without the column. No-op (column-already-exists) on every other
        // database, matching the try/with idiom every other migration here uses.
        try
            conn |> Db.newCommand "ALTER TABLE game_list ADD COLUMN genres TEXT NOT NULL DEFAULT '[]'" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN genres TEXT NOT NULL DEFAULT '[]'" |> Db.exec
        with _ -> ()
        // games-p6vkz: playtime accumulated before session tracking began —
        // event-derived (Prior_play_time_recorded), therefore replayable,
        // therefore a projection column rather than imperative state.
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN prior_play_time INTEGER NOT NULL DEFAULT 0" |> Db.exec
        with _ -> ()
        // Task 048: collapse legacy 'Playing' status into 'InFocus'. Idempotent.
        try
            conn |> Db.newCommand "UPDATE game_list SET status = 'InFocus' WHERE status = 'Playing'" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "UPDATE game_detail SET status = 'InFocus' WHERE status = 'Playing'" |> Db.exec
        with _ -> ()
        // games-status-vocabulary-reconcile: OnHold removed (upcast to InFocus),
        // Completed renamed Retired. Idempotent.
        try
            conn |> Db.newCommand "UPDATE game_list SET status = 'InFocus' WHERE status = 'OnHold'" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "UPDATE game_detail SET status = 'InFocus' WHERE status = 'OnHold'" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "UPDATE game_list SET status = 'Retired' WHERE status = 'Completed'" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "UPDATE game_detail SET status = 'Retired' WHERE status = 'Completed'" |> Db.exec
        with _ -> ()

        // games-a7dqx (ADR-0053): the manual play-facets override — a
        // `Projected`-tier write (this table stays `Projected`; only
        // `game_metadata_cache` is `Cache`-classified, ADR-0045). Nullable:
        // `NULL` on every column means "no override recorded", the same
        // meaning `PlayFacetsOverride`'s `None` carries on the aggregate
        // side. `facet_override_vr` stores the `VrSupport` DU as text.
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_solo INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_coop_couch INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_coop_online INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_versus_couch INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_versus_online INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_remote_play_together INTEGER" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE game_detail ADD COLUMN facet_override_vr TEXT" |> Db.exec
        with _ -> ()

    /// games-v4nqe: drops the now-fully-unread columns the emission cutover
    /// makes dead — description/short_description/website_url/hltb_*/
    /// play_modes/steam_last_played are cache-derived or query-time-derived
    /// now (see this task's event disposition table). `genres` is
    /// deliberately NOT in this list — ADR-0055 (amending ADR-0043) reverted
    /// the iteration-1 attempt to drop it and cache-source it: no refresh
    /// path in this codebase ever re-fetches Game genres (RAWG genre search
    /// only ever runs at creation time), so it fails ADR-0043's
    /// re-derivability test and stays the identity-card projection column
    /// ADR-0043's own classification table already names it as. Mirrors
    /// `SeriesProjection.dropDeprecatedColumns`'s idiom (try/with per
    /// column, tolerating "already dropped" on a database that already ran
    /// this migration).
    let dropDeprecatedColumns (conn: SqliteConnection) : unit =
        for col in [ "hltb_hours" ] do
            try
                conn |> Db.newCommand (sprintf "ALTER TABLE game_list DROP COLUMN %s" col) |> Db.exec
            with _ -> () // Column already dropped
        for col in [ "description"; "short_description"; "website_url"
                     "hltb_hours"; "hltb_main_plus_hours"; "hltb_completionist_hours"
                     "play_modes"; "steam_last_played" ] do
            try
                conn |> Db.newCommand (sprintf "ALTER TABLE game_detail DROP COLUMN %s" col) |> Db.exec
            with _ -> () // Column already dropped

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
        | Retired -> "Retired"
        | Abandoned -> "Abandoned"
        | Dismissed -> "Dismissed"

    let private parseGameStatus (s: string) : GameStatus =
        match s with
        | "Backlog" -> Backlog
        | "InFocus" -> InFocus
        | "Playing" -> InFocus  // legacy — folded into InFocus by task 048
        | "Retired" -> Retired
        | "Completed" -> Retired  // legacy — Completed renamed Retired (games-status-vocabulary-reconcile)
        | "Abandoned" -> Abandoned
        | "OnHold" -> InFocus  // legacy — OnHold removed, upcast to InFocus (games-status-vocabulary-reconcile)
        | "Dismissed" -> Dismissed
        | _ -> Backlog

    // games-a7dqx (ADR-0053): the same VrSupport encode/decode idiom used by
    // Games.Serialization and MetadataCache — kept as a small private
    // duplication (mirroring how encodeGameStatus is duplicated across
    // Games.fs/GameProjection.fs already) rather than a shared dependency,
    // since none of these three modules currently import from one another.
    let private encodeVrSupport (vr: VrSupport) =
        match vr with
        | NoVr -> "NoVr"
        | VrSupported -> "VrSupported"
        | VrOnly -> "VrOnly"

    let private decodeVrSupport (s: string) : VrSupport =
        match s with
        | "VrSupported" -> VrSupported
        | "VrOnly" -> VrOnly
        | _ -> NoVr

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
                    // games-v4nqe (hazard 1): description/short_description/
                    // website_url are no longer projection columns — this arm
                    // no longer writes them anywhere. The creation code path
                    // itself (Api.fs / PlaytimeTracker.fs, at command time,
                    // imperatively) writes them into game_metadata_cache
                    // immediately after Add_game succeeds — never this
                    // ProjectionHandler (ADR-0045's hard constraint; mirrors
                    // series-r2xhv's precedent).
                    //
                    // `genres` is the one exception (ADR-0055, amending
                    // ADR-0043): it stays an event-carried identity-card
                    // projection column, written here exactly as before this
                    // task, because no refresh path in this codebase ever
                    // re-derives Game genres.
                    let genresJson = data.Genres |> List.map Encode.string |> Encode.list |> Encode.toString 0
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO game_list (slug, name, year, cover_ref, genres, status, total_play_time, personal_rating, rawg_rating)
                        VALUES (@slug, @name, @year, @cover_ref, @genres, 'Backlog', 0, NULL, @rawg_rating)
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
                        INSERT OR REPLACE INTO game_detail (slug, name, year, cover_ref, backdrop_ref, genres, status, rawg_id, rawg_rating, personal_rating, family_owners, recommended_by, want_to_play_with, played_with, total_play_time, steam_library_date)
                        VALUES (@slug, @name, @year, @cover_ref, @backdrop_ref, @genres, 'Backlog', @rawg_id, @rawg_rating, NULL, '[]', '[]', '[]', '[]', 0, NULL)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "year", SqlType.Int32 data.Year
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

                | Games.Game_categorized _ -> () // demoted (games-v4nqe): Categorize_game had zero live callers; genres stays sourced exclusively from Game_added_to_library's payload (ADR-0043/ADR-0055) — this legacy event is ignored, four-part no-op

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

                | Games.Game_hltb_hours_set _ -> () // demoted (games-v4nqe, ADR-0043) — HLTB hours now cache-derived; legacy event, ignored

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

                | Games.Game_rawg_id_set (rawgId, rawgRating) ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET rawg_rating = @rawg_rating WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rawg_rating", match rawgRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET rawg_id = @rawg_id, rawg_rating = @rawg_rating WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "rawg_id", SqlType.Int32 rawgId
                        "rawg_rating", match rawgRating with Some r -> SqlType.Double r | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_steam_app_id_set steamAppId ->
                    conn
                    |> Db.newCommand "UPDATE game_list SET steam_app_id = @steam_app_id WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "steam_app_id", SqlType.Int32 steamAppId ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_detail SET steam_app_id = @steam_app_id WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "steam_app_id", SqlType.Int32 steamAppId ]
                    |> Db.exec

                | Games.Game_play_time_set _ ->
                    // Legacy, mandatory no-op (games-p6vkz) — mirrors
                    // Games.evolve's explicit no-op arm for the same event.
                    // Replaying this republished SUM would double-count
                    // against the reconstructed total from
                    // Prior_play_time_recorded plus the session events below.
                    ()

                // games-p6vkz: total_play_time is pure payload arithmetic —
                // every arm below adjusts it using only numbers already
                // carried on the event, never by re-reading game_play_session
                // (a different projection's table, ADR-0031 "no
                // cross-projection write"), so it stays in lock-step with
                // Games.ActiveGame.TotalPlayTimeMinutes by construction.
                | Games.Prior_play_time_recorded minutes ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET prior_play_time = @minutes, total_play_time = total_play_time + @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 minutes ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_list SET total_play_time = total_play_time + @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 minutes ]
                    |> Db.exec

                | Games.Play_session_recorded d ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET total_play_time = total_play_time + @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 d.Minutes ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_list SET total_play_time = total_play_time + @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 d.Minutes ]
                    |> Db.exec

                | Games.Play_session_minutes_corrected (_day, newMinutes, previousMinutes) ->
                    let delta = newMinutes - previousMinutes
                    conn
                    |> Db.newCommand "UPDATE game_detail SET total_play_time = total_play_time + @delta WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "delta", SqlType.Int32 delta ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_list SET total_play_time = total_play_time + @delta WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "delta", SqlType.Int32 delta ]
                    |> Db.exec

                | Games.Play_session_moved _ ->
                    // Relocating a day's minutes to another day doesn't change
                    // the sum across all days — no total_play_time change.
                    ()

                | Games.Play_session_removed (_day, previousMinutes) ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET total_play_time = total_play_time - @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 previousMinutes ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "UPDATE game_list SET total_play_time = total_play_time - @minutes WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug; "minutes", SqlType.Int32 previousMinutes ]
                    |> Db.exec

                | Games.Steam_observed_total_reconciled _ ->
                    // Affects only Games.ActiveGame.SteamObservedMinutes (the
                    // sync cursor), which is aggregate-only and has no
                    // projected column.
                    ()

                | Games.Game_description_set _ -> () // demoted (games-v4nqe, ADR-0043) — description now cache-derived; legacy event, ignored

                | Games.Game_short_description_set _ -> () // demoted (games-v4nqe, ADR-0043) — short description now cache-derived; legacy event, ignored

                | Games.Game_website_url_set _ -> () // demoted (games-v4nqe, ADR-0043) — website url now cache-derived; legacy event, ignored

                | Games.Game_play_mode_added _ -> () // demoted (games-v4nqe, ADR-0053) — superseded by Game_play_facets_overridden; legacy event, ignored

                | Games.Game_play_mode_removed _ -> () // demoted (games-v4nqe, ADR-0053) — superseded by Game_play_facets_overridden; legacy event, ignored

                | Games.Game_steam_library_date_set dateAdded ->
                    conn
                    |> Db.newCommand "UPDATE game_detail SET steam_library_date = @val WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "val", match dateAdded with Some d -> SqlType.String d | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Games.Game_steam_last_played_set _ -> () // demoted (games-v4nqe) — redundant with game_play_session, derived at query time; legacy event, ignored

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

                | Games.Game_play_facets_overridden ovr ->
                    // games-a7dqx (ADR-0053): a `Projected`-tier write — the
                    // 7 `facet_override_*` columns on `game_detail`, NOT
                    // `game_metadata_cache` (the cache tier). No
                    // ProjectionHandler ever writes the cache tier
                    // (ADR-0045); this arm never references
                    // `game_metadata_cache` at all.
                    let boolToSql (v: bool option) =
                        match v with
                        | Some true -> SqlType.Int32 1
                        | Some false -> SqlType.Int32 0
                        | None -> SqlType.Null
                    let vrToSql (v: VrSupport option) =
                        match v with
                        | Some vr -> SqlType.String (encodeVrSupport vr)
                        | None -> SqlType.Null
                    conn
                    |> Db.newCommand """
                        UPDATE game_detail SET
                            facet_override_solo = @solo,
                            facet_override_coop_couch = @coop_couch,
                            facet_override_coop_online = @coop_online,
                            facet_override_versus_couch = @versus_couch,
                            facet_override_versus_online = @versus_online,
                            facet_override_remote_play_together = @remote_play_together,
                            facet_override_vr = @vr
                        WHERE slug = @slug
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "solo", boolToSql ovr.Solo
                        "coop_couch", boolToSql ovr.CoopCouch
                        "coop_online", boolToSql ovr.CoopOnline
                        "versus_couch", boolToSql ovr.VersusCouch
                        "versus_online", boolToSql ovr.VersusOnline
                        "remote_play_together", boolToSql ovr.RemotePlayTogether
                        "vr", vrToSql ovr.Vr
                    ]
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

    /// games-v4nqe (ADR-0053): shared row-readers for the cache-derived
    /// `PlayFacets` default and the event-sourced `PlayFacetsOverride`
    /// correction — factored out of `getPlayFacets` (games-a7dqx) so
    /// `getAll`/`getBySlug` can select the same columns into their own
    /// broader queries and merge them the same way, without an N+1 per-row
    /// re-query (ADR-0048's "join in the query function" shape). Column
    /// names must match whatever SELECT the caller wrote (see `getAll`/
    /// `getBySlug`/`getPlayFacets` below).
    let private readCachedPlayFacets (rd: IDataReader) : PlayFacets =
        let readBool (col: string) =
            if rd.IsDBNull(rd.GetOrdinal(col)) then false else rd.ReadInt32 col <> 0
        {
            Solo = readBool "facet_solo"
            CoopCouch = readBool "facet_coop_couch"
            CoopOnline = readBool "facet_coop_online"
            VersusCouch = readBool "facet_versus_couch"
            VersusOnline = readBool "facet_versus_online"
            RemotePlayTogether = readBool "facet_remote_play_together"
            Vr = if rd.IsDBNull(rd.GetOrdinal("facet_vr")) then NoVr else decodeVrSupport (rd.ReadString "facet_vr")
        }

    let private readPlayFacetsOverrideRow (rd: IDataReader) : PlayFacetsOverride =
        let readOverrideBool (col: string) =
            if rd.IsDBNull(rd.GetOrdinal(col)) then None else Some (rd.ReadInt32 col <> 0)
        {
            Solo = readOverrideBool "facet_override_solo"
            CoopCouch = readOverrideBool "facet_override_coop_couch"
            CoopOnline = readOverrideBool "facet_override_coop_online"
            VersusCouch = readOverrideBool "facet_override_versus_couch"
            VersusOnline = readOverrideBool "facet_override_versus_online"
            RemotePlayTogether = readOverrideBool "facet_override_remote_play_together"
            Vr =
                if rd.IsDBNull(rd.GetOrdinal("facet_override_vr")) then None
                else Some (decodeVrSupport (rd.ReadString "facet_override_vr"))
        }

    let getAll (conn: SqliteConnection) : GameListItem list =
        conn
        |> Db.newCommand """
            SELECT gl.slug, gl.name, gl.year, gl.cover_ref, gl.genres, gl.status, gl.total_play_time, mc.hltb_hours, gl.personal_rating, gl.rawg_rating,
                   mc.facet_solo, mc.facet_coop_couch, mc.facet_coop_online, mc.facet_versus_couch, mc.facet_versus_online, mc.facet_remote_play_together, mc.facet_vr,
                   gd.facet_override_solo, gd.facet_override_coop_couch, gd.facet_override_coop_online,
                   gd.facet_override_versus_couch, gd.facet_override_versus_online,
                   gd.facet_override_remote_play_together, gd.facet_override_vr
            FROM game_list gl
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gl.slug
            LEFT JOIN game_detail gd ON gd.slug = gl.slug
            ORDER BY gl.name
        """
        |> Db.query (fun (rd: IDataReader) ->
            let genres =
                if rd.IsDBNull(rd.GetOrdinal("genres")) then []
                else Decode.fromString (Decode.list Decode.string) (rd.ReadString "genres") |> Result.defaultValue []
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
              PlayFacets = FacetDerivation.merge (readCachedPlayFacets rd) (readPlayFacetsOverrideRow rd)
              RawgRating =
                if rd.IsDBNull(rd.GetOrdinal("rawg_rating")) then None
                else Some (rd.ReadDouble "rawg_rating") }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) : GameDetail option =
        conn
        |> Db.newCommand """
            SELECT
                gd.slug, gd.name, gd.year,
                mc.description, mc.short_description, mc.website_url,
                gd.cover_ref, gd.backdrop_ref, gd.genres, gd.status, gd.rawg_id, gd.rawg_rating,
                mc.hltb_hours, mc.hltb_main_plus_hours, mc.hltb_completionist_hours,
                gd.personal_rating, gd.steam_app_id, gd.family_owners, gd.recommended_by,
                gd.want_to_play_with, gd.played_with, gd.total_play_time, gd.prior_play_time,
                gd.steam_library_date, gd.is_owned,
                (SELECT MAX(date) FROM game_play_session WHERE game_slug = gd.slug) AS steam_last_played,
                mc.facet_solo, mc.facet_coop_couch, mc.facet_coop_online,
                mc.facet_versus_couch, mc.facet_versus_online, mc.facet_remote_play_together, mc.facet_vr,
                gd.facet_override_solo, gd.facet_override_coop_couch, gd.facet_override_coop_online,
                gd.facet_override_versus_couch, gd.facet_override_versus_online,
                gd.facet_override_remote_play_together, gd.facet_override_vr
            FROM game_detail gd
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gd.slug
            WHERE gd.slug = @slug
        """
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            let genres =
                if rd.IsDBNull(rd.GetOrdinal("genres")) then []
                else Decode.fromString (Decode.list Decode.string) (rd.ReadString "genres") |> Result.defaultValue []
            let overrideRecord = readPlayFacetsOverrideRow rd
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
              Description =
                if rd.IsDBNull(rd.GetOrdinal("description")) then ""
                else rd.ReadString "description"
              ShortDescription =
                if rd.IsDBNull(rd.GetOrdinal("short_description")) then ""
                else rd.ReadString "short_description"
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
              PriorPlayTimeMinutes = rd.ReadInt32 "prior_play_time"
              PlayFacets = FacetDerivation.merge (readCachedPlayFacets rd) overrideRecord
              PlayFacetsOverride = overrideRecord
              IsOwnedByMe = rd.ReadInt32 "is_owned" <> 0
              FamilyOwners = resolveFriendRefs conn familyOwnerSlugs
              RecommendedBy = resolveFriendRefs conn recommendedBySlugs
              WantToPlayWith = resolveFriendRefs conn wantToPlayWithSlugs
              PlayedWith = resolveFriendRefs conn playedWithSlugs
              ContentBlocks = ContentBlockProjection.getForMovieDetail conn slug }
        )

    /// ADR-0053: composes the display-ready `PlayFacets` for one game by
    /// joining `game_metadata_cache`'s facet columns (the cache-derived
    /// default) with `game_detail`'s `facet_override_*` columns (the
    /// event-sourced correction) and applying `FacetDerivation.merge` —
    /// ADR-0048's "join in the query function, never the API layer" shape.
    /// `getAll`/`getBySlug` (games-v4nqe) inline the same two column groups
    /// into their own broader SELECTs and reuse `readCachedPlayFacets`/
    /// `readPlayFacetsOverrideRow` rather than calling this per row (avoiding
    /// an N+1 query); this standalone version stays for direct single-slug
    /// callers. A missing `game_metadata_cache` row (not yet backfilled, or
    /// the game was created before that row could ever be seeded) degrades
    /// to the all-false/`NoVr` cache default — never a fabricated value.
    let getPlayFacets (conn: SqliteConnection) (slug: string) : PlayFacets option =
        conn
        |> Db.newCommand """
            SELECT
                mc.facet_solo, mc.facet_coop_couch, mc.facet_coop_online,
                mc.facet_versus_couch, mc.facet_versus_online, mc.facet_remote_play_together, mc.facet_vr,
                gd.facet_override_solo, gd.facet_override_coop_couch, gd.facet_override_coop_online,
                gd.facet_override_versus_couch, gd.facet_override_versus_online,
                gd.facet_override_remote_play_together, gd.facet_override_vr
            FROM game_detail gd
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gd.slug
            WHERE gd.slug = @slug
        """
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            FacetDerivation.merge (readCachedPlayFacets rd) (readPlayFacetsOverrideRow rd))

    /// Lightweight status lookup — used by Steam sync to check whether a game already is InFocus
    /// before emitting a redundant Game_status_changed event.
    let getGameStatus (conn: SqliteConnection) (slug: string) : GameStatus option =
        conn
        |> Db.newCommand "SELECT status FROM game_list WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) -> parseGameStatus (rd.ReadString "status"))

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

    // games-v4nqe: getAllPlayModes deleted alongside its API method — play
    // modes are superseded by ADR-0053's PlayFacets/PlayFacetsOverride, and
    // `game_detail.play_modes` no longer exists.

    let findBySteamAppId (conn: SqliteConnection) (appId: int) : string option =
        conn
        |> Db.newCommand "SELECT slug FROM game_detail WHERE steam_app_id = @app_id LIMIT 1"
        |> Db.setParams [ "app_id", SqlType.Int32 appId ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "slug")

    /// games-v4nqe: rewritten to query the cache tier — description/
    /// short_description no longer live on `game_detail`. Kept (not retired
    /// in favor of the facet backfill job) since it backfills a different
    /// concern (missing description text) than `findGamesNeedingFacetBackfill`
    /// (missing facet derivation); the two cursors are independent.
    let findGamesWithEmptyDescriptionAndSteamAppId (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand """
            SELECT gd.slug, gd.steam_app_id
            FROM game_detail gd
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gd.slug
            WHERE gd.steam_app_id IS NOT NULL
              AND (mc.description IS NULL OR mc.description = '')
              AND (mc.short_description IS NULL OR mc.short_description = '')
        """
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

    let findByRawgId (conn: SqliteConnection) (rawgId: int) : (string * string) option =
        conn
        |> Db.newCommand "SELECT slug, name FROM game_detail WHERE rawg_id = @rawg_id LIMIT 1"
        |> Db.setParams [ "rawg_id", SqlType.Int32 rawgId ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "slug", rd.ReadString "name")

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
            SELECT ps.game_slug, gl.name, gl.cover_ref, gl.total_play_time, mc.hltb_hours, MAX(ps.date) as last_played
            FROM game_play_session ps
            JOIN game_list gl ON gl.slug = ps.game_slug
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = ps.game_slug
            WHERE gl.status != 'Dismissed'
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
            SELECT gl.slug, gl.name, gl.year, gl.cover_ref, gl.genres, gl.status, gl.total_play_time, mc.hltb_hours, gl.personal_rating, gl.rawg_rating,
                   mc.facet_solo, mc.facet_coop_couch, mc.facet_coop_online, mc.facet_versus_couch, mc.facet_versus_online, mc.facet_remote_play_together, mc.facet_vr,
                   gd.facet_override_solo, gd.facet_override_coop_couch, gd.facet_override_coop_online,
                   gd.facet_override_versus_couch, gd.facet_override_versus_online,
                   gd.facet_override_remote_play_together, gd.facet_override_vr
            FROM game_list gl
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gl.slug
            LEFT JOIN game_detail gd ON gd.slug = gl.slug
            WHERE gl.status != 'Dismissed'
            ORDER BY gl.rowid DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 limit ]
        |> Db.query (fun (rd: IDataReader) ->
            let genres =
                if rd.IsDBNull(rd.GetOrdinal("genres")) then []
                else Decode.fromString (Decode.list Decode.string) (rd.ReadString "genres") |> Result.defaultValue []
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
              PlayFacets = FacetDerivation.merge (readCachedPlayFacets rd) (readPlayFacetsOverrideRow rd)
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
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Retired'"
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
                SELECT mc.hltb_hours
                FROM game_list gl
                LEFT JOIN game_metadata_cache mc ON mc.game_slug = gl.slug
                WHERE gl.status IN ('Backlog', 'InFocus')
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
        // games-v4nqe-2: reverted to reading `game_list.genres` directly —
        // see ADR-0055. Genres stays an identity-card projection column
        // (ADR-0043), not cache-sourced.
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
            SELECT gl.slug, gl.name, gl.cover_ref, gl.total_play_time, mc.hltb_hours
            FROM game_list gl
            LEFT JOIN game_metadata_cache mc ON mc.game_slug = gl.slug
            WHERE gl.status = 'Retired'
              AND mc.hltb_hours IS NOT NULL
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

    let getInFocusEstimate (conn: SqliteConnection) : Mediatheca.Shared.InFocusEstimate =
        let inFocusGames =
            conn
            |> Db.newCommand """
                SELECT gl.total_play_time, mc.hltb_hours
                FROM game_list gl
                LEFT JOIN game_metadata_cache mc ON mc.game_slug = gl.slug
                WHERE gl.status = 'InFocus'
            """
            |> Db.query (fun (rd: IDataReader) ->
                let playMinutes = rd.ReadInt32 "total_play_time"
                let hltb =
                    if rd.IsDBNull(rd.GetOrdinal("hltb_hours")) then None
                    else Some (rd.ReadDouble "hltb_hours")
                playMinutes, hltb)
        let totalRemaining =
            inFocusGames
            |> List.sumBy (fun (playMin, hltb) ->
                match hltb with
                | Some h ->
                    let hltbMinutes = int (h * 60.0)
                    max 0 (hltbMinutes - playMin)
                | None -> 0)
        let gamesWithoutHltb =
            inFocusGames |> List.filter (fun (_, h) -> h.IsNone) |> List.length
        { InFocusEstimate.TotalRemainingMinutes = totalRemaining
          GameCount = List.length inFocusGames
          GamesWithoutHltb = gamesWithoutHltb }

    let getMonthlyPlayTimePerGame (conn: SqliteConnection) : Mediatheca.Shared.GameMonthlyPlayTime list =
        conn
        |> Db.newCommand """
            SELECT strftime('%Y-%m', ps.date) as month, ps.game_slug, gl.name, SUM(ps.minutes_played) as total_minutes
            FROM game_play_session ps
            JOIN game_list gl ON gl.slug = ps.game_slug
            WHERE ps.date >= date('now', '-12 months')
            GROUP BY month, ps.game_slug
            ORDER BY month, total_minutes DESC
        """
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.GameMonthlyPlayTime.Month = rd.ReadString "month"
              GameSlug = rd.ReadString "game_slug"
              GameName = rd.ReadString "name"
              MinutesPlayed = rd.ReadInt32 "total_minutes" })

    let getGamesCompletedPerYear (conn: SqliteConnection) : (int * int) list =
        // Approximate completion year using last play session date for
        // completed games. games-a7dqx: the `COALESCE(..., gd.steam_last_played)`
        // fallback is dropped (ADR-0048's honest-degradation stance,
        // mirroring series-q8jwc) — a game whose only history is dateless
        // `Prior_play_time_recorded` genuinely has no last-played date and
        // is correctly excluded here, rather than papered over with a
        // frozen, potentially stale `game_detail.steam_last_played` value.
        conn
        |> Db.newCommand """
            SELECT CAST(strftime('%Y', (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug)) AS INTEGER) as completion_year, COUNT(*) as count
            FROM game_list gl
            WHERE gl.status = 'Retired'
              AND (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug) IS NOT NULL
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
            WHERE gl.status = 'Retired'
              AND (SELECT MAX(date) FROM game_play_session WHERE game_slug = gl.slug) >= strftime('%Y-01-01', 'now')
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
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'InFocus'"
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
