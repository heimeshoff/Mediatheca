module Mediatheca.Tests.FriendsTests

open Expecto
open Mediatheca.Server.Friends

let private givenWhenThen (given: FriendEvent list) (command: FriendCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let friendsTests =
    testList "Friends" [

        testList "Add_friend" [
            testCase "adding friend to empty state succeeds" <| fun _ ->
                let result = givenWhenThen [] (Add_friend ("Marco", None))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Friend_added data ->
                        Expect.equal data.Name "Marco" "Name should match"
                        Expect.equal data.ImageRef None "ImageRef should be None"
                    | _ -> failtest "Expected Friend_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding friend with image succeeds" <| fun _ ->
                let result = givenWhenThen [] (Add_friend ("Sarah", Some "friends/sarah.jpg"))
                match result with
                | Ok events ->
                    match events.[0] with
                    | Friend_added data ->
                        Expect.equal data.ImageRef (Some "friends/sarah.jpg") "ImageRef should match"
                    | _ -> failtest "Expected Friend_added"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding already-existing friend fails" <| fun _ ->
                let result = givenWhenThen
                                [ Friend_added { Name = "Marco"; ImageRef = None } ]
                                (Add_friend ("Marco", None))
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Update_friend" [
            testCase "updating existing friend succeeds" <| fun _ ->
                let result = givenWhenThen
                                [ Friend_added { Name = "Marco"; ImageRef = None } ]
                                (Update_friend ("Marco Updated", Some "friends/marco.jpg"))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | Friend_updated data ->
                        Expect.equal data.Name "Marco Updated" "Name should match"
                        Expect.equal data.ImageRef (Some "friends/marco.jpg") "ImageRef should match"
                    | _ -> failtest "Expected Friend_updated"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "updating non-existent friend fails" <| fun _ ->
                let result = givenWhenThen [] (Update_friend ("Marco", None))
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Remove_friend" [
            testCase "removing existing friend succeeds" <| fun _ ->
                let result = givenWhenThen
                                [ Friend_added { Name = "Marco"; ImageRef = None } ]
                                Remove_friend
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] Friend_removed "Should be Friend_removed"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent friend fails" <| fun _ ->
                let result = givenWhenThen [] Remove_friend
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Removed friend" [
            testCase "commands on removed friend fail" <| fun _ ->
                let removedEvents = [ Friend_added { Name = "Marco"; ImageRef = None }; Friend_removed ]
                let commands: FriendCommand list = [
                    Add_friend ("Marco", None)
                    Update_friend ("Marco Updated", None)
                    Remove_friend
                ]
                for cmd in commands do
                    let result = givenWhenThen removedEvents cmd
                    match result with
                    | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                    | Ok _ -> failtest $"Expected error for command on removed friend: {cmd}"
        ]

        testList "Serialization" [
            testCase "Friend_added round-trip" <| fun _ ->
                let event = Friend_added { Name = "Marco"; ImageRef = Some "friends/marco.jpg" }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Friend_updated round-trip" <| fun _ ->
                let event = Friend_updated { Name = "Marco Updated"; ImageRef = None }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "Friend_removed round-trip" <| fun _ ->
                let event = Friend_removed
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
