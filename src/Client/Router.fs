module Mediatheca.Client.Router

open Feliz.Router

/// Sub-tabs of the /admin section. URL-addressable — each tab is its own route
/// (/admin/events, /admin/projections, ...) so later admin-console tasks
/// (explorer upgrades, projection dashboard, health, jobs, surgery) slot in
/// without reworking the shell.
type AdminTab =
    | AdminEvents
    | AdminProjections
    | AdminHealth
    | AdminJobs
    | AdminSurgery

type Page =
    | Dashboard
    | Movie_list
    | Movie_detail of slug: string
    | Series_list
    | Series_detail of slug: string
    | Game_list
    | Game_detail of slug: string
    | Friend_list
    | Friend_detail of slug: string
    | Catalog_list
    | Catalog_detail of slug: string
    | Admin of AdminTab
    | Settings
    | Styleguide
    | Not_found

module Route =
    let parseUrl (segments: string list) =
        match segments with
        | [] -> Dashboard
        | [ "movies" ] -> Movie_list
        | [ "movies"; slug ] -> Movie_detail slug
        | [ "series" ] -> Series_list
        | [ "series"; slug ] -> Series_detail slug
        | [ "games" ] -> Game_list
        | [ "games"; slug ] -> Game_detail slug
        | [ "friends" ] -> Friend_list
        | [ "friends"; slug ] -> Friend_detail slug
        | [ "catalogs" ] -> Catalog_list
        | [ "catalogs"; slug ] -> Catalog_detail slug
        | [ "admin" ] -> Admin AdminEvents
        | [ "admin"; "events" ] -> Admin AdminEvents
        | [ "admin"; "projections" ] -> Admin AdminProjections
        | [ "admin"; "health" ] -> Admin AdminHealth
        | [ "admin"; "jobs" ] -> Admin AdminJobs
        | [ "admin"; "surgery" ] -> Admin AdminSurgery
        // Legacy alias — old /events bookmarks still resolve to the Events tab.
        | [ "events" ] -> Admin AdminEvents
        | [ "settings" ] -> Settings
        | [ "styleguide" ] -> Styleguide
        | _ -> Not_found

    let private adminTabSegment (tab: AdminTab) =
        match tab with
        | AdminEvents -> "events"
        | AdminProjections -> "projections"
        | AdminHealth -> "health"
        | AdminJobs -> "jobs"
        | AdminSurgery -> "surgery"

    let toUrl (page: Page) =
        match page with
        | Dashboard -> Router.format ""
        | Movie_list -> Router.format "movies"
        | Movie_detail slug -> Router.format ("movies", slug)
        | Series_list -> Router.format "series"
        | Series_detail slug -> Router.format ("series", slug)
        | Game_list -> Router.format "games"
        | Game_detail slug -> Router.format ("games", slug)
        | Friend_list -> Router.format "friends"
        | Friend_detail slug -> Router.format ("friends", slug)
        | Catalog_list -> Router.format "catalogs"
        | Catalog_detail slug -> Router.format ("catalogs", slug)
        | Admin tab -> Router.format ("admin", adminTabSegment tab)
        | Settings -> Router.format "settings"
        | Styleguide -> Router.format "styleguide"
        | Not_found -> Router.format "not-found"

    let navigateTo (page: Page) =
        match page with
        | Dashboard -> Router.navigate ""
        | Movie_list -> Router.navigate "movies"
        | Movie_detail slug -> Router.navigate ("movies", slug)
        | Series_list -> Router.navigate "series"
        | Series_detail slug -> Router.navigate ("series", slug)
        | Game_list -> Router.navigate "games"
        | Game_detail slug -> Router.navigate ("games", slug)
        | Friend_list -> Router.navigate "friends"
        | Friend_detail slug -> Router.navigate ("friends", slug)
        | Catalog_list -> Router.navigate "catalogs"
        | Catalog_detail slug -> Router.navigate ("catalogs", slug)
        | Admin tab -> Router.navigate ("admin", adminTabSegment tab)
        | Settings -> Router.navigate "settings"
        | Styleguide -> Router.navigate "styleguide"
        | Not_found -> Router.navigate "not-found"

    let isAdminSection (page: Page) =
        match page with
        | Admin _ -> true
        | _ -> false

    let isMoviesSection (page: Page) =
        match page with
        | Movie_list | Movie_detail _ -> true
        | _ -> false

    let isFriendsSection (page: Page) =
        match page with
        | Friend_list | Friend_detail _ -> true
        | _ -> false

    let isSeriesSection (page: Page) =
        match page with
        | Series_list | Series_detail _ -> true
        | _ -> false

    let isGamesSection (page: Page) =
        match page with
        | Game_list | Game_detail _ -> true
        | _ -> false

    let isCatalogsSection (page: Page) =
        match page with
        | Catalog_list | Catalog_detail _ -> true
        | _ -> false
