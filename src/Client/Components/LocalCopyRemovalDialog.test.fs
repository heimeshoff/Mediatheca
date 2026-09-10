/// integration-mqsd3: coverage for the "Remove local copy" phase machine
/// (ADR-0064) -- `update` is driven directly and asserted on the model, the
/// same pattern `FamilyTokenRejected.test.fs` and `SearchModal.test.fs` use.
/// Effects are plain lambdas, never `Unchecked.defaultof<IMediathecaApi>`
/// (that value is `null` in Fable and would throw the moment `update`
/// touched a member).
module Mediatheca.Client.Components.LocalCopyRemovalDialogTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Components.LocalCopyRemovalDialog

let private noopEffects: Effects = {
    Plan = fun _ -> async { return Error "not used in this test" }
    Remove = fun _ -> async { return { Completed = []; Failure = None } }
}

let private torrent (hash: string) (preTicked: bool) : PlannedTorrent = {
    Hash = hash
    Name = $"Torrent {hash}"
    Ratio = 1.5
    SeedingTimeDays = 3.0
    Match = ExactOrInside
    Risk = Safe
    PreTicked = preTicked
}

let private torrentsPresentPlan (torrents: PlannedTorrent list) : RemovalPlan =
    { Path = "/media/movies/dune-2021"; Case = TorrentsPresent; Torrents = torrents }

let private filesOnlyPlan () : RemovalPlan =
    { Path = "/media/movies/dune-2021"; Case = FilesOnly; Torrents = [] }

let private alreadyGonePlan () : RemovalPlan =
    { Path = ""; Case = AlreadyGoneInJellyfin; Torrents = [] }

let private outsideMountPlan () : RemovalPlan =
    { Path = "/mnt/other/dune-2021"; Case = PathOutsideMountMap; Torrents = [] }

let private target = MovieTarget "dune-2021"

let private baseModel (phase: Phase) : Model = { Target = target; Phase = phase }

