module Mediatheca.Server.DataDir

open System
open System.IO
open System.Runtime.InteropServices

/// The OS family the process is running on, as far as data-dir defaults care.
/// Linux and "everything else we haven't special-cased" share the existing
/// ~/app/mediatheca default; macOS gets the platform-conventional
/// Application Support directory.
type Platform =
    | Windows
    | MacOS
    | LinuxOrOther

/// Detect the current OS family via RuntimeInformation. Kept as a thin
/// wrapper so `resolve` itself stays a pure function we can unit test
/// without touching the real OS or environment.
let currentPlatform () : Platform =
    if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then MacOS
    elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then Windows
    else LinuxOrOther

/// Pure resolution: given an optional DATA_DIR override, the detected
/// platform, and the user's home directory, compute the data directory.
/// - DATA_DIR (non-empty) always wins, on every platform.
/// - Otherwise: macOS defaults to ~/Library/Application Support/Mediatheca
///   (platform convention for per-app persistent data); every other
///   platform (Windows, Linux) keeps the existing ~/app/mediatheca default.
let resolve (platform: Platform) (envDataDir: string option) (homeDir: string) : string =
    match envDataDir with
    | Some dir when dir <> "" -> dir
    | _ ->
        match platform with
        | MacOS -> Path.Combine(homeDir, "Library", "Application Support", "Mediatheca")
        | Windows
        | LinuxOrOther -> Path.Combine(homeDir, "app", "mediatheca")

/// Real-world convenience wrapper: reads DATA_DIR from the environment,
/// detects the actual OS, and uses the actual user profile directory.
let resolveDefault () : string =
    let envDataDir =
        Environment.GetEnvironmentVariable("DATA_DIR") |> Option.ofObj
    let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    resolve (currentPlatform ()) envDataDir home
