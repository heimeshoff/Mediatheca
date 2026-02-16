---
description: Execute an ad-hoc task without full planning
---
Ask what the user wants to do.

## Setup: Git Worktree

Before doing any work, create an isolated worktree:

1. Derive a short kebab-case slug from the task description (e.g., "fix poster alignment" → `fix-poster-alignment`)
2. Run: `git worktree add ../mediatheca-quick-<slug> -b quick/<slug>` from the repo root
3. **All subsequent work** (file reads, edits, builds, tests) MUST happen inside the worktree directory (`../mediatheca-quick-<slug>`), NOT the original repo
4. Run `npm install` in the worktree if `node_modules/` is missing (worktrees don't share it)

## Execution

If the user provides **multiple independent tasks** (e.g., "rename X, fix Y, update Z"):
1. Identify which tasks are independent (don't modify the same files or depend on each other's output)
2. Launch independent tasks as parallel Task agents (`subagent_type: "general-purpose"`), telling each agent to work in the worktree path
3. Run dependent tasks sequentially after their dependencies complete
4. Collect results from all agents

If the user provides a **single task**, execute it directly — all inside the worktree.

## Verification & Commit

After all tasks complete (all commands run inside the worktree directory):
1. Run `npm run build` and `npm test` in parallel (using Task agents with `subagent_type: "Bash"`) to verify nothing broke
2. Log to `.planning/STATE.md` under "Recent Progress" with timestamp
3. Stage and commit **on the worktree branch** with message: `"quick: [description]"`
4. Do NOT merge into the original branch — leave the `quick/<slug>` branch for the user to review/merge
5. Tell the user the branch name and how to merge it (e.g., `git merge quick/<slug>`)

## Cleanup

After committing, remove the worktree:
1. `cd` back to the original repo root
2. Run: `git worktree remove ../mediatheca-quick-<slug>`
3. The `quick/<slug>` branch remains available for merge
