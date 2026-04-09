# Task 040: Deploy Skill (/deploy)

**Status:** Todo
**Size:** Small
**Created:** 2026-04-09
**Milestone:** --

## Description

Create a `/deploy` slash command skill that performs a full build-test-deploy cycle to the Linux production server. The skill automates the entire deployment pipeline that is currently done manually.

### Pipeline Steps

1. **Test** — Run `npm test` (Expecto tests)
2. **Build** — Run `npm run deploy` (Docker build + export to `mediatheca.tar`)
3. **Upload** — `scp mediatheca.tar marco@harbour.elver-minor.ts.net:/tmp/`
4. **Deploy on server** (via SSH to `marco@harbour.elver-minor.ts.net`):
   a. Load image: `docker load < /tmp/mediatheca.tar`
   b. Stop container: `cd /opt/stacks/mediatheca && docker compose down`
   c. Start container: `docker compose up -d`
5. **Cleanup**:
   - Remove `mediatheca.tar` locally
   - Remove `/tmp/mediatheca.tar` on server
   - Prune dangling Docker images on server

### Abort Conditions

- If tests fail, abort before building
- If build fails, abort before uploading
- If any SSH/SCP step fails, report the error

## Acceptance Criteria

- [x] `/deploy` skill file exists at `.claude/skills/deploy/SKILL.md`
- [x] Running `/deploy` executes the full pipeline end-to-end
- [x] Tests run before build; failure aborts the deploy
- [x] Container is stopped before image update, restarted after
- [x] Cleanup removes tar files (local + remote) and dangling images
- [x] Clear progress reporting at each step

## Work Log

### 2026-04-09 -- Created /deploy skill

Created `.claude/skills/deploy/SKILL.md` following the existing skill format (modeled after `/fix`). The skill instructs Claude to execute 5 sequential steps (test, build, upload, deploy, cleanup) with clear progress reporting and abort-on-failure semantics. Each step uses direct bash commands. SSH commands target `marco@harbour.elver-minor.ts.net` with key-based auth. Cleanup removes local and remote tar files and prunes dangling images.
