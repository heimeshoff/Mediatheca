module Mediatheca.Tests.GoodreadsTests

/// integration-wmqn3 (ADR-0075): Goodreads is read through its public,
/// key-free shelf RSS feed keyed by the user's Goodreads user id. This suite
/// pins:
/// 1. `parseUserId` -- bare numeric id, `user/show/{id}-slug` URL,
///    `review/list/{id}?shelf=...` URL, and rejection of non-numeric input.
/// 2. The RSS parser decoding a pinned fixture (field names per the
///    research report's live element list) into every field of
///    `GoodreadsShelfItem`, including `user_rating` of `0` -> `None` and a
///    missing ISBN -> `None`.
///
/// No test here makes a live Goodreads call -- every request goes through a
/// stub `HttpMessageHandler`.

open System.Net
open System.Net.Http
open System.Threading
open Expecto
open Mediatheca.Server

// No throttle test lives in this file -- zero it out so the handful of
// sequential calls below don't pay the real 2s adapter-owned gate.
Goodreads.throttleInterval <- System.TimeSpan.Zero

type private AsyncStubHandler(respond: HttpRequestMessage -> Async<HttpResponseMessage>) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Async.StartAsTask(respond request)

let private xmlResponse (statusCode: HttpStatusCode) (xml: string) =
    let resp = new HttpResponseMessage(statusCode)
    resp.Content <- new StringContent(xml, System.Text.Encoding.UTF8, "application/rss+xml")
    resp

/// Two items off the `currently-reading` shelf: the first carries every
/// field the research report's live element list names (a full ISBN/ISBN-13
/// pair, a non-zero `average_rating`, `user_rating=0` -- Goodreads' "not
/// rated" sentinel); the second omits both ISBN elements, `num_pages`,
/// `average_rating` and `book_published` entirely, and carries a real
/// `user_rating` -- pinning "missing -> None" and "0 -> None" as two
/// distinct behaviors.
let private shelfFixture =
    """<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0">
<channel>
<title>Marco's bookshelf: currently-reading</title>
<link>https://www.goodreads.com/review/list_rss/12345678?shelf=currently-reading</link>
<description>Marco's currently-reading shelf</description>
<item>
<guid isPermaLink="false">https://www.goodreads.com/review/show/1111111111</guid>
<pubDate>Mon, 01 Sep 2026 12:00:00 -0800</pubDate>
<title>Project Hail Mary</title>
<link>https://www.goodreads.com/review/show/1111111111?utm_medium=api</link>
<book_id>893415</book_id>
<book_image_url>https://images-na.ssl-images-amazon.com/images/1.jpg</book_image_url>
<book_small_image_url>https://images-na.ssl-images-amazon.com/images/1s.jpg</book_small_image_url>
<book_medium_image_url>https://images-na.ssl-images-amazon.com/images/1m.jpg</book_medium_image_url>
<book_large_image_url>https://images-na.ssl-images-amazon.com/images/1l.jpg</book_large_image_url>
<book_description>A survival story on a dying planet.</book_description>
<num_pages>496</num_pages>
<author_name>Andy Weir</author_name>
<isbn>0593135202</isbn>
<isbn13>9780593135204</isbn13>
<user_name>Marco</user_name>
<user_rating>0</user_rating>
<user_read_at></user_read_at>
<user_date_added>Sun, 31 Aug 2026 00:00:00 -0800</user_date_added>
<user_date_created>Sun, 31 Aug 2026 00:00:00 -0800</user_date_created>
<user_shelves>currently-reading</user_shelves>
<user_review></user_review>
<average_rating>4.42</average_rating>
<book_published>2021</book_published>
<description></description>
</item>
<item>
<guid isPermaLink="false">https://www.goodreads.com/review/show/2222222222</guid>
<pubDate>Tue, 02 Sep 2026 12:00:00 -0800</pubDate>
<title>No ISBN Book</title>
<link>https://www.goodreads.com/review/show/2222222222?utm_medium=api</link>
<book_id>999999</book_id>
<book_image_url></book_image_url>
<book_large_image_url>https://images-na.ssl-images-amazon.com/images/2l.jpg</book_large_image_url>
<num_pages></num_pages>
<author_name>Someone Else</author_name>
<isbn></isbn>
<isbn13></isbn13>
<user_rating>5</user_rating>
<user_read_at></user_read_at>
<user_shelves>currently-reading</user_shelves>
<average_rating></average_rating>
<book_published></book_published>
</item>
</channel>
</rss>"""

