# Status Skill

Use when: User asks "where are we?", runs /status, or starts a new session

## Step 1: Show Status

1. Read `.planning/STATE.md`, `.planning/ROADMAP.md`, and `.planning/REQUIREMENTS.md`
2. Summarize concisely: current phase, recent progress, active decisions, blockers
3. List the next actions from STATE.md as the current task list

## Step 2: Ask About Modeling

Ask the user (using AskUserQuestion):

> "Before we start implementing — do you have any further thoughts, questions, or modeling you'd like to explore first?"

### If YES → Enter Modeling Mode

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

## Step 3: Execute

1. Create a task list (using TaskCreate) from the next actions
2. Start working through the tasks in order, marking them in-progress and completed as you go
3. After completing tasks, update `.planning/STATE.md` to reflect progress
