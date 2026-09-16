namespace Mediatheca.Server

open System.Net.Http
open System.Xml.Linq

/// Goodreads — read through its public, key-free RSS feeds keyed by the
/// user's Goodreads user id (ADR-0075). No developer API exists any more
/// (retired 2020-12-08) and no session cookie is ever accepted here; the
/// shelf RSS feed (`review/list_rss/{id}?shelf={shelf}`) is public for a
/// public profile and needs neither key nor auth. This module is compiled
/// BEFORE `BookProjection.fs`/`Api.fs` in `Server.fsproj` (the adapter
/// block), mirroring `OpenLibrary.fs`'s own doc comment about that ordering.
module Goodreads =

    /// integration-wmqn3 (ADR-0075): the user's Goodreads setting -- their
    /// public user id, never a key, never a cookie -- plus which shelves
    /// (beyond the always-on `currently-reading`) get NEW, unmatched items
    /// imported into the library.
    type GoodreadsConfig = {
        UserId: string option
        ImportShelves: string list
    }

    /// One item off a shelf RSS feed (`review/list_rss/{id}?shelf=...`) --
    /// field names per the research report's live element list (goodreads-
    /// reading-progress-and-book-metadata-sources-2026-09-16, §2a).
    type GoodreadsShelfItem = {
        BookId: string
        Title: string
        Author: string
        Isbn: string option
        Isbn13: string option
        ImageUrl: string option
        LargeImageUrl: string option
        NumPages: int option
        AverageRating: float option
        /// `user_rating` of `0` means "not rated" on Goodreads -- decoded to
        /// `None`, never `Some 0` (this task's own acceptance criterion).
        UserRating: int option
        Published: int option
        DateAdded: string option
        ReadAt: string option
        Shelves: string list
    }

    /// A 403/404, an empty channel with no title, or a redirect to a sign-in
    /// page all collapse to `ProfilePrivateOrUnknown` -- the feature requires
    /// a public profile, and Goodreads gives no sharper signal than that.
    type GoodreadsError =
        | ProfilePrivateOrUnknown
        | FeedUnavailable of string
        | ParseFailed of string

    type private GoodreadsFeed = {
        ChannelTitle: string
        Items: GoodreadsShelfItem list
    }

    // ── parseUserId ──────────────────────────────────────────────────────

    /// Accepts a bare numeric id, or a `goodreads.com/user/show/{id}[-slug]`
    /// / `goodreads.com/review/list/{id}...` URL (with or without a scheme).
    /// Rejects anything without a recognizable numeric id.
    let parseUserId (input: string) : Result<string, string> =
        let trimmed = if isNull input then "" else input.Trim()
        if trimmed = "" then
            Error "Goodreads user id cannot be empty"
        elif System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+$") then
            Ok trimmed
        else
            let m =
                System.Text.RegularExpressions.Regex.Match(
                    trimmed,
                    @"goodreads\.com/(?:user/show|review/list)/(\d+)")
            if m.Success then Ok m.Groups.[1].Value
            else Error "Could not find a Goodreads user id in that input -- paste your profile URL or numeric id"

    // ── Throttle (ADR-0066's shape, copied from OpenLibrary.fs/Steam.fs) ──
    // One request per 2 seconds across all Goodreads calls (this task's own
    // instructions) -- Goodreads has an active anti-automation posture and
    // this adapter is a plain unauthenticated GET, never a login.

    let mutable throttleInterval = System.TimeSpan.FromSeconds(2.0)

    let private gate = new System.Threading.SemaphoreSlim(1, 1)
    let mutable private lastCallStartedAt: System.DateTime option = None

    /// Exposed so tests can exercise the gate directly, the same
    /// `OpenLibrary.throttleApiCall` precedent.
    let throttleCall (fetch: unit -> Async<'a>) : Async<'a> =
        async {
            do! gate.WaitAsync() |> Async.AwaitTask
            try
                let now = System.DateTime.UtcNow
                match lastCallStartedAt with
                | Some last ->
                    let remaining = throttleInterval - (now - last)
                    if remaining > System.TimeSpan.Zero then
                        do! Async.Sleep remaining
                | None -> ()
                lastCallStartedAt <- Some System.DateTime.UtcNow
                return! fetch ()
            finally
                gate.Release() |> ignore
        }

    // ── RSS parsing ──────────────────────────────────────────────────────

    /// A browser-like User-Agent -- Goodreads' anti-bot posture treats a
    /// bare/empty or clearly-automated UA more suspiciously than a normal
    /// browser string; this is still a plain unauthenticated GET, never a
    /// login (ADR-0070's reasoning).
    let private userAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

    let private elementText (item: XElement) (name: string) : string option =
        match item.Element(XName.Get name) with
        | null -> None
        | el ->
            let v = el.Value.Trim()
            if v = "" then None else Some v

    let private parseIntElement (item: XElement) (name: string) : int option =
        elementText item name
        |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)

    let private parseItem (item: XElement) : GoodreadsShelfItem =
        let userRating =
            parseIntElement item "user_rating"
            |> Option.bind (fun v -> if v = 0 then None else Some v)
        let shelves =
            elementText item "user_shelves"
            |> Option.map (fun s ->
                s.Split(',')
                |> Array.map (fun x -> x.Trim())
                |> Array.filter (fun x -> x <> "")
                |> Array.toList)
            |> Option.defaultValue []
        { BookId = elementText item "book_id" |> Option.defaultValue ""
          Title = elementText item "title" |> Option.defaultValue ""
          Author = elementText item "author_name" |> Option.defaultValue ""
          Isbn = elementText item "isbn"
          Isbn13 = elementText item "isbn13"
          ImageUrl = elementText item "book_image_url"
          LargeImageUrl = elementText item "book_large_image_url"
          NumPages = parseIntElement item "num_pages"
          AverageRating =
            elementText item "average_rating"
            |> Option.bind (fun s -> match System.Double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture) with true, v -> Some v | _ -> None)
          UserRating = userRating
          Published = parseIntElement item "book_published"
          DateAdded = elementText item "user_date_added"
          ReadAt = elementText item "user_read_at"
          Shelves = shelves }

    let private parseFeed (xml: string) : Result<GoodreadsFeed, GoodreadsError> =
        try
            let doc = XDocument.Parse(xml)
            match doc.Root with
            | null -> Error (ParseFailed "empty document")
            | root ->
                match root.Element(XName.Get "channel") with
                | null -> Error (ParseFailed "no <channel> element")
                | channel ->
                    match elementText channel "title" with
                    | None -> Error ProfilePrivateOrUnknown
                    | Some channelTitle ->
                        let items =
                            channel.Elements(XName.Get "item")
                            |> Seq.map parseItem
                            |> Seq.toList
                        Ok { ChannelTitle = channelTitle; Items = items }
        with ex ->
            Error (ParseFailed ex.Message)

    let private fetchFeed (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<GoodreadsFeed, GoodreadsError>> =
        throttleCall (fun () ->
            async {
                try
                    let url =
                        sprintf "https://www.goodreads.com/review/list_rss/%s?shelf=%s"
                            userId (System.Uri.EscapeDataString shelf)
                    use request = new HttpRequestMessage(HttpMethod.Get, url)
                    request.Headers.Add("User-Agent", userAgent)
                    request.Headers.Add("Accept", "application/rss+xml, application/xml")
                    let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                    if response.StatusCode = System.Net.HttpStatusCode.Forbidden
                       || response.StatusCode = System.Net.HttpStatusCode.NotFound then
                        return Error ProfilePrivateOrUnknown
                    elif not response.IsSuccessStatusCode then
                        return Error (FeedUnavailable (sprintf "HTTP %d" (int response.StatusCode)))
                    else
                        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return parseFeed body
                with ex ->
                    return Error (FeedUnavailable ex.Message)
            })

    /// `GET https://www.goodreads.com/review/list_rss/{userId}?shelf={shelf}`
    /// -- shelf membership, ratings, read dates.
    let getShelf (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<GoodreadsShelfItem list, GoodreadsError>> =
        async {
            let! result = fetchFeed httpClient userId shelf
            return result |> Result.map (fun f -> f.Items)
        }

    /// The channel `<title>` of the feed (e.g. "Marco's bookshelf:
    /// currently-reading") -- `testGoodreadsConnection`'s profile name.
    let getProfileName (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<string, GoodreadsError>> =
        async {
            let! result = fetchFeed httpClient userId shelf
            return result |> Result.map (fun f -> f.ChannelTitle)
        }

    /// Fixed, user-facing text for each error case -- `goodreads_last_error`
    /// stores exactly this for `ProfilePrivateOrUnknown` (this task's own
    /// acceptance criterion).
    let describeError (err: GoodreadsError) : string =
        match err with
        | ProfilePrivateOrUnknown -> "profile private or user id unknown"
        | FeedUnavailable msg -> sprintf "Goodreads feed unavailable: %s" msg
        | ParseFailed msg -> sprintf "Goodreads feed could not be parsed: %s" msg
