namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

/// The Journal diary of individual play sessions — games-p6vkz's read model
/// for `game_play_session`, kept as its own checkpoint-tracked projection
/// (not folded into `GameProjection`): coupling would force an operator to
/// drop 900 games' catalog just to rebuild the diary.
///
/// Table PK is the natural key `(game_slug, date)` — no synthetic id. That is
/// mechanical, not aesthetic: `Administration.diffTable` keys rows by
/// declared PK, so an `AUTOINCREMENT` id would assign different rowids on
/// shadow replay and every row would report as drifted. `source` replaces the
/// old `steam_app_id = 0` sentinel, and `created_at` is deliberately dropped
/// — a write-time artifact that would make every drift check report
/// `columnMismatch` on every row.
///
/// Prior playtime writes no row here at all — by construction, not by
/// filtering — so the Journal heatmap, Recently Played and
/// `getDashboardPlaySessions` exclude it with nothing to remember.
module PlaySessionProjection =

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS game_play_session (
                game_slug      TEXT NOT NULL,
                date           TEXT NOT NULL,
                minutes_played INTEGER NOT NULL,
                source         TEXT NOT NULL,
                PRIMARY KEY (game_slug, date)
            );

            CREATE INDEX IF NOT EXISTS idx_play_session_slug ON game_play_session(game_slug);
            CREATE INDEX IF NOT EXISTS idx_play_session_date ON game_play_session(date);

            CREATE TABLE IF NOT EXISTS game_play_session_friend (
                game_slug   TEXT NOT NULL,
                date        TEXT NOT NULL,
                friend_slug TEXT NOT NULL,
                PRIMARY KEY (game_slug, date, friend_slug)
            );

            CREATE INDEX IF NOT EXISTS idx_play_session_friend_friend ON game_play_session_friend(friend_slug);
        """
        |> Db.exec

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand "DROP TABLE IF EXISTS game_play_session; DROP TABLE IF EXISTS game_play_session_friend;"
        |> Db.exec

    let private encodeSource (source: PlaySessionSource) =
        match source with
        | SteamSync -> "SteamSync"
        | Manual -> "Manual"
        | RomM -> "RomM"

    /// ADR-0088: the exhaustive counterpart to `encodeSource` above. Prior to
    /// this task, `getSource`/`toPlaySessionDto` collapsed anything that
    /// wasn't the literal string `"Manual"` to `SteamSync` — a latent bug a
    /// third source would otherwise trip silently (every RomM-sourced diary
    /// row would read as Steam).
    let private decodeSource (s: string) : PlaySessionSource =
        match s with
        | "Manual" -> Manual
        | "RomM" -> RomM
        | _ -> SteamSync

    /// Insert-or-merge: if a row for `(slug, day)` already exists (two Steam
    /// syncs attributing to the same gaming day — integration-004), sum the
    /// minutes into it rather than overwrite. `source` is deliberately left
    /// untouched on conflict, mirroring the old `recordPlaySession`'s
    /// steam_app_id rule, so a pre-existing Manual row absorbing a Steam
    /// delta stays labelled Manual.
    let private mergeSession (conn: SqliteConnection) (slug: string) (day: string) (minutes: int) (source: PlaySessionSource) : unit =
        conn
        |> Db.newCommand """
            INSERT INTO game_play_session (game_slug, date, minutes_played, source)
            VALUES (@slug, @day, @minutes, @source)
            ON CONFLICT(game_slug, date) DO UPDATE SET
                minutes_played = minutes_played + excluded.minutes_played
        """
        |> Db.setParams [
            "slug", SqlType.String slug
            "day", SqlType.String day
            "minutes", SqlType.Int32 minutes
            "source", SqlType.String (encodeSource source)
        ]
        |> Db.exec

    let private getSource (conn: SqliteConnection) (slug: string) (day: string) : PlaySessionSource =
        conn
        |> Db.newCommand "SELECT source FROM game_play_session WHERE game_slug = @slug AND date = @day"
        |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "source")
        |> Option.map decodeSource
        |> Option.defaultValue Manual

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Game-")) then ()
        else
            let slug = event.StreamId.Substring(5) // Remove "Game-" prefix
            match Games.Serialization.fromStoredEvent event with
            | None -> ()
            | Some gameEvent ->
                match gameEvent with
                | Games.Play_session_recorded d ->
                    mergeSession conn slug d.Day d.Minutes d.Source

                | Games.Play_session_minutes_corrected (day, newMinutes, _previousMinutes) ->
                    conn
                    |> Db.newCommand "UPDATE game_play_session SET minutes_played = @minutes WHERE game_slug = @slug AND date = @day"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "day", SqlType.String day
                        "minutes", SqlType.Int32 newMinutes
                    ]
                    |> Db.exec

                | Games.Play_session_moved (fromDay, toDay, minutes) ->
                    // Carry the source across the move, then merge at the
                    // destination day (may itself already hold a row) before
                    // deleting the origin row.
                    let source = getSource conn slug fromDay
                    mergeSession conn slug toDay minutes source
                    conn
                    |> Db.newCommand "DELETE FROM game_play_session WHERE game_slug = @slug AND date = @day"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String fromDay ]
                    |> Db.exec
                    // games-zex36: carry the origin day's friends across too
                    // (copy-then-delete, same shape as the minutes merge
                    // above) — INSERT OR IGNORE so a friend already present
                    // on the destination day is a no-op, not a PK conflict.
                    conn
                    |> Db.newCommand """
                        INSERT OR IGNORE INTO game_play_session_friend (game_slug, date, friend_slug)
                        SELECT game_slug, @toDay, friend_slug FROM game_play_session_friend
                        WHERE game_slug = @slug AND date = @fromDay
                    """
                    |> Db.setParams [ "slug", SqlType.String slug; "fromDay", SqlType.String fromDay; "toDay", SqlType.String toDay ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM game_play_session_friend WHERE game_slug = @slug AND date = @day"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String fromDay ]
                    |> Db.exec

                | Games.Play_session_removed (day, _previousMinutes) ->
                    conn
                    |> Db.newCommand "DELETE FROM game_play_session WHERE game_slug = @slug AND date = @day"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM game_play_session_friend WHERE game_slug = @slug AND date = @day"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
                    |> Db.exec

                | Games.Friend_added_to_play_session (day, friendSlug) ->
                    conn
                    |> Db.newCommand "INSERT OR IGNORE INTO game_play_session_friend (game_slug, date, friend_slug) VALUES (@slug, @day, @friend)"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day; "friend", SqlType.String friendSlug ]
                    |> Db.exec

                | Games.Friend_removed_from_play_session (day, friendSlug) ->
                    conn
                    |> Db.newCommand "DELETE FROM game_play_session_friend WHERE game_slug = @slug AND date = @day AND friend_slug = @friend"
                    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day; "friend", SqlType.String friendSlug ]
                    |> Db.exec

                // Prior playtime writes no session row — by construction, not
                // filtering: the Journal diary only ever sees genuinely dated
                // sessions. Every other Games event is irrelevant here.
                | _ -> ()

    let handler: Projection.ProjectionHandler = {
        Name = "PlaySessionProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    /// Mirrors `GameProjection.resolveFriendRefs`/`MovieProjection.resolveFriendRefs`
    /// (games-zex36) — resolves session-friend slugs to display refs via
    /// `friend_list`.
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

    let private friendsForSession (conn: SqliteConnection) (slug: string) (day: string) : FriendRef list =
        conn
        |> Db.newCommand "SELECT friend_slug FROM game_play_session_friend WHERE game_slug = @slug AND date = @day ORDER BY friend_slug"
        |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "friend_slug")
        |> resolveFriendRefs conn

    let private toPlaySessionDto (conn: SqliteConnection) (rd: IDataReader) : PlaySessionDto =
        let source = decodeSource (rd.ReadString "source")
        let slug = rd.ReadString "game_slug"
        let date = rd.ReadString "date"
        { GameSlug = slug
          Date = date
          MinutesPlayed = rd.ReadInt32 "minutes_played"
          Source = source
          Friends = friendsForSession conn slug date }

    let getForGame (conn: SqliteConnection) (slug: string) : PlaySessionDto list =
        conn
        |> Db.newCommand "SELECT game_slug, date, minutes_played, source FROM game_play_session WHERE game_slug = @slug ORDER BY date DESC"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.query (toPlaySessionDto conn)

    let getBySlugAndDay (conn: SqliteConnection) (slug: string) (day: string) : PlaySessionDto option =
        conn
        |> Db.newCommand "SELECT game_slug, date, minutes_played, source FROM game_play_session WHERE game_slug = @slug AND date = @day"
        |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
        |> Db.querySingle (toPlaySessionDto conn)

    /// Friend-page query, shaped like `MovieProjection.getMoviesWatchedWithFriend`
    /// (games-zex36): every gaming day a friend was attached to a session,
    /// grouped by game slug -> its dates. Membership itself (whether a game
    /// counts as "played with" at all) stays driven by `played_with` — see
    /// `GameProjection.getGamesPlayedWithFriend` — this only supplies real
    /// dates on top for games that have session friends recorded.
    let getGamePlaySessionDatesForFriend (conn: SqliteConnection) (friendSlug: string) : Map<string, string list> =
        conn
        |> Db.newCommand "SELECT game_slug, date FROM game_play_session_friend WHERE friend_slug = @friend ORDER BY game_slug, date DESC"
        |> Db.setParams [ "friend", SqlType.String friendSlug ]
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "game_slug", rd.ReadString "date")
        |> List.groupBy fst
        |> List.map (fun (slug, rows) -> slug, rows |> List.map snd)
        |> Map.ofList

    let hasAnySessions (conn: SqliteConnection) (slug: string) : bool =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_play_session WHERE game_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
        |> Option.map (fun c -> c > 0)
        |> Option.defaultValue false

    let getPlaytimeSummary (conn: SqliteConnection) (fromDate: string) (toDate: string) : PlaytimeSummaryItem list =
        conn
        |> Db.newCommand """
            SELECT ps.game_slug,
                   COALESCE(gd.name, ps.game_slug) as game_name,
                   gd.cover_ref,
                   SUM(ps.minutes_played) as total_minutes,
                   COUNT(*) as session_count
            FROM game_play_session ps
            LEFT JOIN game_detail gd ON gd.slug = ps.game_slug
            WHERE ps.date >= @from_date AND ps.date <= @to_date
            GROUP BY ps.game_slug
            ORDER BY total_minutes DESC
        """
        |> Db.setParams [
            "from_date", SqlType.String fromDate
            "to_date", SqlType.String toDate
        ]
        |> Db.query (fun (rd: IDataReader) ->
            { PlaytimeSummaryItem.GameSlug = rd.ReadString "game_slug"
              GameName = rd.ReadString "game_name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              TotalMinutes = rd.ReadInt32 "total_minutes"
              SessionCount = rd.ReadInt32 "session_count" })

    let getDashboardPlaySessions (conn: SqliteConnection) (days: int) : DashboardPlaySession list =
        let fromDate = System.DateTime.Now.AddDays(float -days).ToString("yyyy-MM-dd")
        conn
        |> Db.newCommand """
            SELECT ps.game_slug,
                   COALESCE(gd.name, ps.game_slug) as game_name,
                   gd.cover_ref,
                   ps.date,
                   ps.minutes_played
            FROM game_play_session ps
            LEFT JOIN game_detail gd ON gd.slug = ps.game_slug
            WHERE ps.date >= @from_date
            ORDER BY ps.date
        """
        |> Db.setParams [ "from_date", SqlType.String fromDate ]
        |> Db.query (fun (rd: IDataReader) ->
            { DashboardPlaySession.GameSlug = rd.ReadString "game_slug"
              GameName = rd.ReadString "game_name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              Date = rd.ReadString "date"
              MinutesPlayed = rd.ReadInt32 "minutes_played" })
