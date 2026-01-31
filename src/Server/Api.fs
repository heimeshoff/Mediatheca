namespace Mediatheca.Server

open Mediatheca.Shared

module Api =

    let mediathecaApi: IMediathecaApi = {
        healthCheck = fun () -> async { return "Mediatheca is running" }
    }
