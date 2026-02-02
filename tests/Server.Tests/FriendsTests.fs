module Mediatheca.Tests.FriendsTests

open Expecto
open Mediatheca.Server.Friends

let private givenWhenThen (given: FriendEvent list) (command: FriendCommand) =
    let state = reconstitute given
    decide state command

[<Tests>]
let friendsTests =
    testList "Friends" [

        testList "AddFriend" [
            testCase "adding friend to empty state succeeds" <| fun _ ->
                let result = givenWhenThen [] (AddFriend ("Marco", None))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | FriendAdded data ->
                        Expect.equal data.Name "Marco" "Name should match"
                        Expect.equal data.ImageRef None "ImageRef should be None"
                    | _ -> failtest "Expected FriendAdded"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding friend with image succeeds" <| fun _ ->
                let result = givenWhenThen [] (AddFriend ("Sarah", Some "friends/sarah.jpg"))
                match result with
                | Ok events ->
                    match events.[0] with
                    | FriendAdded data ->
                        Expect.equal data.ImageRef (Some "friends/sarah.jpg") "ImageRef should match"
                    | _ -> failtest "Expected FriendAdded"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "adding already-existing friend fails" <| fun _ ->
                let result = givenWhenThen
                                [ FriendAdded { Name = "Marco"; ImageRef = None } ]
                                (AddFriend ("Marco", None))
                match result with
                | Error msg -> Expect.stringContains msg "already exists" "Should say already exists"
                | Ok _ -> failtest "Expected error"
        ]

        testList "UpdateFriend" [
            testCase "updating existing friend succeeds" <| fun _ ->
                let result = givenWhenThen
                                [ FriendAdded { Name = "Marco"; ImageRef = None } ]
                                (UpdateFriend ("Marco Updated", Some "friends/marco.jpg"))
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    match events.[0] with
                    | FriendUpdated data ->
                        Expect.equal data.Name "Marco Updated" "Name should match"
                        Expect.equal data.ImageRef (Some "friends/marco.jpg") "ImageRef should match"
                    | _ -> failtest "Expected FriendUpdated"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "updating non-existent friend fails" <| fun _ ->
                let result = givenWhenThen [] (UpdateFriend ("Marco", None))
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "RemoveFriend" [
            testCase "removing existing friend succeeds" <| fun _ ->
                let result = givenWhenThen
                                [ FriendAdded { Name = "Marco"; ImageRef = None } ]
                                RemoveFriend
                match result with
                | Ok events ->
                    Expect.equal (List.length events) 1 "Should produce one event"
                    Expect.equal events.[0] FriendRemoved "Should be FriendRemoved"
                | Error e -> failtest $"Expected success but got: {e}"

            testCase "removing non-existent friend fails" <| fun _ ->
                let result = givenWhenThen [] RemoveFriend
                match result with
                | Error msg -> Expect.stringContains msg "does not exist" "Should say does not exist"
                | Ok _ -> failtest "Expected error"
        ]

        testList "Removed friend" [
            testCase "commands on removed friend fail" <| fun _ ->
                let removedEvents = [ FriendAdded { Name = "Marco"; ImageRef = None }; FriendRemoved ]
                let commands: FriendCommand list = [
                    AddFriend ("Marco", None)
                    UpdateFriend ("Marco Updated", None)
                    RemoveFriend
                ]
                for cmd in commands do
                    let result = givenWhenThen removedEvents cmd
                    match result with
                    | Error msg -> Expect.stringContains msg "removed" "Should say removed"
                    | Ok _ -> failtest $"Expected error for command on removed friend: {cmd}"
        ]

        testList "Serialization" [
            testCase "FriendAdded round-trip" <| fun _ ->
                let event = FriendAdded { Name = "Marco"; ImageRef = Some "friends/marco.jpg" }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "FriendUpdated round-trip" <| fun _ ->
                let event = FriendUpdated { Name = "Marco Updated"; ImageRef = None }
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"

            testCase "FriendRemoved round-trip" <| fun _ ->
                let event = FriendRemoved
                let eventType, data = Serialization.serialize event
                let deserialized = Serialization.deserialize eventType data
                Expect.equal deserialized (Some event) "Should round-trip"
        ]
    ]
