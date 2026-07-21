module Mediatheca.Server.Program

[<EntryPoint>]
let main args =
    // No URL override here: Docker sets ASPNETCORE_URLS itself (see Dockerfile),
    // and `dotnet run` / dev falls back to Kestrel's own defaults. The desktop
    // shell (src/Desktop/Program.fs) is the caller that overrides binding, via
    // the same Composition.buildApp entry point.
    let app = Composition.buildApp args None
    app.Run()
    0
