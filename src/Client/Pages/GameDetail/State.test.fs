/// games-zex36: MVU coverage for attaching/detaching a friend on an
/// individual play session. `Add_friend_to_session`/`Remove_friend_from_session`
/// must issue the matching `IMediathecaApi.addFriendToPlaySession`/
/// `removeFriendFromPlaySession` command with (slug, day, friendSlug), and a
/// successful `Session_friend_command_result` must refetch both the play
/// sessions (friend chips) and the game detail (PlayedWith may have grown).
/// Same `createObj [...] |> unbox` api stand-in and `runCmd` timer-draining
/// helper `BookDetail/State.test.fs` established.
module Mediatheca.Client.Pages.GameDetail.StateTests

open Elmish
open Fable.Core.JsInterop
open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.GameDetail.Types
open Mediatheca.Client.Pages.GameDetail.State

let private runCmd (cmd: Cmd<Msg>) : Async<Msg list> = async {
    let dispatched = ResizeArray<Msg>()
    for effect in cmd do
        effect dispatched.Add
    do! Async.Sleep 50
    return List.ofSeq dispatched
}

let sessionFriendTests =
    testList "games-zex36: GameDetail.State play-session friends" [

        testCaseAsync "Add_friend_to_session issues addFriendToPlaySession with (slug, day, friendSlug), then refetches sessions and game on success" <| async {
            let mutable captured : (string * string * string) option = None
            let calls = ResizeArray<string>()
            let api : IMediathecaApi =
                createObj [
                    "addFriendToPlaySession" ==> (fun (slug: string) (day: string) (friendSlug: string) ->
                        captured <- Some (slug, day, friendSlug)
                        calls.Add "addFriendToPlaySession"
                        async { return Ok () })
                    "getGamePlaySessions" ==> (fun (_: string) -> calls.Add "getGamePlaySessions"; async { return [] })
                    "getGameDetail" ==> (fun (_: string) -> calls.Add "getGameDetail"; async { return None })
                ] |> unbox
            let model, _ = init "hollow-knight-2017"
            let _, cmd = update api (Add_friend_to_session ("2024-06-01", "marco")) model
            let! dispatched = runCmd cmd
            match captured with
            | Some (slug, day, friendSlug) ->
                Expect.equal slug "hollow-knight-2017" "the game's own slug"
                Expect.equal day "2024-06-01" "the session's natural-key day"
                Expect.equal friendSlug "marco" "the friend being added"
            | None -> failtest "expected addFriendToPlaySession to have been called"
            // The Ok () result dispatches Session_friend_command_result — run it too.
            for msg in dispatched do
                let _, resultCmd = update api msg model
                let! _ = runCmd resultCmd
                ()
            Expect.equal
                (calls |> Seq.sort |> List.ofSeq)
                [ "addFriendToPlaySession"; "getGameDetail"; "getGamePlaySessions" ]
                "both the session-friend command and the post-success refetch of sessions + game detail fire"
        }

        testCaseAsync "Remove_friend_from_session issues removeFriendFromPlaySession with (slug, day, friendSlug)" <| async {
            let mutable captured : (string * string * string) option = None
            let api : IMediathecaApi =
                createObj [
                    "removeFriendFromPlaySession" ==> (fun (slug: string) (day: string) (friendSlug: string) ->
                        captured <- Some (slug, day, friendSlug)
                        async { return Ok () })
                ] |> unbox
            let model, _ = init "hollow-knight-2017"
            let _, cmd = update api (Remove_friend_from_session ("2024-06-01", "marco")) model
            let! _ = runCmd cmd
            match captured with
            | Some (slug, day, friendSlug) ->
                Expect.equal slug "hollow-knight-2017" "the game's own slug"
                Expect.equal day "2024-06-01" "the session's natural-key day"
                Expect.equal friendSlug "marco" "the friend being removed"
            | None -> failtest "expected removeFriendFromPlaySession to have been called"
        }

        testCaseAsync "Session_friend_command_result (Ok ()) refetches play sessions and game detail" <| async {
            let calls = ResizeArray<string>()
            let api : IMediathecaApi =
                createObj [
                    "getGamePlaySessions" ==> (fun (_: string) -> calls.Add "getGamePlaySessions"; async { return [] })
                    "getGameDetail" ==> (fun (_: string) -> calls.Add "getGameDetail"; async { return None })
                ] |> unbox
            let model, _ = init "hollow-knight-2017"
            let _, cmd = update api (Session_friend_command_result (Ok ())) model
            let! _ = runCmd cmd
            Expect.equal
                (calls |> Seq.sort |> List.ofSeq)
                [ "getGameDetail"; "getGamePlaySessions" ]
                "both loads fire so friend chips and any newly-implied PlayedWith entry show up"
        }

        testCase "Session_friend_command_result (Error _) surfaces the error without an api call" <| fun () ->
            let fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
            let model, _ = init "hollow-knight-2017"
            let errored, cmd = update fakeApi (Session_friend_command_result (Error "Play session not found")) model
            Expect.equal errored.Error (Some "Play session not found") "the error surfaces on the model"
            Expect.isEmpty cmd "no further command — nothing to refetch after a failed command"

        testCaseAsync "Add_new_friend_to_session creates the friend, then attaches them to the named session" <| async {
            let mutable addFriendName : string option = None
            let mutable attachedTo : (string * string) option = None
            let api : IMediathecaApi =
                createObj [
                    "addFriend" ==> (fun (name: string) ->
                        addFriendName <- Some name
                        async { return Ok "new-friend" })
                    "addFriendToPlaySession" ==> (fun (_: string) (day: string) (friendSlug: string) ->
                        attachedTo <- Some (day, friendSlug)
                        async { return Ok () })
                ] |> unbox
            let model, _ = init "hollow-knight-2017"
            let _, cmd = update api (Add_new_friend_to_session ("2024-06-01", "New Friend")) model
            let! _ = runCmd cmd
            Expect.equal addFriendName (Some "New Friend") "the new friend's name"
            Expect.equal attachedTo (Some ("2024-06-01", "new-friend")) "the newly created friend's slug is attached to the right session"
        }
    ]

Mocha.runTests sessionFriendTests |> ignore
