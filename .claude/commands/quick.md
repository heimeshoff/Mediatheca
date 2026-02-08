---
description: Execute an ad-hoc task without full planning
---
Ask what the user wants to do.

If the user provides **multiple independent tasks** (e.g., "rename X, fix Y, update Z"):
1. Identify which tasks are independent (don't modify the same files or depend on each other's output)
2. Launch independent tasks as parallel Task agents (`subagent_type: "general-purpose"`)
3. Run dependent tasks sequentially after their dependencies complete
4. Collect results from all agents

If the user provides a **single task**, execute it directly.

After all tasks complete:
1. Run `npm run build` and `npm test` in parallel (using Task agents with `subagent_type: "Bash"`) to verify nothing broke
2. Log to `.planning/STATE.md` under "Recent Progress" with timestamp
3. Commit with message: `"quick: [description]"`