[<Tests>]
let goodreadsTests =
    testList "Goodreads adapter (integration-wmqn3)" [

        testList "parseUserId" [
            testCase "accepts a bare numeric id" <| fun _ ->
                Expect.equal (Goodreads.parseUserId "12345678") (Ok "12345678") "bare numeric id"

            testCase "accepts a user/show/{id}-slug URL" <| fun _ ->
                Expect.equal
                    (Goodreads.parseUserId "https://www.goodreads.com/user/show/12345678-marco")
                    (Ok "12345678")
                    "user/show URL"

            testCase "accepts a review/list/{id}?shelf=... URL without a scheme" <| fun _ ->
                Expect.equal
                    (Goodreads.parseUserId "goodreads.com/review/list/12345678?shelf=read")
                    (Ok "12345678")
                    "review/list URL"

            testCase "rejects non-numeric input" <| fun _ ->
                match Goodreads.parseUserId "not-a-goodreads-id" with
                | Error _ -> ()
                | Ok id -> failtestf "Expected Error, got Ok %s" id
        ]

        testCase "the RSS parser decodes every field, user_rating=0 -> None, missing ISBN -> None" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return xmlResponse HttpStatusCode.OK shelfFixture })
            let httpClient = new HttpClient(handler)

            let items =
                match Goodreads.getShelf httpClient "12345678" "currently-reading" |> Async.RunSynchronously with
                | Ok items -> items
                | Error e -> failtestf "Expected Ok, got Error %A" e

            Expect.equal (List.length items) 2 "both items parsed"
            let first = items.[0]
            Expect.equal first.BookId "893415" "book_id"
            Expect.equal first.Title "Project Hail Mary" "title"
            Expect.equal first.Author "Andy Weir" "author_name"
            Expect.equal first.Isbn (Some "0593135202") "isbn"
            Expect.equal first.Isbn13 (Some "9780593135204") "isbn13"
            Expect.equal first.ImageUrl (Some "https://images-na.ssl-images-amazon.com/images/1.jpg") "book_image_url"
            Expect.equal first.LargeImageUrl (Some "https://images-na.ssl-images-amazon.com/images/1l.jpg") "book_large_image_url"
            Expect.equal first.NumPages (Some 496) "num_pages"
            Expect.equal first.AverageRating (Some 4.42) "average_rating"
            Expect.equal first.UserRating None "user_rating of 0 decodes to None, never Some 0"
            Expect.equal first.Published (Some 2021) "book_published"
            Expect.equal first.DateAdded (Some "Sun, 31 Aug 2026 00:00:00 -0800") "user_date_added"
            Expect.equal first.ReadAt None "empty user_read_at decodes to None"
            Expect.equal first.Shelves [ "currently-reading" ] "user_shelves"

            let second = items.[1]
            Expect.equal second.Isbn None "missing isbn element -> None"
            Expect.equal second.Isbn13 None "missing isbn13 element -> None"
            Expect.equal second.NumPages None "missing num_pages -> None"
            Expect.equal second.AverageRating None "missing average_rating -> None"
            Expect.equal second.Published None "missing book_published -> None"
            Expect.equal second.UserRating (Some 5) "a genuine non-zero user_rating decodes to Some"

        testCase "getProfileName returns the channel title" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return xmlResponse HttpStatusCode.OK shelfFixture })
            let httpClient = new HttpClient(handler)
            match Goodreads.getProfileName httpClient "12345678" "currently-reading" |> Async.RunSynchronously with
            | Ok name -> Expect.equal name "Marco's bookshelf: currently-reading" "channel title"
            | Error e -> failtestf "Expected Ok, got Error %A" e

        testCase "a 403 response maps to ProfilePrivateOrUnknown" <| fun _ ->
            let handler = new AsyncStubHandler(fun _ -> async { return xmlResponse HttpStatusCode.Forbidden "" })
            let httpClient = new HttpClient(handler)
            match Goodreads.getShelf httpClient "12345678" "currently-reading" |> Async.RunSynchronously with
            | Error Goodreads.ProfilePrivateOrUnknown -> ()
            | other -> failtestf "Expected Error ProfilePrivateOrUnknown, got %A" other
    ]
    |> testSequenced
