module Mediatheca.Client.Router

open Feliz.Router

type Page =
    | Dashboard
    | Movies
    | Friends
    | Catalog
    | Settings
    | NotFound

module Route =
    let parseUrl (segments: string list) =
        match segments with
        | [] -> Dashboard
        | [ "movies" ] -> Movies
        | [ "friends" ] -> Friends
        | [ "catalog" ] -> Catalog
        | [ "settings" ] -> Settings
        | _ -> NotFound

    let toUrl (page: Page) =
        match page with
        | Dashboard -> Router.format ""
        | Movies -> Router.format "movies"
        | Friends -> Router.format "friends"
        | Catalog -> Router.format "catalog"
        | Settings -> Router.format "settings"
        | NotFound -> Router.format "not-found"
