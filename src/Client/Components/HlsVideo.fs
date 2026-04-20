module Mediatheca.Client.Components.HlsVideo

open Feliz
open Fable.Core
open Fable.Core.JsInterop
open Browser.Types

// Steam now serves trailers as HLS (.m3u8) only. Safari plays HLS natively;
// Chrome/Firefox need hls.js to translate via MSE. Direct MP4/WebM URLs also
// work with this component — we set them as `src` directly and skip hls.js.

[<ReactComponent>]
let view (url: string) (className: string) (handleError: unit -> unit) =
    let videoRef = React.useRef<HTMLElement option> None

    React.useEffect((fun () ->
        match videoRef.current with
        | None ->
            { new System.IDisposable with member _.Dispose() = () }
        | Some el ->
            let video = el
            let isHls = url.Contains(".m3u8")
            let nativeHls : string =
                emitJsExpr video "$0.canPlayType('application/vnd.apple.mpegurl')"
            let mutable hlsInstance : obj option = None

            if not isHls then
                emitJsStatement (video, url) "$0.src = $1"
            elif nativeHls <> "" then
                emitJsStatement (video, url) "$0.src = $1"
            else
                let HlsCtor : obj = importDefault "hls.js"
                let supported : bool = emitJsExpr HlsCtor "$0.isSupported()"
                if supported then
                    let hls : obj = emitJsExpr HlsCtor "new $0()"
                    hlsInstance <- Some hls
                    emitJsStatement (hls, url) "$0.loadSource($1)"
                    emitJsStatement (hls, video) "$0.attachMedia($1)"
                    emitJsStatement (hls, HlsCtor) "$0.on($1.Events.ERROR, (e, data) => { if (data && data.fatal) { $0.destroy(); } })"
                else
                    handleError ()

            { new System.IDisposable with
                member _.Dispose() =
                    match hlsInstance with
                    | Some hls -> emitJsStatement hls "$0.destroy()"
                    | None -> ()
                    emitJsStatement video "try { $0.pause(); $0.removeAttribute('src'); $0.load(); } catch (_) {}"
            }
    ), [| url :> obj |])

    Html.video [
        prop.ref (fun el ->
            if isNull el then videoRef.current <- None
            else videoRef.current <- Some (el :?> HTMLElement))
        prop.className className
        prop.controls true
        prop.autoPlay true
        prop.preload.metadata
        prop.custom ("onError", (fun (_: obj) -> handleError ()))
    ]
