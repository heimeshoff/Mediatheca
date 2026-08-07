namespace Mediatheca.Server

open System
open System.Threading.Tasks
open QRCoder
open SteamKit2
open SteamKit2.Authentication
open SteamKit2.Internal

/// One-time "Connect Steam" QR login (integration-hebjs) — the SteamKit2
/// half of ADR-0019's now-proven-live path. Ongoing family access-token
/// refresh never touches SteamKit2 or a CM connection (see
/// `Steam.mintFamilyAccessToken`, a plain HTTP POST); this module exists
/// solely to mint the long-lived refresh token once, via the interactive QR
/// ceremony the integration-hebjs builder gate proved end-to-end against the
/// real Steam network. Mirrors `spikes/steam-family-token-spike/login.fsx`
/// (the proven-live reference this was built from) — API deltas vs. the
/// SteamKit2 samples/docs, confirmed by that gate run: `CallbackManager` is
/// not `IDisposable`; `QrAuthSession.ChallengeURLChanged` is an `Action`
/// *property* (assign with `<-`), not a .NET event; the platform enum lives
/// at `SteamKit2.Internal.EAuthTokenPlatformType`.
module SteamConnect =

    /// A "Connect Steam" session's current state, polled by the SSE handler
    /// in `Api.fs`. `AwaitingScan` carries a PNG data URL the Settings UI
    /// renders directly as an `<img>` — the challenge URL itself is a QR
    /// payload for the Steam mobile app's scanner, not a link: opening it in
    /// a desktop browser lands on Steam's install page (gate-run finding).
    type ConnectStatus =
        | AwaitingScan of qrImageDataUrl: string
        | Connected of refreshToken: string
        | ConnectFailed of string

    /// In-memory only — a session id is meaningless after a server restart
    /// (the SSE handler that started it is also gone by then), and the
    /// resulting refresh token is persisted by the caller (Api.fs,
    /// SettingsStore) the moment the session reaches `Connected`, not kept
    /// here.
    let private sessions = System.Collections.Concurrent.ConcurrentDictionary<string, ConnectStatus ref>()

    /// How long to wait for the initial CM connection before giving up.
    let private connectTimeout = TimeSpan.FromSeconds(20.0)

    /// How long to keep a QR session open awaiting a scan. The QR itself
    /// rotates roughly every 30s (`ChallengeURLChanged`); this is the outer
    /// bound on the whole ceremony, comfortably longer than a user needs to
    /// find their phone and scan.
    let private pollTimeout = TimeSpan.FromMinutes(5.0)

    let private renderQrDataUrl (challengeUrl: string) : string =
        use generator = new QRCodeGenerator()
        use qrData = generator.CreateQrCode(challengeUrl, QRCodeGenerator.ECCLevel.Q)
        let png = (new PngByteQRCode(qrData)).GetGraphic(10)
        "data:image/png;base64," + Convert.ToBase64String(png)

    /// Kicks off a new QR login session in the background (connecting to
    /// Steam and beginning the QR auth handshake can take a couple of
    /// seconds) and returns its id immediately. Poll `status sessionId` for
    /// updates until it reaches `Connected` or `ConnectFailed`.
    let startConnect () : string =
        let sessionId = Guid.NewGuid().ToString("N")
        let status = ref (AwaitingScan "")
        sessions.[sessionId] <- status

        Task.Run(fun () ->
            try
                let steamClient = new SteamClient()
                let manager = new CallbackManager(steamClient)
                let mutable connected = false
                manager.Subscribe<SteamClient.ConnectedCallback>(fun _ -> connected <- true) |> ignore
                steamClient.Connect()

                let connectDeadline = DateTime.UtcNow + connectTimeout
                while not connected && DateTime.UtcNow < connectDeadline do
                    manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100.0)) |> ignore

                if not connected then
                    status.Value <- ConnectFailed "Could not connect to Steam"
                else
                    // Requesting the MobileApp platform with a persistent
                    // session is deliberate, not the SteamKit2-sample default
                    // (SteamClient platform): a SteamClient-platform token
                    // needs an authenticated CM connection to refresh, which
                    // is exactly what the ongoing refresh path
                    // (`Steam.mintFamilyAccessToken`) avoids (ADR-0019).
                    let authDetails =
                        AuthSessionDetails(
                            PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                            IsPersistentSession = true,
                            ClientOSType = EOSType.Android9)

                    let authSession =
                        steamClient.Authentication.BeginAuthSessionViaQRAsync(authDetails)
                            .GetAwaiter().GetResult()

                    status.Value <- AwaitingScan (renderQrDataUrl authSession.ChallengeURL)
                    authSession.ChallengeURLChanged <-
                        Action(fun () -> status.Value <- AwaitingScan (renderQrDataUrl authSession.ChallengeURL))

                    let pollTask = authSession.PollingWaitForResultAsync()
                    if pollTask.Wait(pollTimeout) then
                        status.Value <- Connected pollTask.Result.RefreshToken
                    else
                        status.Value <- ConnectFailed "QR code expired — start Connect Steam again"

                    steamClient.Disconnect()
            with
            | :? AggregateException as agg -> status.Value <- ConnectFailed (agg.GetBaseException().Message)
            | ex -> status.Value <- ConnectFailed ex.Message
        )
        |> ignore

        sessionId

    /// Current state of a session started by `startConnect`, or `None` if
    /// the session id is unknown (never started, or the server restarted
    /// since).
    let status (sessionId: string) : ConnectStatus option =
        match sessions.TryGetValue(sessionId) with
        | true, s -> Some s.Value
        | _ -> None
