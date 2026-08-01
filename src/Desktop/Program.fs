/// Photino.NET desktop shell prototype (infrastructure-w8fnp). Hosts the
/// existing Giraffe/Kestrel server in-process (via the shared
/// Mediatheca.Server.Composition.buildApp composition root — no duplicated
/// setup vs. the Docker entry point in src/Server/Program.fs) and opens a
/// native webview window pointed at it.
///
/// Binding: loopback-only (127.0.0.1), on a free ephemeral port. The app has
/// no authentication (ADR-0007), so a desktop server must never be reachable
/// from the network — this is a hard constraint, not a preference.
module Mediatheca.Desktop.Program

open System
open System.Net
open System.Net.Sockets
open Photino.NET
open Mediatheca.Server

/// Bind a TCP listener to loopback port 0 (OS picks a free ephemeral port),
/// read back the assigned port, then release it immediately so Kestrel can
/// bind it. There is a small theoretical race (another process could grab
/// the port between release and Kestrel's bind) — acceptable for a
/// single-user local desktop app and the standard pattern for this problem
/// when the socket itself can't be handed directly to Kestrel.
let private findFreeLoopbackPort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

// WebView2 is COM-based: CreateCoreWebView2EnvironmentWithOptions must be
// called from a single-threaded apartment. .NET defaults the main thread to
// MTA, and without this attribute Photino still creates the native window but
// the webview never attaches to it — the window renders as a black rectangle,
// no msedgewebview2 child process spawns, and no WebView2 user-data folder is
// created. The failure is silent (the COM error is swallowed inside Photino's
// async environment-creation handler), so this attribute is load-bearing.
[<EntryPoint; STAThread>]
let main args =
    // Composition.buildApp resolves `deploy/public` relative to the process's
    // current directory (see Composition.fs), and self-contained publish
    // output can be launched from any working directory (double-click,
    // shortcut, etc.) — anchor to the executable's own directory so the
    // bundled client assets are always found.
    Environment.CurrentDirectory <- AppContext.BaseDirectory

    let port = findFreeLoopbackPort ()
    let url = sprintf "http://127.0.0.1:%d" port

    let app = Composition.buildApp args (Some url)
    app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously

    eprintfn "[Desktop] Server listening on %s (loopback only)" url

    let window =
        (new PhotinoWindow())
            .SetTitle("Mediatheca")
            .SetUseOsDefaultSize(false)
            .SetSize(1400, 900)
            .Center()
            .Load(url)

    // Blocks until the native window is closed by the user.
    window.WaitForClose()

    app.StopAsync() |> Async.AwaitTask |> Async.RunSynchronously
    0
