namespace Mediatheca.Shared

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type IMediathecaApi = {
    healthCheck: unit -> Async<string>
}
