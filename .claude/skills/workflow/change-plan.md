# Change Plan Skill

Use when: User has a new feature idea while already mid-implementation in a later phase. This is NOT for starting a new project (use `/brainstorm`) or creating initial plans (use `/plan-project`). This is for capturing and slotting new ideas into an existing, active planning structure.

## Key Principle

Capture without disrupting. The user is in the middle of building something — the goal is to properly record the idea, decide where it belongs, and get back to work. Keep it lightweight.

## Process

### Step 1: Capture

Ask the user to describe their idea briefly. If they already stated it (e.g., "I have an idea for tagging movies"), acknowledge it and move on — don't ask them to repeat themselves.

Follow up with at most 2-3 focused questions. Pick only what's relevant:

- "What would the user experience look like?" (if the idea is vague)
- "Does this relate to an existing bounded context, or is it something new?"
- "Is this something you'd want in the current MVP, or is it a future thing?"

Do NOT run the full brainstorm interview (problem, user, success criteria, constraints, scope). The project context already exists.

### Step 2: Contextualize

Read the current planning files to understand where things stand:
- `.planning/REQUIREMENTS.md` — what's already planned
- `.planning/ROADMAP.md` — current phases and their scope
- `.planning/STATE.md` — what phase we're in, what's in progress

### Step 3: Classify

Present the user with a classification (using AskUserQuestion):

> "Where should this idea land?"

Options:
- **Current phase** — Add as a new requirement in the phase we're currently implementing. Use this if the idea is small, closely related to current work, and won't delay the phase.
- **Later phase** — Add as a new requirement in a future v1 phase. Use this if it's clearly MVP-worthy but doesn't belong in the current phase.
- **v2 (future)** — Park it in the v2 section of REQUIREMENTS.md. Use this if it's a nice-to-have that shouldn't distract from v1.
- **Needs modeling first** — The idea is significant enough that it needs a proper modeling conversation before it can be classified. Transition into the `/status` Step 2 modeling mode.

### Step 4: Update Planning Files

Based on the classification:

**If current phase:**
1. Add a new `REQ-XXX` to REQUIREMENTS.md under the current phase (use the next available number)
2. Add it to the phase's requirement list in ROADMAP.md
3. Add it to the "Next Actions" in STATE.md
4. Log the decision in STATE.md under "Recent Progress" with timestamp

**If later phase:**
1. Add a new `REQ-XXX` to REQUIREMENTS.md under the target phase
2. Add it to that phase's requirement list in ROADMAP.md
3. Log the decision in STATE.md under "Recent Progress" with timestamp

**If v2:**
1. Add a new `REQ-XXX` to the v2 section of REQUIREMENTS.md
2. Log the decision in STATE.md under "Recent Progress" with timestamp

**If needs modeling:**
1. Log the idea in STATE.md under "Active Decisions" as pending
2. Tell the user: "Let's model this properly. Starting a modeling conversation."
3. Follow the modeling mode from `.claude/skills/workflow/status.md` Step 2b (If YES → Enter Modeling Mode)
4. After modeling, return to this skill's Step 3 to classify and file the idea

### Step 5: Confirm and Return

Show the user what changed in the planning files (a brief diff summary, not full file contents). Then:

> "Idea captured. Ready to continue with [current task from STATE.md]?"
