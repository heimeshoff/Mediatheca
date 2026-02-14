module Mediatheca.Client.Router

open Feliz.Router

type Page =
    | Dashboard
    | Movie_list
    | Movie_detail of slug: string
    | Friend_list
    | Friend_detail of slug: string
    | Catalog_list
    | Catalog_detail of slug: string
    | Event_browser
    | Settings
    | Styleguide
    | Not_found

module Route =
    let parseUrl (segments: string list) =
        match segments with
        | [] -> Dashboard
        | [ "movies" ] -> Movie_list
        | [ "movies"; slug ] -> Movie_detail slug
        | [ "friends" ] -> Friend_list
        | [ "friends"; slug ] -> Friend_detail slug
        | [ "catalogs" ] -> Catalog_list
        | [ "catalogs"; slug ] -> Catalog_detail slug
        | [ "events" ] -> Event_browser
        | [ "settings" ] -> Settings
        | [ "styleguide" ] -> Styleguide
        | _ -> Not_found

    let toUrl (page: Page) =
        match page with
        | Dashboard -> Router.format ""
        | Movie_list -> Router.format "movies"
        | Movie_detail slug -> Router.format ("movies", slug)
        | Friend_list -> Router.format "friends"
        | Friend_detail slug -> Router.format ("friends", slug)
        | Catalog_list -> Router.format "catalogs"
        | Catalog_detail slug -> Router.format ("catalogs", slug)
        | Event_browser -> Router.format "events"
        | Settings -> Router.format "settings"
        | Styleguide -> Router.format "styleguide"
        | Not_found -> Router.format "not-found"

    let isMoviesSection (page: Page) =
        match page with
        | Movie_list | Movie_detail _ -> true
        | _ -> false

    let isFriendsSection (page: Page) =
        match page with
        | Friend_list | Friend_detail _ -> true
        | _ -> false

    let isCatalogsSection (page: Page) =
        match page with
        | Catalog_list | Catalog_detail _ -> true
        | _ -> false