let localCopyRemovalDialogTests =
    testList "integration-mqsd3: LocalCopyRemovalDialog phase machine" [

        // showRemoveLocalCopy: the pure show-predicate MovieDetail.Views and
        // SeriesDetail.Views both call to decide whether "Remove local copy"
        // shows in the action row — mirrors "Play in Jellyfin"'s condition
        // (MovieDetail.JellyfinId / SeriesDetail.JellyfinId are both
        // `string option`, so one predicate covers both DTOs).
        testCase "showRemoveLocalCopy is true when JellyfinId is Some" <| fun () ->
            Expect.isTrue (showRemoveLocalCopy (Some "jf-1")) "a linked item shows the action"

        testCase "showRemoveLocalCopy is false when JellyfinId is None" <| fun () ->
            Expect.isFalse (showRemoveLocalCopy None) "an unlinked item hides the action"

        testCase "Plan_result (Ok plan) moves Planning to Confirming with ticked = exactly the PreTicked hashes" <| fun () ->
            let plan = torrentsPresentPlan [ torrent "aaa" true; torrent "bbb" false; torrent "ccc" true ]
            let model = baseModel Planning
            let updated, _ = update noopEffects (Plan_result (Ok plan)) model
            match updated.Phase with
            | Confirming (p, ticked) ->
                Expect.equal p plan "the plan lands unchanged"
                Expect.equal ticked (Set.ofList [ "aaa"; "ccc" ]) "ticked = exactly the PreTicked hashes"
            | _ -> failtest "expected Confirming"

        testCase "Plan_result (Error e) moves Planning to PlanFailed e" <| fun () ->
            let model = baseModel Planning
            let updated, _ = update noopEffects (Plan_result (Error "boom")) model
            Expect.equal updated.Phase (PlanFailed "boom") "PlanFailed carries the message"

        testCase "Toggle hash flips exactly one row" <| fun () ->
            let plan = torrentsPresentPlan [ torrent "aaa" true; torrent "bbb" false ]
            let model = baseModel (Confirming (plan, Set.ofList [ "aaa" ]))
            let toggledOff, _ = update noopEffects (Toggle "aaa") model
            match toggledOff.Phase with
            | Confirming (_, ticked) -> Expect.equal ticked Set.empty "aaa was ticked, Toggle un-ticks it"
            | _ -> failtest "expected Confirming"
            let toggledOn, _ = update noopEffects (Toggle "bbb") toggledOff
            match toggledOn.Phase with
            | Confirming (_, ticked) -> Expect.equal ticked (Set.ofList [ "bbb" ]) "bbb was un-ticked, Toggle ticks it"
            | _ -> failtest "expected Confirming"

        testCase "canRemove is false while any TorrentsPresent row is un-ticked, true once all are ticked" <| fun () ->
            let plan = torrentsPresentPlan [ torrent "aaa" true; torrent "bbb" false ]
            Expect.isFalse (canRemove plan (Set.ofList [ "aaa" ])) "bbb is still un-ticked"
            Expect.isTrue (canRemove plan (Set.ofList [ "aaa"; "bbb" ])) "every row is ticked"

        testCase "canRemove is true for FilesOnly with no rows" <| fun () ->
            Expect.isTrue (canRemove (filesOnlyPlan ()) Set.empty) "nothing to acknowledge"

        testCase "canRemove is false for AlreadyGoneInJellyfin and PathOutsideMountMap" <| fun () ->
            Expect.isFalse (canRemove (alreadyGonePlan ()) Set.empty) "dead end: nothing can be removed"
            Expect.isFalse (canRemove (outsideMountPlan ()) Set.empty) "dead end: nothing can be removed"

        testCase "acknowledgedHashes returns exactly the ticked rows' hashes, in plan order" <| fun () ->
            let plan = torrentsPresentPlan [ torrent "aaa" true; torrent "bbb" false; torrent "ccc" true ]
            Expect.equal
                (acknowledgedHashes plan (Set.ofList [ "ccc"; "aaa" ]))
                [ "aaa"; "ccc" ]
                "plan order, not ticked-set order"

        testCase "Confirm on Confirming moves to Removing with exactly the ticked rows' hashes" <| fun () ->
            let plan = torrentsPresentPlan [ torrent "aaa" true; torrent "bbb" false; torrent "ccc" true ]
            let model = baseModel (Confirming (plan, Set.ofList [ "aaa"; "ccc" ]))
            let updated, _ = update noopEffects Confirm model
            Expect.equal updated.Phase (Removing [ "aaa"; "ccc" ]) "acknowledged = exactly the ticked rows' hashes"

        testCase "Removal_result outcome moves Removing to Finished outcome" <| fun () ->
            let outcome = { Completed = [ PreserveWatchHistory; ResolvePath ]; Failure = None }
            let model = baseModel (Removing [ "aaa" ])
            let updated, _ = update noopEffects (Removal_result outcome) model
            Expect.equal updated.Phase (Finished outcome) "the outcome lands unchanged"

        testCase "Retry on a Finished outcome with Failure = Some moves back to Planning" <| fun () ->
            let outcome = {
                Completed = []
                Failure = Some (PreserveWatchHistory, "refused: a Jellyfin sync is in progress -- try again once it finishes")
            }
            let model = baseModel (Finished outcome)
            let updated, _ = update noopEffects Retry model
            Expect.equal updated.Phase Planning "Retry re-issues Plan from scratch"

        testCase "outcomeRows renders every Completed step in order, and on failure appends the failing step with its reason verbatim" <| fun () ->
            let outcome = {
                Completed = [ PreserveWatchHistory; ResolvePath; MatchTorrents ]
                Failure = Some (DeleteTorrents, "the torrent list changed since the dialog was shown — reopen it")
            }
            let rows = outcomeRows outcome
            Expect.equal
                (rows |> List.map (fun r -> r.Step))
                [ PreserveWatchHistory; ResolvePath; MatchTorrents; DeleteTorrents ]
                "completed steps first, in order, failing step appended"
            Expect.equal
                (rows |> List.map (fun r -> r.Label))
                [ "Play state saved"; "Path resolved"; "Torrents matched"; "Torrents deleted (with files)" ]
                "human labels"
            match rows |> List.last with
            | { Status = Row_failed reason } ->
                Expect.equal
                    reason
                    "the torrent list changed since the dialog was shown — reopen it"
                    "reason text verbatim"
            | _ -> failtest "expected the last row to be the failing step"

        testCase "outcomeRows on full success has every step completed, nothing failed" <| fun () ->
            let outcome = {
                Completed =
                    [ PreserveWatchHistory
                      ResolvePath
                      MatchTorrents
                      DeleteTorrents
                      DeleteJellyfinItem
                      Verify
                      ClearLinks ]
                Failure = None
            }
            let rows = outcomeRows outcome
            Expect.equal (List.length rows) 7 "all seven steps, nothing failed"
            Expect.isTrue
                (rows
                 |> List.forall (fun r ->
                     match r.Status with
                     | Row_completed -> true
                     | Row_failed _ -> false))
                "every row is completed"
    ]

Mocha.runTests localCopyRemovalDialogTests |> ignore
