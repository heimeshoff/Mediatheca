# Status Skill

Use when: User asks "where are we?", runs /status, or starts a new session

## Step 1: Show Status

1. Read `.planning/STATE.md`, `.planning/ROADMAP.md`, and `.planning/REQUIREMENTS.md`
2. Summarize concisely: current phase, recent progress, active decisions, blockers
3. List the next actions from STATE.md as the current task list

## Step 2: Survey and Model

### 2a: Codebase Survey (automatic)

Before asking the user about modeling, run the codebase survey in the background to pre-load architectural context. Read `.claude/skills/workflow/codebase-survey.md` and follow it — launch the 4 parallel Explore agents while presenting the status summary to the user.

The survey results will be ready by the time the user responds to the modeling question, giving you instant deep context for the conversation.

### 2b: Ask About Modeling

Ask the user (using AskUserQuestion):

> "Before we start implementing — do you have any further thoughts, questions, or modeling you'd like to explore first? (A codebase survey is running in the background to give us full context.)"

### If YES → Enter Modeling Mode

Use the codebase survey results to inform the conversation. Reference specific types, patterns, and gaps from the snapshot rather than needing to read files on-the-fly.

Guide a collaborative modeling conversation. Ask probing questions one or two at a time, such as:

- "What feels unclear or risky about the next tasks?"
- "Are there any assumptions we're making that you want to challenge?"
- "Has anything changed in your thinking about how this should work?"
- "Are there domain concepts we haven't fully explored yet?"
- "Should the scope or order of the upcoming work change?"
- "Are there edge cases or user flows we haven't considered?"

Adapt your questions based on the user's answers. Keep the conversation focused and productive. Use DDD thinking — explore aggregates, events, boundaries, and invariants where relevant.

When the user signals they're done modeling (or the conversation reaches a natural conclusion):

1. **Summarize** the modeling insights and any decisions made
2. **Update planning files** as needed:
   - Update `.planning/STATE.md` with new decisions, changed next actions, and updated progress
   - Update `.planning/ROADMAP.md` if phase scope, ordering, or deliverables changed
   - Update `.planning/REQUIREMENTS.md` if requirements were added, changed, or reprioritized
3. **Show the user** what changed in the planning files before proceeding

### If NO → Skip Modeling

Acknowledge and move on.

## Step 3: Execute with Parallel Agents

Before starting implementation, analyze the next actions for parallelization opportunities.

### 3a: Dependency Analysis

1. Read the next actions from STATE.md
2. For each action, determine:
   - What it produces (new types, new files, new API endpoints)
   - What it consumes (types/files/APIs from other actions)
3. Build a dependency graph and identify **parallel tracks** — groups of actions that are independent of each other

**Common parallel patterns in this codebase:**

- **Independent bounded contexts**: Domain work in Catalog vs. Journal vs. Friends can run in parallel
- **Independent aggregates**: New aggregate definitions that don't share types can be built concurrently
- **Vertical slices**: Two features touching different pages/routes are independent
- **Shared→Fan-out**: When multiple actions depend on the same shared types, define the shared types first, then fan out the dependent work in parallel

### 3b: Parallel Dispatch

1. Create a task list (using TodoWrite) showing all tracks and their ordering
2. For actions with no dependencies between them, launch parallel Task agents (using `subagent_type: "general-purpose"`). Each agent receives:
   - The specific requirement(s) to implement
   - Relevant project context: conventions from CLAUDE.md, domain model from STATE.md
   - The specific files it should read and modify
   - Instruction to follow existing patterns in the codebase
3. For actions that depend on a previous action's output, run them sequentially after the dependency completes
4. **Boundary rule**: Each agent works on its own set of files. If two actions need to modify the same file (e.g., `Shared.fs` for API contracts), sequence them or handle the shared file in a dedicated step first.

**Example — Phase 3 next actions parallelized:**

```
Step 1 (sequential): Define shared types for WatchSession and ContentBlock in Shared.fs

Step 2 (parallel agents):
  Agent A — Journal track: WatchSession domain model, commands, events, projection, API
  Agent B — Content track: ContentBlock domain model, commands, events, projection, API

Step 3 (parallel agents, after Step 2):
  Agent C — Watch history UI (depends on Agent A's API)
  Agent D — Content block editor UI (depends on Agent B's API)

Step 4 (parallel — see Step 4 below): Verification
```

### 3c: Sequential Fallback

If all next actions are tightly coupled (modifying the same aggregate, same files), skip parallelization and work through them sequentially. Don't force parallelism where it adds coordination overhead without benefit.

## Step 4: Verify in Parallel

After implementation work completes (whether parallel or sequential), run verification agents concurrently.

**CRITICAL GUARD: NEVER update `.planning/STATE.md`, mark requirements as complete in `REQUIREMENTS.md`, or commit changes until ALL verification steps pass. A failing build or test means the work is not done — regardless of how correct the code looks. If verification fails, fix the issue and re-run verification. Repeat until green.**

### 4a: Build + Test (required)

Launch in parallel using Task agents (`subagent_type: "Bash"`):
- **Build agent**: `npm run build` — catches Fable compilation and type errors
- **Test agent**: `npm test` — runs Expecto test suite

If either fails, fix the issues and re-run both. Do not proceed to 4b or 4c until both pass.

### 4b: Browser Smoke Test (optional, when Chrome DevTools MCP is available)

If the Chrome DevTools MCP server is connected and the work involved UI changes, launch a browser smoke test agent:

1. Ensure the dev server is running (`npm start` or `npm run dev:server` + `npm run dev:client`)
2. Navigate to each page affected by the implementation
3. Verify:
   - The page renders without console errors
   - Key UI elements are present and visible (use `read_page` or `find` to check for expected elements)
   - New interactive elements respond to clicks (buttons open modals, forms accept input, etc.)
   - No JavaScript exceptions in the browser console (`read_console_messages` filtered for errors)
4. Report any visual or runtime issues found

Skip this step if:
- The Chrome DevTools MCP is not connected
- The changes are server-only (domain model, projections, API) with no UI impact
- The dev server is not running and starting it would be disruptive

### 4c: Finalize

Once all verification passes:
1. Update `.planning/STATE.md` to reflect completed work
2. Mark completed requirements in `.planning/REQUIREMENTS.md`
3. Commit the changes with a descriptive message
