module Mediatheca.Client.Router

open Feliz.Router

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
    /// Stream drill-in (administration-v4y9g): full history + current
    /// projection state for one event stream. Every other admin-console
    /// surface dissolved into inline sections on Settings
    /// (administration-k3vmt) — this stays a top-level `Page` case (like the
    /// other *_detail pages) because it's parameterized (one page per
    /// stream) and can't be a fixed section.
    | Stream_detail of streamId: string
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
        | [ "admin"; "streams"; streamId ] -> Stream_detail streamId
        // The /admin console dissolved into inline sections on Settings
        // (administration-k3vmt) — every former admin URL, including the six
        // per-tab segments and the legacy /events alias, resolves to Settings.
        // Settings has no section-level route: none of these carry which
        // section to open (see Settings.State for the lazy-load/expand
        // convention that replaces per-tab deep-linking).
        | [ "admin" ] -> Settings
        | [ "admin"; "events" ] -> Settings
        | [ "admin"; "projections" ] -> Settings
        | [ "admin"; "health" ] -> Settings
        | [ "admin"; "images" ] -> Settings
        | [ "admin"; "jobs" ] -> Settings
        | [ "admin"; "surgery" ] -> Settings
        | [ "events" ] -> Settings
        | [ "settings" ] -> Settings
        | [ "styleguide" ] -> Styleguide
        | _ -> Not_found

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
        | Stream_detail streamId -> Router.format ("admin", "streams", streamId)
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
        | Stream_detail streamId -> Router.navigate ("admin", "streams", streamId)
        | Settings -> Router.navigate "settings"
        | Styleguide -> Router.navigate "styleguide"
        | Not_found -> Router.navigate "not-found"

    /// The sidebar's single Settings nav item stays highlighted while on a
    /// stream drill-in too, since Stream_detail is Settings' own "drill
    /// deeper" page (reached from the Events section) rather than an
    /// unrelated destination (administration-k3vmt, formerly isAdminSection).
    let isSettingsSection (page: Page) =
        match page with
        | Settings | Stream_detail _ -> true
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
