// Spike harness (integration-ygwsa) — UNEXECUTED, see README.md in this folder.
//
// One-time interactive QR login via SteamKit2, requesting a long-lived
// ("persistent") refresh token scoped to the MobileApp platform (see
// README.md for why MobileApp rather than the SteamKit2-sample default of
// SteamClient). Prints the QR challenge URL for the user to scan with the
// Steam mobile app, then persists the resulting refresh token to a local
// file. Never committed; never logged to a shared location.
//
// Run: dotnet fsi login.fsx

#r "nuget: SteamKit2, 3.1.0"
#r "nuget: QRCoder, 1.6.0"

open System
open QRCoder
open SteamKit2
open SteamKit2.Authentication
open SteamKit2.Internal

let refreshTokenPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "refresh-token.local.txt")

let qrPngPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "qr.local.png")

// The challenge URL is a QR payload for the Steam mobile app's scanner —
// opening it in a desktop browser just lands on Steam's install page.
// Render it as a PNG the user can scan off the screen.
let writeQrPng (url: string) =
    use generator = new QRCodeGenerator()
    use qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q)
    let png = PngByteQRCode(qrData).GetGraphic(20)
    System.IO.File.WriteAllBytes(qrPngPath, png)
    printfn "QR written to %s — open it and scan with the Steam mobile app (Steam Guard / shield icon > scan)." qrPngPath

let run () =
    async {
        let steamClient = new SteamClient()

        // The QR/credentials auth handshake itself is expected to run over
        // plain HTTPS (WebApiTransport) once PlatformType is MobileApp/WebBrowser
        // rather than SteamClient (research finding — not independently
        // verified against SteamKit2's own transport-selection source, only
        // inferred from the sibling node-steam-session library). We still
        // connect the CM client here because the SteamKit2 API surface for
        // `.Authentication` is exposed as a handler on a connected
        // `SteamClient` instance in the current SDK; if this turns out to be
        // unnecessary, dropping `Connect()` would make the harness fully
        // CM-connection-free.
        let manager = new CallbackManager(steamClient)
        let mutable connected = false
        let onConnected = manager.Subscribe<SteamClient.ConnectedCallback>(fun _ -> connected <- true)
        steamClient.Connect()

        while not connected do
            manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100.0)) |> ignore

        let authDetails =
            AuthSessionDetails(
                PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                IsPersistentSession = true,
                ClientOSType = EOSType.Android9
            )

        let! authSession =
            steamClient.Authentication.BeginAuthSessionViaQRAsync(authDetails)
            |> Async.AwaitTask

        writeQrPng authSession.ChallengeURL
        authSession.ChallengeURLChanged <-
            Action(fun () ->
                printfn "QR rotated — the PNG has been rewritten, rescan if you missed the last one."
                writeQrPng authSession.ChallengeURL)

        let! pollResponse = authSession.PollingWaitForResultAsync() |> Async.AwaitTask

        printfn "Logged in as: %s" pollResponse.AccountName
        printfn "Refresh token acquired (length %d chars) — writing to %s" pollResponse.RefreshToken.Length refreshTokenPath

        System.IO.File.WriteAllText(refreshTokenPath, pollResponse.RefreshToken)
        printfn "Done. DO NOT COMMIT refresh-token.local.txt — it is a long-lived credential."

        steamClient.Disconnect()
    }

run () |> Async.RunSynchronously
