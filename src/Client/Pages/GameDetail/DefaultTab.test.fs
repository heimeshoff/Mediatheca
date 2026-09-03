/// games-t69rb: MVU coverage for the game-detail page's default-tab rule —
/// `State.update`'s `Game_loaded` case must land on Journal only on the
/// page's first load for a game (when `HasJournalContent` says so), and must
/// never override a tab the builder already picked by hand on the refreshes
/// that follow every command. `Game_loaded` never touches the `api`
/// parameter, so a `Unchecked.defaultof<IMediathecaApi>` stand-in is safe
/// here without faking ~100 unrelated RPC fields.
module Mediatheca.Client.Pages.GameDetail.DefaultTabTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.GameDetail.Types
open Mediatheca.Client.Pages.GameDetail.State

/// Fills every field `Game_loaded`'s default-tab decision doesn't look at
/// with a neutral default, so each test case reads as its scenario.
let private gameDetail (hasJournalContent: bool) : GameDetail =
    { Slug = "some-game"
      Name = "Some Game"
      Year = 2026
      Description = ""
      ShortDescription = ""
      WebsiteUrl = None
      CoverRef = None
      BackdropRef = None
      Genres = []
      Status = Backlog
      RawgId = None
      RawgRating = None
      HltbHours = Some 10.0 // avoids the Fetch_hltb follow-up command in this test
      HltbMainPlusHours = None
      HltbCompletionistHours = None
      PersonalRating = None
      SteamAppId = None
      SteamLibraryDate = None
      SteamLastPlayed = None
      TotalPlayTimeMinutes = 0
      PriorPlayTimeMinutes = 0
      PlayFacets =
        { Solo = false
          CoopCouch = false
          CoopOnline = false
          VersusCouch = false
          VersusOnline = false
          RemotePlayTogether = false
          Vr = NoVr }
      PlayFacetsOverride =
        { Solo = None
          CoopCouch = None
          CoopOnline = None
          VersusCouch = None
          VersusOnline = None
          RemotePlayTogether = None
          Vr = None }
      DeckCompat = Unknown
      ReleaseDate =
        { Raw = ""
          Parsed = None
          ComingSoon = false
          IsUnreleased = false }
      IsOwnedByMe = false
      FamilyOwners = []
      RecommendedBy = []
      WantToPlayWith = []
      PlayedWith = []
      ContentBlocks = []
      HasJournalContent = hasJournalContent }

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>

let defaultTabTests =
    testList "games-t69rb: GameDetail.State.update Game_loaded default-tab rule" [

        testCase "(1) first load, journal has content -> lands on Journal" <| fun () ->
            let model, _ = init "some-game"
            let updated, _ = update fakeApi (Game_loaded (Some (gameDetail true))) model
            Expect.equal updated.ActiveTab Journal "a game with journal content opens Journal-first"

        testCase "(2) first load, journal is empty -> lands on Overview" <| fun () ->
            let model, _ = init "some-game"
            let updated, _ = update fakeApi (Game_loaded (Some (gameDetail false))) model
            Expect.equal updated.ActiveTab Overview "a game with no journal content opens on Overview"

        testCase "(3) a hand-picked tab survives a same-game refresh (e.g. after a status change)" <| fun () ->
            let model, _ = init "some-game"
            // First load lands on Journal (journal has content)...
            let afterFirstLoad, _ = update fakeApi (Game_loaded (Some (gameDetail true))) model
            // ...the builder switches to Overview by hand...
            let afterManualPick, _ = update fakeApi (Set_tab Overview) afterFirstLoad
            // ...then a command (status change, rating, etc.) re-fetches the same game.
            let afterRefresh, _ = update fakeApi (Game_loaded (Some (gameDetail true))) afterManualPick
            Expect.equal afterRefresh.ActiveTab Overview "the manually-picked tab must survive the refresh, even though the journal still has content"

        testCase "(4) navigating to a different game (fresh init) re-applies the default-tab rule" <| fun () ->
            let model, _ = init "some-game"
            let afterFirstLoad, _ = update fakeApi (Game_loaded (Some (gameDetail true))) model
            let afterManualPick, _ = update fakeApi (Set_tab Overview) afterFirstLoad
            Expect.equal afterManualPick.ActiveTab Overview "sanity: manual pick took effect before navigating away"

            // Navigating to a new slug goes through `init` again (see root
            // State.fs), which resets `Game` to `None` — the next
            // `Game_loaded` is a first load again for the new game.
            let nextGameModel, _ = init "another-game"
            let nextGameLoaded, _ = update fakeApi (Game_loaded (Some (gameDetail false))) nextGameModel
            Expect.equal nextGameLoaded.ActiveTab Overview "the new game's own default (no journal content) applies, independent of the previous game's tab"
    ]

Mocha.runTests defaultTabTests |> ignore
