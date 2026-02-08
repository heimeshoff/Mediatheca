module Mediatheca.Client.Router

open Feliz.Router

type Page =
    | Dashboard
    | Movie_list
    | Movie_detail of slug: string
    | Friend_list
    | Friend_detail of slug: string
    | Settings
    | Not_found

module Route =
    let parseUrl (segments: string list) =
        match segments with
        | [] -> Dashboard
        | [ "movies" ] -> Movie_list
        | [ "movies"; slug ] -> Movie_detail slug
        | [ "friends" ] -> Friend_list
        | [ "friends"; slug ] -> Friend_detail slug
        | [ "settings" ] -> Settings
        | _ -> Not_found

    let toUrl (page: Page) =
        match page with
        | Dashboard -> Router.format ""
        | Movie_list -> Router.format "movies"
        | Movie_detail slug -> Router.format ("movies", slug)
        | Friend_list -> Router.format "friends"
        | Friend_detail slug -> Router.format ("friends", slug)
        | Settings -> Router.format "settings"
        | Not_found -> Router.format "not-found"

    let isMoviesSection (page: Page) =
        match page with
        | Movie_list | Movie_detail _ -> true
        | _ -> false

    let isFriendsSection (page: Page) =
        match page with
        | Friend_list | Friend_detail _ -> true
        | _ -> false
