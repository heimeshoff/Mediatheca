module Mediatheca.Tests.JellyfinItemTests

/// integration-r4vzm (ADR-0071): `Jellyfin.getItemWithReauth` /
/// `deleteItemWithReauth`, the two additions "Remove local copy" needs on
/// top of the ADR-0011 self-healing fetch. Both are built on the already
/// pinned `withReauthRetry` (`JellyfinReauthTests.fs`, left untouched) --
/// this file only covers the new 404 unfolding and the fact that a 401 still
/// re-authenticates and retries exactly once, same as every other fetch.

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Mediatheca.Server
open Mediatheca.Server.Jellyfin

/// Routes a fake server's responses by (method, path), tracking how many
/// times each has been hit -- lets a test answer 401 on the first GET/DELETE
/// and 200 on the retry, exactly like a real token rejection.
type private FakeJellyfin(respond: HttpMethod -> string -> int -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let hitCounts = System.Collections.Generic.Dictionary<string, int>()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        let path = request.RequestUri.AbsolutePath
        let key = sprintf "%s %s" (request.Method.Method) path
        let hit = (hitCounts.TryGetValue key |> function true, v -> v | false, _ -> 0) + 1
        hitCounts.[key] <- hit
        Task.FromResult<HttpResponseMessage>(respond request.Method path hit)

let private jsonResponse (status: HttpStatusCode) (json: string) =
    let resp = new HttpResponseMessage(status)
    resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    resp

let private authOkJson =
    """{"AccessToken":"fresh-token","User":{"Id":"uid-1","Name":"tester"}}"""

let private itemJson (id: string) =
    sprintf """{"Id":"%s","Name":"Dune","Type":"Movie","Path":"/media/movies/Dune (2021)/Dune.2021.mkv"}""" id

let private config: JellyfinConfig = {
    ServerUrl = "http://jellyfin.local:8096"
    Username = "admin"
    Password = "secret"
    UserId = "uid-0"
    AccessToken = "stale-token"
}

[<Tests>]
let jellyfinItemTests =
    testList "Jellyfin.getItemWithReauth / deleteItemWithReauth (integration-r4vzm)" [

        testCase "getItemWithReauth: 200 -> Ok (Some item), Path decoded" <| fun _ ->
            let handler = new FakeJellyfin(fun _method path _hit ->
                if path.Contains("/Items/") then jsonResponse HttpStatusCode.OK (itemJson "jf-1")
                else jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let mutable persisted = []
            let result = getItemWithReauth http config (fun a -> persisted <- persisted @ [a.AccessToken]) "jf-1" |> Async.RunSynchronously
            match result with
            | Ok (Some item) ->
                Expect.equal item.Id "jf-1" "Decoded the item id"
                Expect.equal item.Path (Some "/media/movies/Dune (2021)/Dune.2021.mkv") "Decoded Path"
            | other -> failtestf "Expected Ok (Some item), got %A" other
            Expect.isEmpty persisted "No re-auth needed on a clean 200"

        testCase "getItemWithReauth: 404 -> Ok None (already gone in Jellyfin)" <| fun _ ->
            let handler = new FakeJellyfin(fun _method path _hit ->
                if path.Contains("/Items/") then jsonResponse HttpStatusCode.NotFound ""
                else jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let result = getItemWithReauth http config (fun _ -> ()) "jf-gone" |> Async.RunSynchronously
            Expect.equal result (Ok None) "404 unfolds to Ok None"

        testCase "getItemWithReauth: 401 then success -- re-authenticates once, persists, retries once" <| fun _ ->
            let handler = new FakeJellyfin(fun method path hit ->
                if path.Contains("/Items/") then
                    if hit = 1 then jsonResponse HttpStatusCode.Unauthorized ""
                    else jsonResponse HttpStatusCode.OK (itemJson "jf-2")
                else
                    jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let mutable persisted = []
            let result = getItemWithReauth http config (fun a -> persisted <- persisted @ [a.AccessToken]) "jf-2" |> Async.RunSynchronously
            match result with
            | Ok (Some item) -> Expect.equal item.Id "jf-2" "Retried and decoded the item after re-auth"
            | other -> failtestf "Expected Ok (Some item) after re-auth, got %A" other
            Expect.equal persisted [ "fresh-token" ] "Persisted the fresh token exactly once"

        testCase "deleteItemWithReauth: 200 -> Ok ()" <| fun _ ->
            let handler = new FakeJellyfin(fun _method path _hit ->
                if path.Contains("/Items/") then jsonResponse HttpStatusCode.OK ""
                else jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let result = deleteItemWithReauth http config (fun _ -> ()) "jf-3" |> Async.RunSynchronously
            Expect.equal result (Ok ()) "A successful DELETE is Ok ()"

        testCase "deleteItemWithReauth: 404 -> Ok () (already gone, idempotent)" <| fun _ ->
            let handler = new FakeJellyfin(fun _method path _hit ->
                if path.Contains("/Items/") then jsonResponse HttpStatusCode.NotFound ""
                else jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let result = deleteItemWithReauth http config (fun _ -> ()) "jf-gone" |> Async.RunSynchronously
            Expect.equal result (Ok ()) "404 unfolds to Ok () -- deleting an already-gone item is a success"

        testCase "deleteItemWithReauth: 401 then success -- re-authenticates once, persists, retries once" <| fun _ ->
            let handler = new FakeJellyfin(fun method path hit ->
                if path.Contains("/Items/") then
                    if hit = 1 then jsonResponse HttpStatusCode.Unauthorized ""
                    else jsonResponse HttpStatusCode.OK ""
                else
                    jsonResponse HttpStatusCode.OK authOkJson)
            use http = new HttpClient(handler)
            let mutable persisted = []
            let result = deleteItemWithReauth http config (fun a -> persisted <- persisted @ [a.AccessToken]) "jf-4" |> Async.RunSynchronously
            Expect.equal result (Ok ()) "Retried and succeeded after re-auth"
            Expect.equal persisted [ "fresh-token" ] "Persisted the fresh token exactly once"
    ]
