# Skill: Deploy

Full build-test-deploy pipeline to the production server. Tests, builds a Docker image, uploads it, and restarts the container.

## Invocation

```
/deploy
```

## Instructions

You are the deployment executor. You run the full pipeline end-to-end, aborting immediately if any step fails.

### 1. Run Tests

```bash
npm test
```

Report: "Running tests..."
If tests fail, abort with: "Tests failed. Deployment aborted."

### 2. Build Docker Image

```bash
npm run deploy
```

This runs `docker build -t mediatheca:latest . && docker save mediatheca:latest -o mediatheca.tar`.

Report: "Building Docker image..."
If build fails, abort with: "Build failed. Deployment aborted."

### 3. Upload Image to Server

```bash
scp mediatheca.tar marco@harbour.elver-minor.ts.net:/tmp/
```

Report: "Uploading image to server..."
If upload fails, abort with: "Upload failed. Deployment aborted."

### 4. Deploy on Server

Run these commands via SSH to `marco@harbour.elver-minor.ts.net`:

```bash
ssh marco@harbour.elver-minor.ts.net "docker load < /tmp/mediatheca.tar"
```

Report: "Loading Docker image on server..."

```bash
ssh marco@harbour.elver-minor.ts.net "cd /opt/stacks/mediatheca && docker compose down && docker compose up -d"
```

Report: "Restarting container..."

If any SSH command fails, abort with the error.

### 5. Cleanup

Run all cleanup steps, reporting any failures but not aborting:

```bash
rm mediatheca.tar
ssh marco@harbour.elver-minor.ts.net "rm /tmp/mediatheca.tar && docker image prune -f"
```

Report: "Cleaning up..."

### 6. Done

Report: "Deployment complete."

### Important

- **Abort on failure.** If any step (test, build, upload, deploy) fails, stop immediately and report the error. Do not continue to the next step.
- **Report progress.** Print a clear status message before each step so the user knows what is happening.
- **SSH is key-based.** No password prompts are expected. If SSH asks for a password, something is misconfigured -- abort and report it.
- **No health check.** The deploy is done once `docker compose up -d` succeeds.
- **No git operations.** This skill does not commit, push, or tag anything.
