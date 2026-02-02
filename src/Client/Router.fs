module Mediatheca.Client.Router

open Feliz.Router

type Page =
    | Dashboard
    | MovieList
    | MovieDetail of slug: string
    | FriendList
    | FriendDetail of slug: string
    | Settings
    | NotFound

module Route =
    let parseUrl (segments: string list) =
        match segments with
        | [] -> Dashboard
        | [ "movies" ] -> MovieList
        | [ "movies"; slug ] -> MovieDetail slug
        | [ "friends" ] -> FriendList
        | [ "friends"; slug ] -> FriendDetail slug
        | [ "settings" ] -> Settings
        | _ -> NotFound

    let toUrl (page: Page) =
        match page with
        | Dashboard -> Router.format ""
        | MovieList -> Router.format "movies"
        | MovieDetail slug -> Router.format ("movies", slug)
        | FriendList -> Router.format "friends"
        | FriendDetail slug -> Router.format ("friends", slug)
        | Settings -> Router.format "settings"
        | NotFound -> Router.format "not-found"

    let isMoviesSection (page: Page) =
        match page with
        | MovieList | MovieDetail _ -> true
        | _ -> false

    let isFriendsSection (page: Page) =
        match page with
        | FriendList | FriendDetail _ -> true
        | _ -> false
