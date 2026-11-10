#!/usr/bin/env bash
# deploy-harbour.sh [--skip-tests | --rollback]
#
# Build Mediatheca ON HARBOUR from the committed HEAD and restart it there.
# No local Docker needed: the repo is streamed over ssh (git archive) and the
# multi-stage Dockerfile builds server and client on harbour itself.
#
#   1. Refuse uncommitted changes to tracked files (the image is exactly HEAD).
#   2. Run the server tests locally (npm test), unless --skip-tests.
#   3. On harbour: keep the running image as mediatheca:previous, then
#      `docker build -t mediatheca:latest -`. A failed build leaves :latest
#      and the running container untouched.
#   4. `docker compose up -d --force-recreate` in /opt/stacks/mediatheca
#      (recreates the app AND its Tailscale sidecar, so the sidecar never sits
#      in a dead network namespace).
#   5. Wait for the container healthcheck, then check
#      https://mediatheca.elver-minor.ts.net/health from here.
#   6. Remove dangling images (the build cache is kept, for faster rebuilds).
#
# --rollback  puts mediatheca:previous back as :latest and recreates; no build.
#
# The compose file itself belongs to the harbour repo (stacks/mediatheca);
# this script never edits it. Double-click deploy.cmd, or run it from a terminal.

set -euo pipefail

HOST="marco@harbour.elver-minor.ts.net"
STACK="/opt/stacks/mediatheca"
URL="https://mediatheca.elver-minor.ts.net/health"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

step() { printf '\n== %s\n' "$*"; }
die()  { printf '\nDEPLOY ABORTED: %s\n' "$*" >&2; exit 1; }

SKIP_TESTS=0; ROLLBACK=0
case "${1:-}" in
  "") ;;
  --skip-tests) SKIP_TESTS=1 ;;
  --rollback) ROLLBACK=1 ;;
  *) echo "usage: deploy-harbour.sh [--skip-tests | --rollback]" >&2; exit 2 ;;
esac

recreate_and_check() {
  step "recreate on harbour"
  ssh "$HOST" "sudo sh -c 'cd $STACK && docker compose up -d --force-recreate'"

  step "health"
  local status=""
  for _ in $(seq 1 36); do
    status="$(ssh "$HOST" "sudo docker inspect mediatheca --format '{{.State.Health.Status}}'" 2>/dev/null || true)"
    [ "$status" = "healthy" ] && break
    [ "$status" = "unhealthy" ] && break
    sleep 5
  done
  ssh "$HOST" "sudo docker ps --filter label=com.docker.compose.project=mediatheca --format '{{.Names}}\t{{.Status}}'"
  [ "$status" = "healthy" ] || die "container is '$status', not healthy. Logs: ssh harbour 'sudo docker logs mediatheca --tail 50'. Rollback: deploy.cmd --rollback"
  local ok=0
  for _ in $(seq 1 12); do
    curl -sf -o /dev/null --max-time 10 "$URL" && { ok=1; break; }
    sleep 5
  done
  [ $ok = 1 ] || die "container is healthy but $URL does not answer (sidecar?). Check: ssh harbour 'sudo docker logs mediatheca-tailscale --tail 30'"
  echo "$URL answers"
}

# --- rollback --------------------------------------------------------------
if [ $ROLLBACK = 1 ]; then
  step "rollback to mediatheca:previous"
  ssh "$HOST" "sudo docker image inspect mediatheca:previous >/dev/null 2>&1" || die "no mediatheca:previous on harbour"
  ssh "$HOST" "sudo docker image inspect mediatheca:previous --format 'previous was built from: {{index .Config.Labels \"org.opencontainers.image.revision\"}}'"
  ssh "$HOST" "sudo docker tag mediatheca:previous mediatheca:latest"
  recreate_and_check
  printf '\nROLLED BACK. Run deploy.cmd again once HEAD is fixed.\n'
  exit 0
fi

# --- 1. clean tree ---------------------------------------------------------
step "1/6 working tree"
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  git status --short --untracked-files=no >&2
  die "uncommitted changes to tracked files. Commit first; the image is built from HEAD."
fi
UNTRACKED="$(git status --porcelain | grep -c '^??' || true)"
[ "$UNTRACKED" = 0 ] || echo "note: $UNTRACKED untracked file(s) are NOT part of the build (git status)"
SHA="$(git rev-parse --short HEAD)"
echo "building from $SHA: $(git log -1 --format=%s | cut -c1-80)"

# --- 2. tests --------------------------------------------------------------
step "2/6 tests"
if [ $SKIP_TESTS = 1 ]; then
  echo "SKIPPED (--skip-tests)"
else
  npm test || die "tests failed; nothing was built or deployed"
fi

# --- 3. build on harbour ---------------------------------------------------
step "3/6 build mediatheca:latest on harbour (several minutes on the N150)"
ssh "$HOST" "sudo docker image inspect mediatheca:latest >/dev/null 2>&1 && sudo docker tag mediatheca:latest mediatheca:previous && echo 'kept the current image as mediatheca:previous'" || true
git archive --format=tar HEAD \
  | ssh "$HOST" "sudo docker build \
      --label org.opencontainers.image.revision=$SHA \
      -t mediatheca:latest -" \
  || die "build failed on harbour; mediatheca:latest and the running container are unchanged"

# --- 4 + 5. recreate, health ----------------------------------------------
recreate_and_check

# --- 6. cleanup ------------------------------------------------------------
step "6/6 cleanup"
ssh "$HOST" "sudo docker image prune -f" | tail -1 || echo "(cleanup failed; harmless)"

printf '\nDONE: Mediatheca %s is live. Rollback if needed: deploy.cmd --rollback\n' "$SHA"
