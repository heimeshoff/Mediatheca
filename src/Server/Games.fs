namespace Mediatheca.Server

open Thoth.Json.Net
open Mediatheca.Shared

module Games =

    // Data records for events

    type GameAddedData = {
        Name: string
        Year: int
        Genres: string list
        Description: string
        ShortDescription: string
        WebsiteUrl: string option
        CoverRef: string option
        BackdropRef: string option
        RawgId: int option
        RawgRating: float option
    }

    /// Payload of `Play_session_recorded` — a gaming day, the minutes played on
    /// it, and whether the delta came from Steam or was typed in by hand
    /// (games-p6vkz). `Source` decides whether the minutes also accumulate
    /// into `ActiveGame.SteamObservedMinutes` (see the two-fold design note on
    /// `evolve` below) — the whole reason the old Steam-sync cursor table
    /// could be deleted rather than merely guarded.
    type PlaySessionRecordedData = {
        Day: string
        Minutes: int
        Source: PlaySessionSource
    }

    /// A first Steam observation at or under this many minutes is plausibly
    /// one real sitting and is dated correctly from `rtime_last_played`; above
    /// it, it cannot be one sitting, so it is accumulated pre-tracking history
    /// (`Prior_play_time_recorded`) instead of a fabricated single day. Lives
    /// here (not the Steam adapter) so the whole policy is one pure,
    /// directly-testable function — see `Record_steam_observed_total` in
    /// `decide`. See ADR-0050 for the 16h rationale.
    [<Literal>]
    let PriorPlayTimeThresholdMinutes = 960

    /// ADR-0053: "not overridden, defer to the cache" on every facet —
    /// `ActiveGame.PlayFacetsOverride`'s default from `Game_added_to_library`
    /// onward, and `decide`'s no-op comparison base.
    let noPlayFacetsOverride : PlayFacetsOverride = {
        Solo = None
        CoopCouch = None
        CoopOnline = None
        VersusCouch = None
        VersusOnline = None
        RemotePlayTogether = None
        Vr = None
    }

    // Events

    type GameEvent =
        | Game_added_to_library of GameAddedData
        | Game_removed_from_library
        | Game_categorized of genres: string list
        | Game_cover_replaced of coverRef: string
        | Game_backdrop_replaced of backdropRef: string
        | Game_personal_rating_set of rating: int option
        | Game_status_changed of GameStatus
        | Game_hltb_hours_set of hours: float option * mainPlusHours: float option * completionistHours: float option
        | Game_store_added of store: string
        | Game_store_removed of store: string
        | Game_family_owner_added of friendSlug: string
        | Game_family_owner_removed of friendSlug: string
        | Game_recommended_by of friendSlug: string
        | Game_recommendation_removed of friendSlug: string
        | Want_to_play_with of friendSlug: string
        | Removed_want_to_play_with of friendSlug: string
        | Game_played_with of friendSlug: string
        | Game_played_with_removed of friendSlug: string
        | Game_steam_app_id_set of steamAppId: int
        | Game_play_time_set of totalMinutes: int
        // Legacy — superseded by Prior_play_time_recorded plus the four
        // session events below (games-p6vkz). Kept in the DU (never rewritten,
        // ADR-0002) purely so `evolve` can still fold old streams and
        // `Serialization` can still round-trip old rows; `evolve`'s arm for it
        // is now an explicit no-op (see below) rather than setting the total.
        | Prior_play_time_recorded of minutes: int
        | Play_session_recorded of PlaySessionRecordedData
        | Play_session_minutes_corrected of day: string * newMinutes: int * previousMinutes: int
        | Play_session_moved of fromDay: string * toDay: string * minutes: int
        | Play_session_removed of day: string * previousMinutes: int
        | Steam_observed_total_reconciled of observedMinutes: int
        | Game_description_set of description: string
        | Game_short_description_set of shortDescription: string
        | Game_website_url_set of websiteUrl: string option
        | Game_play_mode_added of playMode: string
        | Game_play_mode_removed of playMode: string
        | Game_steam_library_date_set of dateAdded: string option
        | Game_steam_last_played_set of lastPlayed: string option
        | Game_marked_as_owned
        | Game_ownership_removed
        | Game_rawg_id_set of rawgId: int * rawgRating: float option
        /// ADR-0053 (games-a7dqx): the manual-correction counterpart to the
        /// cache-derived `PlayFacets` — one event carries the whole
        /// all-`Option` override record, not seven. `Game_play_mode_added`/
        /// `Game_play_mode_removed` above are demoted (games-v4nqe) — their
        /// commands are gone, `evolve`'s arms for them are no-ops.
        | Game_play_facets_overridden of PlayFacetsOverride

    // State

    type ActiveGame = {
        Name: string
        Year: int
        Genres: string list
        Description: string
        ShortDescription: string
        WebsiteUrl: string option
        CoverRef: string option
        BackdropRef: string option
        RawgId: int option
        RawgRating: float option
        HltbHours: float option
        HltbMainPlusHours: float option
        HltbCompletionistHours: float option
        PersonalRating: int option
        Status: GameStatus
        SteamAppId: int option
        TotalPlayTimeMinutes: int
        /// Playtime accumulated before session tracking began — a distinct,
        /// dateless fact (games-p6vkz): "this much was played before we
        /// started recording". Never contributes to the diary, only the total.
        PriorPlayTimeMinutes: int
        /// Gaming day -> minutes for that day. The natural key IS the day —
        /// no synthetic session id (see the ADR's drift-detector argument).
        PlaySessions: Map<string, int>
        /// What Steam has told us, cumulatively — `PriorPlayTimeMinutes` plus
        /// every `Play_session_recorded` delta whose `Source` was `SteamSync`,
        /// **as originally recorded** (never reduced by a later correction,
        /// move, or removal). This is the two-fold design's load-bearing half:
        /// it is what makes the old Steam-sync cursor table derivable rather
        /// than merely guardable — see the ADR's phantom-session example.
        SteamObservedMinutes: int
        FamilyOwners: Set<string>
        RecommendedBy: Set<string>
        WantToPlayWith: Set<string>
        PlayedWith: Set<string>
        SteamLibraryDate: string option
        SteamLastPlayed: string option
        IsOwnedByMe: bool
        /// ADR-0053: the manual correction, defer-to-cache by default.
        /// `PlayModes: Set<string>` (games-v4nqe: deleted outright — see the
        /// `evolve` arms for `Game_play_mode_added`/`removed` below, now
        /// explicit no-ops matching the `Game_store_added` precedent).
        PlayFacetsOverride: PlayFacetsOverride
    }

    type GameState =
        | Not_created
        | Active of ActiveGame
        | Removed

    // Commands

    type GameCommand =
        | Add_game of GameAddedData
        | Remove_game
        | Replace_cover of coverRef: string
        | Replace_backdrop of backdropRef: string
        | Set_personal_rating of rating: int option
        | Change_status of GameStatus
        | Add_family_owner of friendSlug: string
        | Remove_family_owner of friendSlug: string
        | Recommend_game of friendSlug: string
        | Remove_recommendation of friendSlug: string
        | Add_want_to_play_with of friendSlug: string
        | Remove_from_want_to_play_with of friendSlug: string
        | Add_played_with of friendSlug: string
        | Remove_played_with of friendSlug: string
        | Set_steam_app_id of steamAppId: int
        // The old direct play-time setter is deleted (games-p6vkz) —
        // superseded by the commands below. Removing it (rather than leaving
        // it unreachable) is mandatory: games-h4mrd appends session events to
        // streams that already contain the legacy Game_play_time_set event,
        // and if the old command could still fire, replay would set the
        // total from the stale republished SUM and then add the
        // reconstructed total on top of it.
        | Record_prior_play_time of minutes: int
        | Record_play_session of day: string * minutesPlayed: int
        | Correct_play_session_minutes of day: string * newMinutes: int
        | Move_play_session of fromDay: string * toDay: string
        | Remove_play_session of day: string
        | Reconcile_steam_observed_total of observedMinutes: int
        /// The Steam-sync entry point: the whole first-sight / prior-playtime /
        /// delta policy lives here as one pure decision (see `decide` below),
        /// so the adapter (`PlaytimeTracker.runSync`) only ever supplies
        /// `(observedMinutes, gamingDay)` and enforces the migration gate.
        | Record_steam_observed_total of observedMinutes: int * gamingDay: string
        | Set_steam_library_date of dateAdded: string option
        | Mark_as_owned
        | Remove_ownership
        | Set_rawg_id of rawgId: int * rawgRating: float option
        /// ADR-0053: sending an all-`None` record is "un-overriding" every
        /// facet — the same operation as setting one, no separate command.
        | Override_play_facets of PlayFacetsOverride

    // Evolve

    /// Recomputes the derived total after any mutation to `PriorPlayTimeMinutes`
    /// or `PlaySessions` — "what the user asserts happened" (the ADR's first
    /// fold), kept as a plain stored field (not a computed property) so every
    /// other reader of `ActiveGame.TotalPlayTimeMinutes` is unaffected.
    let private recomputeTotal (game: ActiveGame) : ActiveGame =
        { game with
            TotalPlayTimeMinutes =
                game.PriorPlayTimeMinutes + (game.PlaySessions |> Map.toSeq |> Seq.sumBy snd) }

    let evolve (state: GameState) (event: GameEvent) : GameState =
        match state, event with
        | Not_created, Game_added_to_library data ->
            Active {
                Name = data.Name
                Year = data.Year
                Genres = data.Genres
                Description = data.Description
                ShortDescription = data.ShortDescription
                WebsiteUrl = data.WebsiteUrl
                CoverRef = data.CoverRef
                BackdropRef = data.BackdropRef
                RawgId = data.RawgId
                RawgRating = data.RawgRating
                HltbHours = None
                HltbMainPlusHours = None
                HltbCompletionistHours = None
                PersonalRating = None
                Status = Backlog
                SteamAppId = None
                TotalPlayTimeMinutes = 0
                PriorPlayTimeMinutes = 0
                PlaySessions = Map.empty
                SteamObservedMinutes = 0
                FamilyOwners = Set.empty
                RecommendedBy = Set.empty
                WantToPlayWith = Set.empty
                PlayedWith = Set.empty
                SteamLibraryDate = None
                SteamLastPlayed = None
                IsOwnedByMe = false
                PlayFacetsOverride = noPlayFacetsOverride
            }
        | Active _, Game_removed_from_library -> Removed
        | _, Game_categorized _ -> state // demoted (games-v4nqe, ADR-0043/ADR-0055) — genres stays sourced exclusively from Game_added_to_library's payload; legacy event, ignored
        | Active game, Game_cover_replaced coverRef ->
            Active { game with CoverRef = Some coverRef }
        | Active game, Game_backdrop_replaced backdropRef ->
            Active { game with BackdropRef = Some backdropRef }
        | Active game, Game_personal_rating_set rating ->
            Active { game with PersonalRating = rating }
        | Active game, Game_status_changed status ->
            Active { game with Status = status }
        | _, Game_hltb_hours_set _ -> state // demoted (games-v4nqe, ADR-0043) — HLTB hours now cache-derived; legacy event, ignored
        | _, Game_store_added _ -> state // legacy event, ignored
        | _, Game_store_removed _ -> state // legacy event, ignored
        | Active game, Game_family_owner_added friendSlug ->
            Active { game with FamilyOwners = game.FamilyOwners |> Set.add friendSlug }
        | Active game, Game_family_owner_removed friendSlug ->
            Active { game with FamilyOwners = game.FamilyOwners |> Set.remove friendSlug }
        | Active game, Game_recommended_by friendSlug ->
            Active { game with RecommendedBy = game.RecommendedBy |> Set.add friendSlug }
        | Active game, Game_recommendation_removed friendSlug ->
            Active { game with RecommendedBy = game.RecommendedBy |> Set.remove friendSlug }
        | Active game, Want_to_play_with friendSlug ->
            Active { game with WantToPlayWith = game.WantToPlayWith |> Set.add friendSlug }
        | Active game, Removed_want_to_play_with friendSlug ->
            Active { game with WantToPlayWith = game.WantToPlayWith |> Set.remove friendSlug }
        | Active game, Game_played_with friendSlug ->
            Active { game with PlayedWith = game.PlayedWith |> Set.add friendSlug }
        | Active game, Game_played_with_removed friendSlug ->
            Active { game with PlayedWith = game.PlayedWith |> Set.remove friendSlug }
        | Active game, Game_steam_app_id_set steamAppId ->
            Active { game with SteamAppId = Some steamAppId }
        // Legacy, mandatory no-op (games-p6vkz — see the DU comment on
        // Game_play_time_set above): must NOT re-derive TotalPlayTimeMinutes
        // from the old republished SUM, or replaying a stream that has both
        // this event and games-h4mrd's reconstructed session/prior events
        // would double-count.
        | Active _, Game_play_time_set _ -> state
        | Active game, Prior_play_time_recorded minutes ->
            Active (recomputeTotal { game with
                                        PriorPlayTimeMinutes = minutes
                                        SteamObservedMinutes = game.SteamObservedMinutes + minutes })
        | Active game, Play_session_recorded d ->
            let currentForDay = game.PlaySessions |> Map.tryFind d.Day |> Option.defaultValue 0
            let updatedSessions = game.PlaySessions |> Map.add d.Day (currentForDay + d.Minutes)
            let updatedSteamObserved =
                match d.Source with
                | SteamSync -> game.SteamObservedMinutes + d.Minutes
                | Manual -> game.SteamObservedMinutes
            Active (recomputeTotal { game with PlaySessions = updatedSessions; SteamObservedMinutes = updatedSteamObserved })
        | Active game, Play_session_minutes_corrected (day, newMinutes, _previousMinutes) ->
            Active (recomputeTotal { game with PlaySessions = game.PlaySessions |> Map.add day newMinutes })
        | Active game, Play_session_moved (fromDay, toDay, minutes) ->
            let withoutFrom = game.PlaySessions |> Map.remove fromDay
            let mergedAtToDay = (withoutFrom |> Map.tryFind toDay |> Option.defaultValue 0) + minutes
            Active (recomputeTotal { game with PlaySessions = withoutFrom |> Map.add toDay mergedAtToDay })
        | Active game, Play_session_removed (day, _previousMinutes) ->
            Active (recomputeTotal { game with PlaySessions = game.PlaySessions |> Map.remove day })
        | Active game, Steam_observed_total_reconciled observedMinutes ->
            // Sets SteamObservedMinutes only — TotalPlayTimeMinutes (what the
            // user asserts happened) is untouched, by design.
            Active { game with SteamObservedMinutes = observedMinutes }
        | _, Game_description_set _ -> state // demoted (games-v4nqe, ADR-0043) — description now cache-derived; legacy event, ignored
        | _, Game_short_description_set _ -> state // demoted (games-v4nqe, ADR-0043) — short description now cache-derived; legacy event, ignored
        | _, Game_website_url_set _ -> state // demoted (games-v4nqe, ADR-0043) — website url now cache-derived; legacy event, ignored
        | _, Game_play_mode_added _ -> state // demoted (games-v4nqe, ADR-0053) — superseded by Game_play_facets_overridden; legacy event, ignored
        | _, Game_play_mode_removed _ -> state // demoted (games-v4nqe, ADR-0053) — superseded by Game_play_facets_overridden; legacy event, ignored
        | Active game, Game_steam_library_date_set dateAdded ->
            Active { game with SteamLibraryDate = dateAdded }
        | _, Game_steam_last_played_set _ -> state // demoted (games-v4nqe) — redundant with game_play_session, derived at query time; legacy event, ignored
        | Active game, Game_marked_as_owned ->
            Active { game with IsOwnedByMe = true }
        | Active game, Game_ownership_removed ->
            Active { game with IsOwnedByMe = false }
        | Active game, Game_rawg_id_set (rawgId, rawgRating) ->
            Active { game with RawgId = Some rawgId; RawgRating = rawgRating }
        | Active game, Game_play_facets_overridden ovr ->
            Active { game with PlayFacetsOverride = ovr }
        | _ -> state

    let reconstitute (events: GameEvent list) : GameState =
        List.fold evolve Not_created events

    // Decide

    /// ADR-0042's any-status rule, moved out of `PlaytimeTracker`'s read-model
    /// consult (`GameProjection.getGameStatus`, CQRS-inverted) and into
    /// `decide` (games-p6vkz), the same shape `Movies.Record_watch_session`
    /// already uses. Narrowed (also games-p6vkz) to *newly recorded* sessions
    /// only — correcting, moving, or removing a session, or recording prior
    /// playtime, must never promote.
    let private promotionEvents (status: GameStatus) : GameEvent list =
        if status <> InFocus then [ Game_status_changed InFocus ] else []

    let decide (state: GameState) (command: GameCommand) : Result<GameEvent list, string> =
        match state, command with
        | Not_created, Add_game data ->
            Ok [ Game_added_to_library data ]
        | Active _, Add_game _ ->
            Error "Game already exists in library"
        | Active _, Remove_game ->
            Ok [ Game_removed_from_library ]
        | Not_created, Remove_game ->
            Error "Game does not exist"
        | Active _, Replace_cover coverRef ->
            Ok [ Game_cover_replaced coverRef ]
        | Active _, Replace_backdrop backdropRef ->
            Ok [ Game_backdrop_replaced backdropRef ]
        | Active game, Set_personal_rating rating ->
            if game.PersonalRating = rating then Ok []
            else Ok [ Game_personal_rating_set rating ]
        | Active game, Change_status status ->
            if game.Status = status then Ok []
            else Ok [ Game_status_changed status ]
        | Active game, Add_family_owner friendSlug ->
            if game.FamilyOwners |> Set.contains friendSlug then Ok []
            else Ok [ Game_family_owner_added friendSlug ]
        | Active game, Remove_family_owner friendSlug ->
            if game.FamilyOwners |> Set.contains friendSlug then
                Ok [ Game_family_owner_removed friendSlug ]
            else Ok []
        | Active game, Recommend_game friendSlug ->
            if game.RecommendedBy |> Set.contains friendSlug then Ok []
            else Ok [ Game_recommended_by friendSlug ]
        | Active game, Remove_recommendation friendSlug ->
            if game.RecommendedBy |> Set.contains friendSlug then
                Ok [ Game_recommendation_removed friendSlug ]
            else Ok []
        | Active game, Add_want_to_play_with friendSlug ->
            if game.WantToPlayWith |> Set.contains friendSlug then Ok []
            else Ok [ Want_to_play_with friendSlug ]
        | Active game, Remove_from_want_to_play_with friendSlug ->
            if game.WantToPlayWith |> Set.contains friendSlug then
                Ok [ Removed_want_to_play_with friendSlug ]
            else Ok []
        | Active game, Add_played_with friendSlug ->
            if game.PlayedWith |> Set.contains friendSlug then Ok []
            else Ok [ Game_played_with friendSlug ]
        | Active game, Remove_played_with friendSlug ->
            if game.PlayedWith |> Set.contains friendSlug then
                Ok [ Game_played_with_removed friendSlug ]
            else Ok []
        | Active game, Set_steam_app_id steamAppId ->
            if game.SteamAppId = Some steamAppId then Ok []
            else Ok [ Game_steam_app_id_set steamAppId ]
        | Active game, Record_prior_play_time minutes ->
            // Refusal is the domain-level guard that makes a lost or reset
            // sync cursor harmless: prior playtime is recorded once per game.
            if game.PriorPlayTimeMinutes > 0 then
                Error "Prior play time has already been recorded for this game"
            else
                Ok [ Prior_play_time_recorded minutes ]
        | Active game, Record_play_session (day, minutesPlayed) ->
            if minutesPlayed <= 0 then
                Error "Session minutes must be greater than 0"
            else
                Ok ([ Play_session_recorded { Day = day; Minutes = minutesPlayed; Source = Manual } ] @ promotionEvents game.Status)
        | Active game, Correct_play_session_minutes (day, newMinutes) ->
            if newMinutes <= 0 then
                Error "Session minutes must be greater than 0"
            else
                match game.PlaySessions |> Map.tryFind day with
                | None -> Error "Play session not found"
                | Some previousMinutes -> Ok [ Play_session_minutes_corrected (day, newMinutes, previousMinutes) ]
        | Active game, Move_play_session (fromDay, toDay) ->
            match game.PlaySessions |> Map.tryFind fromDay with
            | None -> Error "Play session not found"
            | Some minutes -> Ok [ Play_session_moved (fromDay, toDay, minutes) ]
        | Active game, Remove_play_session day ->
            match game.PlaySessions |> Map.tryFind day with
            | None -> Error "Play session not found"
            | Some previousMinutes -> Ok [ Play_session_removed (day, previousMinutes) ]
        | Active game, Reconcile_steam_observed_total observedMinutes ->
            if game.SteamObservedMinutes = observedMinutes then Ok []
            else Ok [ Steam_observed_total_reconciled observedMinutes ]
        | Active game, Record_steam_observed_total (observedMinutes, gamingDay) ->
            // The whole Steam-sync policy, as one pure decision (games-p6vkz):
            // see PriorPlayTimeThresholdMinutes's doc comment for the 16h
            // rationale, and the ADR for the phantom-session example this
            // shape prevents.
            if game.SteamObservedMinutes = 0 then
                // First sight of this game from Steam's perspective.
                if observedMinutes > PriorPlayTimeThresholdMinutes then
                    Ok [ Prior_play_time_recorded observedMinutes ]
                elif observedMinutes > 0 then
                    Ok ([ Play_session_recorded { Day = gamingDay; Minutes = observedMinutes; Source = SteamSync } ] @ promotionEvents game.Status)
                else
                    Ok []
            else
                let delta = observedMinutes - game.SteamObservedMinutes
                if delta > 0 then
                    Ok ([ Play_session_recorded { Day = gamingDay; Minutes = delta; Source = SteamSync } ] @ promotionEvents game.Status)
                else
                    // Zero or negative: emit nothing, adjust nothing — a
                    // corrected/removed session must not be silently re-added
                    // on the very next sync (the phantom-session case).
                    Ok []
        | Active game, Set_steam_library_date dateAdded ->
            if game.SteamLibraryDate = dateAdded then Ok []
            else Ok [ Game_steam_library_date_set dateAdded ]
        | Active game, Mark_as_owned ->
            if game.IsOwnedByMe then Ok [] else Ok [ Game_marked_as_owned ]
        | Active game, Remove_ownership ->
            if game.IsOwnedByMe then Ok [ Game_ownership_removed ] else Ok []
        | Active game, Set_rawg_id (rawgId, rawgRating) ->
            if game.RawgId = Some rawgId && game.RawgRating = rawgRating then Ok []
            else Ok [ Game_rawg_id_set (rawgId, rawgRating) ]
        | Active game, Override_play_facets ovr ->
            // Cache-blind by construction (ADR-0053) — no invariant here
            // ever reads game_metadata_cache, so a redundant-but-harmless
            // override (one that happens to match the cache) is accepted as
            // normal, self-correcting state. Only a no-op against the
            // aggregate's OWN previous override is elided.
            if game.PlayFacetsOverride = ovr then Ok []
            else Ok [ Game_play_facets_overridden ovr ]
        | Removed, _ ->
            Error "Game has been removed"
        | Not_created, _ ->
            Error "Game does not exist"

    // Stream ID

    let streamId (slug: string) = sprintf "Game-%s" slug

    // Serialization

    module Serialization =

        let private encodeGameAddedData (data: GameAddedData) =
            Encode.object [
                "name", Encode.string data.Name
                "year", Encode.int data.Year
                "genres", data.Genres |> List.map Encode.string |> Encode.list
                "description", Encode.string data.Description
                "shortDescription", Encode.string data.ShortDescription
                "websiteUrl", Encode.option Encode.string data.WebsiteUrl
                "coverRef", Encode.option Encode.string data.CoverRef
                "backdropRef", Encode.option Encode.string data.BackdropRef
                "rawgId", Encode.option Encode.int data.RawgId
                "rawgRating", Encode.option Encode.float data.RawgRating
            ]

        let private decodeGameAddedData: Decoder<GameAddedData> =
            Decode.object (fun get -> {
                Name = get.Required.Field "name" Decode.string
                Year = get.Required.Field "year" Decode.int
                Genres = get.Required.Field "genres" (Decode.list Decode.string)
                Description = get.Required.Field "description" Decode.string
                ShortDescription = get.Optional.Field "shortDescription" Decode.string |> Option.defaultValue ""
                WebsiteUrl = get.Optional.Field "websiteUrl" Decode.string
                CoverRef = get.Optional.Field "coverRef" Decode.string
                BackdropRef = get.Optional.Field "backdropRef" Decode.string
                RawgId = get.Optional.Field "rawgId" Decode.int
                RawgRating = get.Optional.Field "rawgRating" Decode.float
            })

        let private encodeGameStatus (status: GameStatus) =
            match status with
            | Backlog -> "Backlog"
            | InFocus -> "InFocus"
            | Retired -> "Retired"
            | Abandoned -> "Abandoned"
            | Dismissed -> "Dismissed"

        let private decodeGameStatus (s: string) : GameStatus =
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

        let private encodePlayFacetsOverride (o: PlayFacetsOverride) =
            Encode.object [
                "solo", Encode.option Encode.bool o.Solo
                "coopCouch", Encode.option Encode.bool o.CoopCouch
                "coopOnline", Encode.option Encode.bool o.CoopOnline
                "versusCouch", Encode.option Encode.bool o.VersusCouch
                "versusOnline", Encode.option Encode.bool o.VersusOnline
                "remotePlayTogether", Encode.option Encode.bool o.RemotePlayTogether
                "vr", Encode.option (encodeVrSupport >> Encode.string) o.Vr
            ]

        let private decodePlayFacetsOverride: Decoder<PlayFacetsOverride> =
            Decode.object (fun get -> {
                Solo = get.Optional.Field "solo" Decode.bool
                CoopCouch = get.Optional.Field "coopCouch" Decode.bool
                CoopOnline = get.Optional.Field "coopOnline" Decode.bool
                VersusCouch = get.Optional.Field "versusCouch" Decode.bool
                VersusOnline = get.Optional.Field "versusOnline" Decode.bool
                RemotePlayTogether = get.Optional.Field "remotePlayTogether" Decode.bool
                Vr = get.Optional.Field "vr" Decode.string |> Option.map decodeVrSupport
            })

        let private encodePlaySessionSource (source: PlaySessionSource) =
            match source with
            | SteamSync -> "SteamSync"
            | Manual -> "Manual"

        let private decodePlaySessionSource (s: string) : PlaySessionSource =
            match s with
            | "Manual" -> Manual
            | _ -> SteamSync

        let serialize (event: GameEvent) : string * string =
            match event with
            | Game_added_to_library data ->
                "Game_added_to_library", Encode.toString 0 (encodeGameAddedData data)
            | Game_removed_from_library ->
                "Game_removed_from_library", "{}"
            | Game_categorized genres ->
                "Game_categorized", Encode.toString 0 (Encode.object [ "genres", genres |> List.map Encode.string |> Encode.list ])
            | Game_cover_replaced coverRef ->
                "Game_cover_replaced", Encode.toString 0 (Encode.object [ "coverRef", Encode.string coverRef ])
            | Game_backdrop_replaced backdropRef ->
                "Game_backdrop_replaced", Encode.toString 0 (Encode.object [ "backdropRef", Encode.string backdropRef ])
            | Game_personal_rating_set rating ->
                "Game_personal_rating_set", Encode.toString 0 (Encode.object [ "rating", Encode.option Encode.int rating ])
            | Game_status_changed status ->
                "Game_status_changed", Encode.toString 0 (Encode.object [ "status", Encode.string (encodeGameStatus status) ])
            | Game_hltb_hours_set (hours, mainPlusHours, completionistHours) ->
                "Game_hltb_hours_set", Encode.toString 0 (Encode.object [
                    "hours", Encode.option Encode.float hours
                    "mainPlusHours", Encode.option Encode.float mainPlusHours
                    "completionistHours", Encode.option Encode.float completionistHours
                ])
            | Game_store_added store ->
                "Game_store_added", Encode.toString 0 (Encode.object [ "store", Encode.string store ])
            | Game_store_removed store ->
                "Game_store_removed", Encode.toString 0 (Encode.object [ "store", Encode.string store ])
            | Game_family_owner_added friendSlug ->
                "Game_family_owner_added", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_family_owner_removed friendSlug ->
                "Game_family_owner_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_recommended_by friendSlug ->
                "Game_recommended_by", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_recommendation_removed friendSlug ->
                "Game_recommendation_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Want_to_play_with friendSlug ->
                "Want_to_play_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Removed_want_to_play_with friendSlug ->
                "Removed_want_to_play_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_played_with friendSlug ->
                "Game_played_with", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_played_with_removed friendSlug ->
                "Game_played_with_removed", Encode.toString 0 (Encode.object [ "friendSlug", Encode.string friendSlug ])
            | Game_steam_app_id_set steamAppId ->
                "Game_steam_app_id_set", Encode.toString 0 (Encode.object [ "steamAppId", Encode.int steamAppId ])
            | Game_play_time_set totalMinutes ->
                "Game_play_time_set", Encode.toString 0 (Encode.object [ "totalMinutes", Encode.int totalMinutes ])
            | Prior_play_time_recorded minutes ->
                "Prior_play_time_recorded", Encode.toString 0 (Encode.object [ "minutes", Encode.int minutes ])
            | Play_session_recorded d ->
                "Play_session_recorded", Encode.toString 0 (Encode.object [
                    "day", Encode.string d.Day
                    "minutes", Encode.int d.Minutes
                    "source", Encode.string (encodePlaySessionSource d.Source)
                ])
            | Play_session_minutes_corrected (day, newMinutes, previousMinutes) ->
                "Play_session_minutes_corrected", Encode.toString 0 (Encode.object [
                    "day", Encode.string day
                    "newMinutes", Encode.int newMinutes
                    "previousMinutes", Encode.int previousMinutes
                ])
            | Play_session_moved (fromDay, toDay, minutes) ->
                "Play_session_moved", Encode.toString 0 (Encode.object [
                    "fromDay", Encode.string fromDay
                    "toDay", Encode.string toDay
                    "minutes", Encode.int minutes
                ])
            | Play_session_removed (day, previousMinutes) ->
                "Play_session_removed", Encode.toString 0 (Encode.object [
                    "day", Encode.string day
                    "previousMinutes", Encode.int previousMinutes
                ])
            | Steam_observed_total_reconciled observedMinutes ->
                "Steam_observed_total_reconciled", Encode.toString 0 (Encode.object [ "observedMinutes", Encode.int observedMinutes ])
            | Game_description_set description ->
                "Game_description_set", Encode.toString 0 (Encode.object [ "description", Encode.string description ])
            | Game_short_description_set shortDescription ->
                "Game_short_description_set", Encode.toString 0 (Encode.object [ "shortDescription", Encode.string shortDescription ])
            | Game_website_url_set websiteUrl ->
                "Game_website_url_set", Encode.toString 0 (Encode.object [ "websiteUrl", Encode.option Encode.string websiteUrl ])
            | Game_play_mode_added playMode ->
                "Game_play_mode_added", Encode.toString 0 (Encode.object [ "playMode", Encode.string playMode ])
            | Game_play_mode_removed playMode ->
                "Game_play_mode_removed", Encode.toString 0 (Encode.object [ "playMode", Encode.string playMode ])
            | Game_steam_library_date_set dateAdded ->
                "Game_steam_library_date_set", Encode.toString 0 (Encode.object [ "dateAdded", Encode.option Encode.string dateAdded ])
            | Game_steam_last_played_set lastPlayed ->
                "Game_steam_last_played_set", Encode.toString 0 (Encode.object [ "lastPlayed", Encode.option Encode.string lastPlayed ])
            | Game_marked_as_owned ->
                "Game_marked_as_owned", "{}"
            | Game_ownership_removed ->
                "Game_ownership_removed", "{}"
            | Game_rawg_id_set (rawgId, rawgRating) ->
                "Game_rawg_id_set", Encode.toString 0 (Encode.object [
                    "rawgId", Encode.int rawgId
                    "rawgRating", Encode.option Encode.float rawgRating
                ])
            | Game_play_facets_overridden ovr ->
                "Game_play_facets_overridden", Encode.toString 0 (encodePlayFacetsOverride ovr)

        let deserialize (eventType: string) (data: string) : GameEvent option =
            match eventType with
            | "Game_added_to_library" ->
                Decode.fromString decodeGameAddedData data
                |> Result.toOption
                |> Option.map Game_added_to_library
            | "Game_removed_from_library" ->
                Some Game_removed_from_library
            | "Game_categorized" ->
                Decode.fromString (Decode.field "genres" (Decode.list Decode.string)) data
                |> Result.toOption
                |> Option.map Game_categorized
            | "Game_cover_replaced" ->
                Decode.fromString (Decode.field "coverRef" Decode.string) data
                |> Result.toOption
                |> Option.map Game_cover_replaced
            | "Game_backdrop_replaced" ->
                Decode.fromString (Decode.field "backdropRef" Decode.string) data
                |> Result.toOption
                |> Option.map Game_backdrop_replaced
            | "Game_personal_rating_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "rating" Decode.int)) data
                |> Result.toOption
                |> Option.map Game_personal_rating_set
            | "Game_status_changed" ->
                Decode.fromString (Decode.field "status" Decode.string) data
                |> Result.toOption
                |> Option.map (decodeGameStatus >> Game_status_changed)
            | "Game_hltb_hours_set" ->
                Decode.fromString (Decode.object (fun get ->
                    let hours = get.Optional.Field "hours" Decode.float
                    let mainPlusHours = get.Optional.Field "mainPlusHours" Decode.float
                    let completionistHours = get.Optional.Field "completionistHours" Decode.float
                    (hours, mainPlusHours, completionistHours)
                )) data
                |> Result.toOption
                |> Option.map Game_hltb_hours_set
            | "Game_store_added" ->
                Decode.fromString (Decode.field "store" Decode.string) data
                |> Result.toOption
                |> Option.map Game_store_added
            | "Game_store_removed" ->
                Decode.fromString (Decode.field "store" Decode.string) data
                |> Result.toOption
                |> Option.map Game_store_removed
            | "Game_family_owner_added" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_family_owner_added
            | "Game_family_owner_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_family_owner_removed
            | "Game_recommended_by" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_recommended_by
            | "Game_recommendation_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_recommendation_removed
            | "Want_to_play_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Want_to_play_with
            | "Removed_want_to_play_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Removed_want_to_play_with
            | "Game_played_with" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_played_with
            | "Game_played_with_removed" ->
                Decode.fromString (Decode.field "friendSlug" Decode.string) data
                |> Result.toOption
                |> Option.map Game_played_with_removed
            | "Game_steam_app_id_set" ->
                Decode.fromString (Decode.field "steamAppId" Decode.int) data
                |> Result.toOption
                |> Option.map Game_steam_app_id_set
            | "Game_play_time_set" ->
                Decode.fromString (Decode.field "totalMinutes" Decode.int) data
                |> Result.toOption
                |> Option.map Game_play_time_set
            | "Prior_play_time_recorded" ->
                Decode.fromString (Decode.field "minutes" Decode.int) data
                |> Result.toOption
                |> Option.map Prior_play_time_recorded
            | "Play_session_recorded" ->
                Decode.fromString (Decode.object (fun get ->
                    { Day = get.Required.Field "day" Decode.string
                      Minutes = get.Required.Field "minutes" Decode.int
                      Source = get.Required.Field "source" Decode.string |> decodePlaySessionSource }
                )) data
                |> Result.toOption
                |> Option.map Play_session_recorded
            | "Play_session_minutes_corrected" ->
                Decode.fromString (Decode.object (fun get ->
                    let day = get.Required.Field "day" Decode.string
                    let newMinutes = get.Required.Field "newMinutes" Decode.int
                    let previousMinutes = get.Required.Field "previousMinutes" Decode.int
                    (day, newMinutes, previousMinutes)
                )) data
                |> Result.toOption
                |> Option.map Play_session_minutes_corrected
            | "Play_session_moved" ->
                Decode.fromString (Decode.object (fun get ->
                    let fromDay = get.Required.Field "fromDay" Decode.string
                    let toDay = get.Required.Field "toDay" Decode.string
                    let minutes = get.Required.Field "minutes" Decode.int
                    (fromDay, toDay, minutes)
                )) data
                |> Result.toOption
                |> Option.map Play_session_moved
            | "Play_session_removed" ->
                Decode.fromString (Decode.object (fun get ->
                    let day = get.Required.Field "day" Decode.string
                    let previousMinutes = get.Required.Field "previousMinutes" Decode.int
                    (day, previousMinutes)
                )) data
                |> Result.toOption
                |> Option.map Play_session_removed
            | "Steam_observed_total_reconciled" ->
                Decode.fromString (Decode.field "observedMinutes" Decode.int) data
                |> Result.toOption
                |> Option.map Steam_observed_total_reconciled
            | "Game_description_set" ->
                Decode.fromString (Decode.field "description" Decode.string) data
                |> Result.toOption
                |> Option.map Game_description_set
            | "Game_short_description_set" ->
                Decode.fromString (Decode.field "shortDescription" Decode.string) data
                |> Result.toOption
                |> Option.map Game_short_description_set
            | "Game_website_url_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "websiteUrl" Decode.string)) data
                |> Result.toOption
                |> Option.map Game_website_url_set
            | "Game_play_mode_added" ->
                Decode.fromString (Decode.field "playMode" Decode.string) data
                |> Result.toOption
                |> Option.map Game_play_mode_added
            | "Game_play_mode_removed" ->
                Decode.fromString (Decode.field "playMode" Decode.string) data
                |> Result.toOption
                |> Option.map Game_play_mode_removed
            | "Game_steam_library_date_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "dateAdded" Decode.string)) data
                |> Result.toOption
                |> Option.map Game_steam_library_date_set
            | "Game_steam_last_played_set" ->
                Decode.fromString (Decode.object (fun get -> get.Optional.Field "lastPlayed" Decode.string)) data
                |> Result.toOption
                |> Option.map Game_steam_last_played_set
            | "Game_marked_as_owned" ->
                Some Game_marked_as_owned
            | "Game_ownership_removed" ->
                Some Game_ownership_removed
            | "Game_rawg_id_set" ->
                Decode.fromString (Decode.object (fun get ->
                    let rawgId = get.Required.Field "rawgId" Decode.int
                    let rawgRating = get.Optional.Field "rawgRating" Decode.float
                    (rawgId, rawgRating)
                )) data
                |> Result.toOption
                |> Option.map Game_rawg_id_set
            | "Game_play_facets_overridden" ->
                Decode.fromString decodePlayFacetsOverride data
                |> Result.toOption
                |> Option.map Game_play_facets_overridden
            | _ -> None

        /// Hand-maintained mirror of the `deserialize` match-arm strings
        /// above (administration-gxd6e) — see Movies.Serialization.handledEventTypes
        /// for the pattern this follows.
        let handledEventTypes : string list = [
            "Game_added_to_library"
            "Game_removed_from_library"
            "Game_categorized"
            "Game_cover_replaced"
            "Game_backdrop_replaced"
            "Game_personal_rating_set"
            "Game_status_changed"
            "Game_hltb_hours_set"
            "Game_store_added"
            "Game_store_removed"
            "Game_family_owner_added"
            "Game_family_owner_removed"
            "Game_recommended_by"
            "Game_recommendation_removed"
            "Want_to_play_with"
            "Removed_want_to_play_with"
            "Game_played_with"
            "Game_played_with_removed"
            "Game_steam_app_id_set"
            "Game_play_time_set"
            "Prior_play_time_recorded"
            "Play_session_recorded"
            "Play_session_minutes_corrected"
            "Play_session_moved"
            "Play_session_removed"
            "Steam_observed_total_reconciled"
            "Game_description_set"
            "Game_short_description_set"
            "Game_website_url_set"
            "Game_play_mode_added"
            "Game_play_mode_removed"
            "Game_steam_library_date_set"
            "Game_steam_last_played_set"
            "Game_marked_as_owned"
            "Game_ownership_removed"
            "Game_rawg_id_set"
            "Game_play_facets_overridden"
        ]

        let toEventData (event: GameEvent) : EventStore.EventData =
            let eventType, data = serialize event
            { EventType = eventType; Data = data; Metadata = "{}" }

        let fromStoredEvent (storedEvent: EventStore.StoredEvent) : GameEvent option =
            deserialize storedEvent.EventType storedEvent.Data
