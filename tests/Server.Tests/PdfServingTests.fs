module Mediatheca.Tests.PdfServingTests

/// integration-qqpq9 (ADR-0043/ADR-0089): `Composition.mountPdfStaticFiles`
/// wires the companion-PDF cache tier onto a stable `/pdfs` URL. Exercised
/// here through a minimal `TestServer` (never the full `Composition.
/// buildApp` pipeline -- no DB, no scheduled jobs) so this stays a fast,
/// focused HTTP-level check of the wiring itself: a stored file serves 200
/// with `Content-Type: application/pdf` and no `attachment` disposition; an
/// unknown asin 404s.

open System.IO
open System.Net
open Expecto
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Mediatheca.Server

let private withTempPdfDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-pdf-serving-test-%s" (System.Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

let private testServer (pdfBasePath: string) : TestServer =
    let hostBuilder =
        (new WebHostBuilder())
            .Configure(fun app -> Composition.mountPdfStaticFiles app pdfBasePath)
    new TestServer(hostBuilder)

[<Tests>]
let pdfServingTests =
    testList "Composition.mountPdfStaticFiles (integration-qqpq9, ADR-0043/ADR-0089)" [

        testCase "a stored PDF serves 200, Content-Type application/pdf, and no attachment disposition" <| fun _ ->
            withTempPdfDir (fun pdfBasePath ->
                File.WriteAllBytes(Path.Combine(pdfBasePath, "ASIN123.pdf"), [| byte '%'; byte 'P'; byte 'D'; byte 'F'; 1uy; 2uy |])
                use server = testServer pdfBasePath
                use client = server.CreateClient()

                let response = client.GetAsync("/pdfs/ASIN123.pdf") |> Async.AwaitTask |> Async.RunSynchronously

                Expect.equal response.StatusCode HttpStatusCode.OK "a stored file returns 200"
                Expect.equal (response.Content.Headers.ContentType |> string) "application/pdf" "served with Content-Type: application/pdf"
                Expect.isTrue (isNull response.Content.Headers.ContentDisposition) "no Content-Disposition header -- so nothing forces attachment; the browser's own PDF viewer opens it inline")

        testCase "an unknown asin returns 404" <| fun _ ->
            withTempPdfDir (fun pdfBasePath ->
                use server = testServer pdfBasePath
                use client = server.CreateClient()

                let response = client.GetAsync("/pdfs/UNKNOWN.pdf") |> Async.AwaitTask |> Async.RunSynchronously

                Expect.equal response.StatusCode HttpStatusCode.NotFound "an asin with no stored file 404s")
    ]
