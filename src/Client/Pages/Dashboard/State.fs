module Mediatheca.Client.Pages.Dashboard.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.Dashboard.Types

let init () : Model * Cmd<Msg> =
    { Placeholder = "Welcome to Mediatheca"
      MovieCount = 0
      FriendCount = 0
      RecentMovies = []
      IsLoading = true },
    Cmd.none

let update (api: IMediathecaApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | NoOp ->
        { model with IsLoading = false }, Cmd.none
    | Movies_loaded movies ->
        let recent = movies |> List.truncate 4
        { model with MovieCount = List.length movies; RecentMovies = recent; IsLoading = false }, Cmd.none
    | Friends_loaded friends ->
        { model with FriendCount = List.length friends }, Cmd.none
