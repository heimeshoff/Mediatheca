namespace Mediatheca.Server

open System
open System.Globalization
open System.Text.RegularExpressions

/// games-ev65k (ADR-0043): a pure, unit-tested best-effort parser turning
/// Steam's `release_date.date` display string into a sortable ISO
/// (`yyyy-MM-dd`) date. Steam sends four distinct shapes in practice — an
/// exact date ("25 Oct, 2026"), a month-year ("October 2026"), a bare year
/// ("2026"), and TBA phrasing ("Coming soon"/"To be announced"/"") — and
/// this module treats all four as normal, not error cases (the task's own
/// framing): the raw string is always kept verbatim for display elsewhere
/// (`Steam.SteamStoreDetails.ReleaseDateRaw`), this module only ever
/// produces the *sortable* half, and an unparseable shape yields `None`
/// rather than a guess.
///
/// Sorting semantics for partial dates (a worker judgment call, per the
/// task's own Notes): a month-year date sorts as if it released on the
/// first of that month; a bare year sorts as if it released on 1 January.
/// Both choices are invisible to the user — display always uses the raw
/// string — and only affect where a partial-precision date lands relative
/// to same-month/same-year exact dates in the Upcoming section's ordering.
module ReleaseDateParsing =

    let private exactDateFormats =
        [| "d MMM, yyyy"; "d MMM yyyy"; "MMM d, yyyy"; "MMM d yyyy"
           "d MMMM, yyyy"; "d MMMM yyyy"; "MMMM d, yyyy"; "MMMM d yyyy" |]

    let private monthYearFormats = [| "MMMM yyyy"; "MMM yyyy" |]

    let private bareYearPattern = Regex(@"^(19|20)\d{2}$")

    /// Best-effort parse of a Steam `release_date.date` string into a
    /// `DateTime` (day precision — see the module doc comment for the
    /// month-year/bare-year day-of-month convention). `None` for empty,
    /// whitespace, TBA phrasing ("Coming soon", "To be announced"), or any
    /// shape none of the three parse attempts below recognize.
    let tryParse (raw: string) : DateTime option =
        if String.IsNullOrWhiteSpace raw then None
        else
            let trimmed = raw.Trim()
            match DateTime.TryParseExact(trimmed, exactDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, d -> Some d
            | false, _ ->
                match DateTime.TryParseExact(trimmed, monthYearFormats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
                | true, d -> Some d
                | false, _ ->
                    if bareYearPattern.IsMatch(trimmed) then
                        match Int32.TryParse(trimmed) with
                        | true, y -> Some (DateTime(y, 1, 1))
                        | _ -> None
                    else
                        None

    /// The sortable ISO (`yyyy-MM-dd`) form stored in
    /// `game_metadata_cache.release_date_parsed` — plain lexical/SQL-date
    /// comparison against `date('now')` works directly on this shape.
    let tryParseSortable (raw: string) : string option =
        tryParse raw |> Option.map (fun d -> d.ToString("yyyy-MM-dd"))
