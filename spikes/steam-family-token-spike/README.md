# Spike harness — Steam Family access-token minting (integration-ygwsa)

**Status: EXECUTED — PASS (2026-08-07).** This harness was written to
prove/disprove that Mediatheca's server can mint its own
`IFamilyGroupsService` access tokens from a stored Steam refresh token,
eliminating the manual DevTools scrape (see
`.agentheim/contexts/integration/done/integration-ygwsa-steam-family-token-spike.md`).

The integration-hebjs builder gate ran it live against the real Steam
network: QR login succeeded (MobileApp platform, persistent session),
`GenerateAccessTokenForApp` minted an access token over plain HTTP (no CM
connection), and `GetFamilyGroupForUser` returned **HTTP 200 with real
family data** — the minted token carries the required audience/scope. The
decision-critical unknown from ADR-0019 is closed; integration-hebjs builds
the production flow on this confirmed path. Running it required a handful of
API fixes vs. the as-written harness (SteamKit2 3.1.0: `CallbackManager` not
`IDisposable`, `ChallengeURLChanged` is an `Action` property,
`EAuthTokenPlatformType` lives in `SteamKit2.Internal`; plus
`GenerateAccessTokenForApp` requires a `steamid` param — taken from the
refresh-token JWT's `sub` claim — and the QR must be rendered as an image,
now written to `qr.local.png` via QRCoder) — all folded into the scripts, so
they are a proven-live reference.

## Why `MobileApp` platform, not the SteamKit2-sample default

The official SteamKit2 QR sample uses the default `AuthSessionDetails()`,
which is `PlatformType = SteamClient`. Research finding: as of an April 2025
Steam-side change, refreshing a `SteamClient`-platform token via plain HTTP
(`GenerateAccessTokenForApp`) now requires an **authenticated CM
connection** — i.e. the server would have to stay connected to Steam's
binary network to mint tokens, defeating the point of a lightweight
HTTP-only adapter matching the rest of `Steam.fs`. `MobileApp`-platform
tokens are reported to refresh over plain HTTP with no CM connection needed.
So this harness deliberately sets `PlatformType = MobileApp` and
`IsPersistentSession = true` at login time.

## Running this for real (not done in this spike)

1. `dotnet fsi login.fsx` from this directory — requires the `SteamKit2`
   NuGet package (referenced via `#r "nuget: SteamKit2"`, resolved at script
   run time, not added to `Server.fsproj`).
2. Scan the printed QR code with the Steam mobile app (the account must be a
   member of the target Family Group).
3. The script prints the resulting refresh token to stdout and writes it to
   `refresh-token.local.txt` in this directory (gitignored — **never commit
   this file**, it is a long-lived credential).
4. Re-run with `dotnet fsi refresh-and-call.fsx` to mint a fresh access token
   from the stored refresh token via a plain HTTP POST and call
   `GetFamilyGroupForUser`. Compare the result against a known-good
   browser-scraped token to answer the audience/scope question.

## Files

- `login.fsx` — one-time interactive QR login, persists a refresh token.
- `refresh-and-call.fsx` — mints an access token from the stored refresh
  token (no SteamKit2/CM connection, plain HTTP) and calls
  `IFamilyGroupsService/GetFamilyGroupForUser`.
