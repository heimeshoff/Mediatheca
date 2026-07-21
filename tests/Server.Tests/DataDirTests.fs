module Mediatheca.Tests.DataDirTests

open System.IO
open Expecto
open Mediatheca.Server.DataDir

// `resolve` builds paths via Path.Combine, so its separator follows whatever
// OS the *test process* runs on (not the `Platform` argument, which only
// selects the path *segments*). Expected values are built the same way so
// these tests are meaningful on any OS, including the Windows dev machine
// these were authored/verified on.
let private combine (parts: string list) = parts |> List.reduce (fun a b -> Path.Combine(a, b))

[<Tests>]
let dataDirTests =
    testList "DataDir" [

        testCase "DATA_DIR override wins on Windows" <| fun _ ->
            let result = resolve Windows (Some "D:\\custom") "C:\\Users\\bob"
            Expect.equal result "D:\\custom" "Explicit DATA_DIR should always win"

        testCase "DATA_DIR override wins on macOS" <| fun _ ->
            let result = resolve MacOS (Some "/custom/dir") "/Users/bob"
            Expect.equal result "/custom/dir" "Explicit DATA_DIR should always win"

        testCase "empty DATA_DIR is treated as unset" <| fun _ ->
            let result = resolve Windows (Some "") "C:\\Users\\bob"
            Expect.equal result (combine ["C:\\Users\\bob"; "app"; "mediatheca"]) "Empty string should fall through to the default"

        testCase "Windows default is ~/app/mediatheca" <| fun _ ->
            let result = resolve Windows None "C:\\Users\\bob"
            Expect.equal result (combine ["C:\\Users\\bob"; "app"; "mediatheca"]) "Windows keeps the existing default"

        testCase "Linux/other default is ~/app/mediatheca" <| fun _ ->
            let result = resolve LinuxOrOther None "/home/bob"
            Expect.equal result (combine ["/home/bob"; "app"; "mediatheca"]) "Linux keeps the existing default (matches Docker behavior)"

        testCase "macOS default is ~/Library/Application Support/Mediatheca" <| fun _ ->
            let result = resolve MacOS None "/Users/bob"
            Expect.equal result (combine ["/Users/bob"; "Library"; "Application Support"; "Mediatheca"]) "macOS uses the platform-conventional app support dir"
    ]
