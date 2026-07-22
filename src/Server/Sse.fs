namespace Mediatheca.Server

/// Shared Server-Sent-Events frame-building (administration-h4k2p). Every SSE
/// handler in this codebase (`Api.steamFamilyImportHandler`,
/// `Administration.importEventsStreamHandler`,
/// `Administration.projectionRebuildStreamHandler`) used to build its own
/// `data: {"type":"...",...}\n\n` line inline via trimming the payload's
/// outer braces before splicing it into the frame. That trim reduces the
/// empty-object payload `"{}"` to `""`, and the unconditional comma in the
/// old inline format string produced `data: {"type":"complete",}` — a
/// trailing comma that is invalid JSON and makes the client's `JSON.parse`
/// throw (surfacing every successful projection rebuild as a false failure).
/// `sseFrame` is the single pure home for this framing so the empty-payload
/// case can never regress at any call site; each handler's `writeEvent` is
/// now just a thin task wrapper around this function that UTF-8 encodes,
/// writes, and flushes.
module Sse =

    let sseFrame (eventType: string) (json: string) : string =
        let body = json.TrimStart('{').TrimEnd('}')
        if body = "" then
            sprintf "data: {\"type\":\"%s\"}\n\n" eventType
        else
            sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType body
