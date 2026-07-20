# Spike harness — Steam Family access-token minting (integration-ygwsa)

**Status: UNEXECUTED.** This harness was written to prove/disprove that
Mediatheca's server can mint its own `IFamilyGroupsService` access tokens from
a stored Steam refresh token, eliminating the manual DevTools scrape (see
`.agentheim/contexts/integration/done/integration-ygwsa-steam-family-token-spike.md`).

It could **not** be run in the environment this spike was done in: the QR
step requires a human with the Steam mobile app to scan a code from a live
terminal session, and no throwaway/test Steam account with an active Family
Group was available. The code below reflects the recommended shape from the
companion research report
(`.agentheim/knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`)
and compiles conceptually against SteamKit2's documented API, but **none of
it has been executed against the real Steam network.** Do not treat the
`GetFamilyGroupForUser` call at the bottom as a confirmed-working path — that
is exactly the decision-critical unknown this spike could not close. See
ADR-0019 and the task's Notes for what integration-hebjs must verify first.

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
