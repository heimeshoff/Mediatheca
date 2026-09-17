/// books-f33e2: pure display-formatting seams for the book detail page's
/// hero — kept Feliz-free and unit-tested independently of the rendered
/// page, the same split `SeriesDetail.NextUp` uses.
module Mediatheca.Client.Pages.BookDetail.Format

open Mediatheca.Shared

/// The hero's length line: "9 h 12 min · narrated by X" for an
/// audiobook (from `RuntimeMinutes`/`Narrators`), "384 pages" for print/
/// ebook/unknown (from `PageCount`). `None` when the cache has neither —
/// the honest-degradation stance every other cache read in this codebase
/// takes (ADR-0043/ADR-0045).
let lengthLine (format: BookFormat) (runtimeMinutes: int option) (pageCount: int option) (narrators: string list) : string option =
    match format with
    | BookFormat.Audiobook ->
        match runtimeMinutes with
        | Some minutes when minutes > 0 ->
            let h = minutes / 60
            let m = minutes % 60
            let timePart =
                if h > 0 && m > 0 then $"{h} h {m} min"
                elif h > 0 then $"{h} h"
                else $"{m} min"
            match narrators |> List.filter (fun n -> not (System.String.IsNullOrWhiteSpace n)) with
            | [] -> Some timePart
            | names -> Some (timePart + " · narrated by " + String.concat ", " names)
        | _ -> None
    | BookFormat.Print
    | BookFormat.Ebook
    | BookFormat.Unknown ->
        match pageCount with
        | Some pages when pages > 0 -> Some $"{pages} pages"
        | _ -> None

/// The Details card's metadata line: "Publisher · Language" from whichever
/// of the two are present, joined by " · "; `None` when neither is present.
/// Deliberately excludes `PublishedDate` (books-depwh) — Audible's release
/// date is still carried in `BookDetail.PublishedDate`/`book_metadata_cache`,
/// it just isn't shown on this line.
let metaLine (publisher: string option) (language: string option) : string option =
    match [ publisher; language ] |> List.choose id with
    | [] -> None
    | parts -> Some (String.concat " · " parts)

/// "Book 2 of *Series*" when the cache carries a series position/name;
/// omitted (`None`) unless both are present — a bare series name with no
/// position isn't a claim this line is prepared to make.
let seriesLine (seriesName: string option) (seriesPosition: int option) : string option =
    match seriesName, seriesPosition with
    | Some name, Some position when not (System.String.IsNullOrWhiteSpace name) ->
        Some $"Book {position} of {name}"
    | _ -> None
