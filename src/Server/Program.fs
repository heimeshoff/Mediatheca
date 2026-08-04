module Mediatheca.Server.Program

[<EntryPoint>]
let main args =
    match args |> Array.toList with
    // administration-z6ymt: the offline NDJSON filter's CLI entry — runnable
    // by the operator on a laptop against an exported file, no `DATA_DIR`,
    // no Giraffe host, no SqliteConnection ever opened. See
    // `docs/runbooks/purge-demoted-metadata-events.md`.
    | "filter-demoted-events" :: inputPath :: outputPath :: _ ->
        EventLogFilter.runCli inputPath outputPath
        0
    | _ ->
        // No URL override here: Docker sets ASPNETCORE_URLS itself (see Dockerfile),
        // and `dotnet run` / dev falls back to Kestrel's own defaults. The desktop
        // shell (src/Desktop/Program.fs) is the caller that overrides binding, via
        // the same Composition.buildApp entry point.
        let app = Composition.buildApp args None
        app.Run()
        0
