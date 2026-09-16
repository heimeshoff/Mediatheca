/// books-f33e2: pure validation/shaping seam for the "Update progress"
/// popover. Mirrors `Shared.SetReadingProgressRequest`'s own Percent/Page/
/// TotalPages shape (minus `Slug`/`ObservedOn`, which the popover's date
/// input and the page's own route slug own) so the popover can reject an
/// impossible input (percent over 100, a page past the total) and show a
/// message *before* ever calling `api.setBookProgress` — no `Cmd`, no
/// side effect, unit-testable without driving the DOM.
module Mediatheca.Client.Pages.BookDetail.Progress

/// The two fields `api.setBookProgress` (via `SetReadingProgressRequest`)
/// actually cares about beyond identity/date — one of `Percent` or
/// `(Page, TotalPages)` is populated, never both (ADR-0076 §1).
type ProgressFields = {
    Percent: int option
    Page: int option
    TotalPages: int option
}

/// The popover's two input modes — the user picks one tab before
/// submitting; raw text, parsed and validated by `buildProgressRequest`.
type ProgressInput =
    | ByPercent of percentText: string
    | ByPage of pageText: string * totalText: string

/// Parses and validates one `ProgressInput`, producing the fields
/// `setBookProgress`'s request wants or a message fit to show inline in the
/// popover. Percent above 100 and a page past its own total are both
/// refused here — a mistake the server would refuse anyway, but at the
/// popover, before a round trip.
let buildProgressRequest (input: ProgressInput) : Result<ProgressFields, string> =
    match input with
    | ByPercent percentText ->
        match System.Int32.TryParse(percentText.Trim()) with
        | true, p when p < 0 -> Error "Percent can't be negative."
        | true, p when p > 100 -> Error "Percent can't exceed 100."
        | true, p -> Ok { Percent = Some p; Page = None; TotalPages = None }
        | false, _ -> Error "Enter a percent."
    | ByPage (pageText, totalText) ->
        match System.Int32.TryParse(pageText.Trim()), System.Int32.TryParse(totalText.Trim()) with
        | (true, _), (true, total) when total <= 0 -> Error "Enter a total page count."
        | (true, page), (true, _) when page < 0 -> Error "Page can't be negative."
        | (true, page), (true, total) when page > total -> Error "Page can't exceed total pages."
        | (true, page), (true, total) -> Ok { Percent = None; Page = Some page; TotalPages = Some total }
        | _ -> Error "Enter a page and total."
