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

/// Formats the upcoming air-date line, episode date preferred over the
/// season fallback — same precedence and wording the badge row used to
/// carry before series-zdqwm moved it into the Next Up card.
let airDateLine (nextEpisodeAirDate: string option) (nextSeasonAirDate: string option) : string option =
    match nextEpisodeAirDate, nextSeasonAirDate with
    | Some d, _ ->
        let suffix = countdownLabel d
        Some (
            if suffix <> ""
            then $"Next episode airs {formatDateOnly d} ({suffix})"
            else $"Next episode airs {formatDateOnly d}")
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
    (nextUp: (int * EpisodeDto) option)
    (nextEpisodeAirDate: string option)
    (nextSeasonAirDate: string option)
    : CardContent =
    let airDate = airDateLine nextEpisodeAirDate nextSeasonAirDate
    match nextUp, airDate with
    | None, None -> NoCard
    | _ -> ShowCard(nextUp, airDate)
