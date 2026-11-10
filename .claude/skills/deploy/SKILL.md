# Skill: Deploy

Full test-build-deploy pipeline to the production server (harbour). The Docker image is
built **on harbour**; no local Docker is needed or used.

## Invocation

```
/deploy
```

## Instructions

Run the one script that does the whole pipeline, from the repo root, in Git Bash:

```bash
bash scripts/deploy-harbour.sh
```

(Marco runs the same thing by double-clicking `deploy.cmd`. In PowerShell, plain `bash` is
WSL's bash, which has no git: use `& "C:\Program Files\Git\bin\bash.exe" scripts/deploy-harbour.sh`.)

What it does, aborting at the first failure:

1. Refuses uncommitted changes to tracked files: the image is built from `HEAD`
   (`git archive`). Untracked files are reported and left out. **Commit first.**
2. `npm test` locally (the server test suite).
3. On harbour: keeps the running image as `mediatheca:previous`, then streams the repo
   over ssh and runs `docker build -t mediatheca:latest -` there. A failed build changes
   nothing.
4. `docker compose up -d --force-recreate` in `/opt/stacks/mediatheca` (app and its
   Tailscale sidecar together).
5. Waits for the container healthcheck, then checks
   `https://mediatheca.elver-minor.ts.net/health`.
6. Removes dangling images; the build cache stays for faster rebuilds.

Options: `--skip-tests` (only when Marco asks), `--rollback` (puts `mediatheca:previous`
back and recreates; no build).

### Important

- **Report the script's output faithfully.** If it aborts, show the `DEPLOY ABORTED` line
  and stop; offer `--rollback` if the new container came up unhealthy.
- **No git operations** beyond what the script reads. This skill does not commit, push, or tag.
- **Never edit the compose file on harbour.** It belongs to the harbour repo
  (`C:\src\heimeshoff\infrastructure\harbour\stacks\mediatheca`), deployed with its
  `deploy-stack.sh`.
- **SSH is key-based.** If ssh asks for a password, abort and report it.
