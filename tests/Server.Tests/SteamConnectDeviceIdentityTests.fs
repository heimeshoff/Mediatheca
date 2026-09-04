module Mediatheca.Tests.SteamConnectDeviceIdentityTests

open Expecto
open SteamKit2.Internal
open Mediatheca.Server.SteamConnect

// Coverage for integration-zwnh4: the third Valve alert (2026-09-03) traced
// the "this account may have been accessed by someone else" flag to a QR
// login session presenting a randomly-named, per-deploy device fingerprint
// (SteamKit2's Environment.MachineName / "Client" website id defaults).
// `authSessionDetails` is the pure, testable extraction that fixes this
// (ADR-0067 amendment 2026-09-04) — asserted here without touching
// SteamKit2's network/CM connection at all.

[<Tests>]
let steamConnectDeviceIdentityTests =
    testList "SteamConnect.authSessionDetails" [

        testCase "default device name is exactly Mediatheca when no override is given" <| fun _ ->
            let details = authSessionDetails None
            Expect.equal details.DeviceFriendlyName "Mediatheca" "Defaults to the fixed device name"

        testCase "an override device name is used verbatim" <| fun _ ->
            let details = authSessionDetails (Some "Mediatheca on harbour")
            Expect.equal details.DeviceFriendlyName "Mediatheca on harbour" "Uses the STEAM_DEVICE_NAME override"

        testCase "a blank/whitespace override falls back to the fixed default" <| fun _ ->
            let details = authSessionDetails (Some "   ")
            Expect.equal details.DeviceFriendlyName "Mediatheca" "Whitespace-only override is treated as unset"

        testCase "device name never contains the SteamKit2 per-host default marker" <| fun _ ->
            let details = authSessionDetails None
            Expect.isFalse (details.DeviceFriendlyName.Contains "(SteamKit2)") "No SteamKit2-default suffix"

        testCase "website id is Mobile, matching the MobileApp platform" <| fun _ ->
            let details = authSessionDetails None
            Expect.equal details.WebsiteID "Mobile" "WebsiteID matches node-steam-session's MobileApp mapping, not SteamKit2's Client default"

        testCase "platform type is MobileApp, persistent session is true" <| fun _ ->
            let details = authSessionDetails None
            Expect.equal details.PlatformType EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp "Platform unchanged from ADR-0019 point 2"
            Expect.isTrue details.IsPersistentSession "Persistent session unchanged from ADR-0019 point 2"

        testCase "client OS type is a fixed Android value" <| fun _ ->
            let details = authSessionDetails None
            Expect.equal details.ClientOSType SteamKit2.EOSType.Android9 "Fixed Android OS type, never a runtime lookup"

        testCase "two consecutive calls with the same input return equal field values (stability)" <| fun _ ->
            let first = authSessionDetails (Some "Mediatheca on harbour")
            let second = authSessionDetails (Some "Mediatheca on harbour")
            Expect.equal second.DeviceFriendlyName first.DeviceFriendlyName "Device name is stable across calls"
            Expect.equal second.WebsiteID first.WebsiteID "Website id is stable across calls"
            Expect.equal second.PlatformType first.PlatformType "Platform type is stable across calls"
            Expect.equal second.IsPersistentSession first.IsPersistentSession "Persistent session flag is stable across calls"
            Expect.equal second.ClientOSType first.ClientOSType "OS type is stable across calls"
    ]
