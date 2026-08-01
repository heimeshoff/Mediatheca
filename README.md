# Mediatheca
Mediatheca — your personal media library for tracking movies, series, games, and books.

## Development

```bash
npm install        # install dependencies
npm start          # run server + client (dev mode)
npm run build      # production client build
npm test           # run tests
```

## Running without Docker

Docker is the production target, but the same codebase runs three other ways. All entry
points share one composition root (`src/Server/Composition.fs`, `Composition.buildApp`) —
there are no per-target forks.

### Dev mode (two processes, hot reload)

```bash
npm start
```

Server on `localhost:5000` (`dotnet watch`), Vite dev client on `localhost:5173` with
`/api/*` proxied to the server. **Open 5173.**

### Single process, in a browser

```bash
npm run serve
```

Builds the client, then runs the server, which serves the built SPA from `deploy/public`.
Open the port Kestrel prints (`http://localhost:5000` unless `ASPNETCORE_URLS` overrides it).
Must be launched from the repo root — `deploy/public` is resolved relative to the working
directory.

### Native desktop app

A Photino.NET shell (`src/Desktop/`, ADR-0018) hosts the server in-process and opens a
native webview window. Kestrel binds to `127.0.0.1` on a free ephemeral port — the app has
no authentication, so a desktop server must never be reachable from the network.

```bash
npm run desktop              # build client + run the shell
npm run desktop:publish      # standalone win-x64 exe  -> deploy/desktop/win-x64/
npm run desktop:publish:mac  # standalone osx-arm64    -> deploy/desktop/osx-arm64/
```

The published output is self-contained: it runs without a .NET install. Native AOT is
deliberately off — Fable.Remoting and Giraffe are reflection-heavy (ADR-0004) and trimming
would break the API surface.

Notes:

- Each script runs `npm run build` first, because the client assets are copied into the
  desktop build output at *build* time. To relaunch without rebuilding the client, call
  `dotnet run --project src/Desktop/Desktop.fsproj` directly.
- `--self-contained` has to be passed on the command line, not set in `Desktop.fsproj` —
  MSBuild's NETSDK1150 check only trusts it as a real global property. See the comments in
  `src/Desktop/Desktop.fsproj`.
- **Status:** the Windows shell is a smoke-tested prototype — no installer, code signing,
  auto-update, tray icon, or single-instance guard. The macOS target is publish-verified
  only and has never been run on an actual Mac.

### Data & configuration

All four run modes share the same database — there is no isolation between them.

- `mediatheca.db`, the `images/` cache, and WAL sidecars live in `DATA_DIR` if set,
  otherwise a per-platform default: `~/app/mediatheca` on Windows and Linux,
  `~/Library/Application Support/Mediatheca` on macOS.
- `TMDB_API_KEY`, `RAWG_API_KEY`, `STEAM_API_KEY`, and `STEAM_ID` are only *seeded* from
  the environment into the settings table when the DB has no value yet. After first run the
  stored value wins — change them in the in-app Settings page.

## Deployment

Mediatheca runs as a Docker container managed by [Dockge](https://github.com/louislam/dockge).

### Build & export the image

```bash
npm run deploy
```

This builds the Docker image and saves it to `mediatheca.tar`.

### Transfer & load on the server

```bash
# Copy the image to your server
scp mediatheca.tar your-server:/tmp/

# SSH into the server and load it
ssh your-server
docker load < /tmp/mediatheca.tar
```

`docker load` imports the image into Docker's internal image store — the tar file location doesn't matter and can be deleted afterwards.

### Restart the stack

In Dockge, open the **mediatheca** stack and click **Restart**. Docker detects that the `mediatheca:latest` image has changed and recreates the container.

Or via CLI:

```bash
cd /opt/stacks/mediatheca
docker compose up -d
```

Your data volume is preserved across updates, so the database and Tailscale state are safe.
