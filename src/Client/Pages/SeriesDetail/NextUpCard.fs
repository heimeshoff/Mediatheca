module Mediatheca.Client.Pages.SeriesDetail.NextUpCard

open Mediatheca.Shared

/// series-zdqwm: date formatting shared by the hero's watched-episode dates
/// and the Next Up card's air-date line. Pure and DTO-only — no Feliz
/// dependency — same discipline as `NextUp.fs`, so it can be unit-tested
/// without driving the DOM.

let formatDateOnly (date: string) =
    match date.IndexOf('T') with
    | -1 -> date
    | i -> date.[..i-1]

/// Days between today (local) and an ISO YYYY-MM-DD date. None if unparseable.
let private daysUntil (isoDate: string) : int option =
    let trimmed = formatDateOnly isoDate
    match System.DateTime.TryParse(trimmed) with
    | true, d ->
        let today = System.DateTime.Today
        Some ((d.Date - today).Days)
    | _ -> None

/// Pretty "in X days" / "today" / "tomorrow" suffix. Returns empty string
/// when already past or not parseable.
let countdownLabel (isoDate: string) : string =
    match daysUntil isoDate with
    | Some 0 -> "today"
    | Some 1 -> "tomorrow"
    | Some n when n > 1 -> sprintf "in %d days" n
    | _ -> ""

/// series-q7vhm: the first episode, ordered by (season, episode), whose
/// `AirDate` (date part) equals `date` — `date` is already the server's
/// `NextEpisodeAirDate`, so matching on it exactly keeps client and server
/// agreeing on which episode is airing without re-deriving "today" here.
/// Several episodes can share a date (a season dropping all at once); the
/// first by season/episode order is the one that counts. None means the
/// episode air date doesn't correspond to any episode in `seasons` (a data
/// gap) — the caller falls back to the generic wording in that case.
let private findAiringEpisode (seasons: SeasonDto list) (date: string) : (int * EpisodeDto) option =
    seasons
    |> List.collect (fun s -> s.Episodes |> List.map (fun e -> s.SeasonNumber, e))
    |> List.sortBy (fun (sNum, e) -> sNum, e.EpisodeNumber)
    |> List.tryFind (fun (_, e) ->
        match e.AirDate with
        | Some ad -> formatDateOnly ad = date
        | None -> false)

/// Formats the upcoming air-date line, episode date preferred over the
/// season fallback — same precedence the badge row used to carry before
/// series-zdqwm moved it into the Next Up card. series-q7vhm: the episode
/// branch now names *which* episode airs, since it often isn't the Next Up
/// episode shown above the line:
/// - same episode as Next Up -> "Airs <date> (<countdown>)" (no repeated
///   numbers, since the episode line right above it already names it)
/// - a different episode, or no Next Up at all -> "Season N, Episode M
///   airs <date> (<countdown>)"
/// - the air date doesn't match any episode in `seasons` (a data gap) ->
///   the old generic "Next episode airs <date> (<countdown>)" wording
let airDateLine
    (seasons: SeasonDto list)
    (nextUp: (int * EpisodeDto) option)
    (nextEpisodeAirDate: string option)
    (nextSeasonAirDate: string option)
    : string option =
    match nextEpisodeAirDate, nextSeasonAirDate with
    | Some d, _ ->
        let suffix = countdownLabel d
        let dateStr = formatDateOnly d
        let core =
            match findAiringEpisode seasons dateStr with
            | Some (sNum, ep) ->
                match nextUp with
                | Some (nSNum, nEp) when nSNum = sNum && nEp.EpisodeNumber = ep.EpisodeNumber ->
                    $"Airs {dateStr}"
                | _ ->
                    $"Season {sNum}, Episode {ep.EpisodeNumber} airs {dateStr}"
            | None -> $"Next episode airs {dateStr}"
        Some (if suffix <> "" then $"{core} ({suffix})" else core)
    | None, Some d ->
        let suffix = countdownLabel d
        Some (
            if suffix <> ""
            then $"Returns {formatDateOnly d} ({suffix})"
            else $"Returns {formatDateOnly d}")
    | None, None -> None

/// The Next Up card's content decision. `NoCard` means the card renders
/// nothing at all; `ShowCard` carries the optional episode line (Season N,
/// Episode M + name — None means "caught up") and the optional air-date
/// line (None means no known upcoming date).
type CardContent =
    | NoCard
    | ShowCard of episode: (int * EpisodeDto) option * airDateLabel: string option

/// series-zdqwm: the card now renders whenever there is a Next Up episode
/// *or* a known upcoming air date — previously it required a Next Up
/// episode alone. A caught-up series with a known return date still gets
/// the card, with a caught-up line standing in for the episode line.
let decide
    (seasons: SeasonDto list)
    (nextUp: (int * EpisodeDto) option)
    (nextEpisodeAirDate: string option)
    (nextSeasonAirDate: string option)
    : CardContent =
    let airDate = airDateLine seasons nextUp nextEpisodeAirDate nextSeasonAirDate
    match nextUp, airDate with
    | None, None -> NoCard
    | _ -> ShowCard(nextUp, airDate)
