# Mediatheca
Mediatheca — your personal media library for tracking movies, series, games, and books.

## Development

```bash
npm install        # install dependencies
npm start          # run server + client (dev mode)
npm run build      # production client build
npm test           # run tests
```

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
