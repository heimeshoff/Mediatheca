---
name: design-check
description: >
  Audit UI code against the Mediatheca design system. Checks F# (Fable/Feliz) view files and CSS
  for violations of paper-overlay rules, typography hierarchy, theme tokens, DesignSystem.fs usage,
  spacing/layout conventions, animation standards, and DaisyUI 5 patterns.
  Use when the user asks to "design check", "check design", "audit styles", "review UI code",
  "check paper overlay", or any request to verify UI code follows the design system.
---

# Design Check

Audit UI source files against the Mediatheca design system conventions.

## Workflow

1. **Identify target files.** If the user specifies files, use those. Otherwise, scan recently changed view files:
   ```
   git diff --name-only HEAD~3 -- 'src/Client/**/*.fs' 'src/Client/**/*.css'
   ```
   Focus on `Views.fs`, component files, and `index.css`.

2. **Load design rules.** Read `references/design-rules.md` for the full rule set.

3. **Read the current DesignSystem.fs** (`src/Client/DesignSystem.fs`) to know which helpers exist.

4. **Read each target file** and check against all 8 rule categories:
   - Paper overlay on floating surfaces (never glassmorphism/backdrop-filter)
   - Typography (fonts, hierarchy, headings)
   - Theme & colors (semantic tokens vs hardcoded)
   - Spacing & layout (responsive grids, DesignSystem padding)
   - Animations (standard durations/classes)
   - Shadows (token system)
   - DaisyUI 5 component usage
   - DesignSystem.fs helper usage

5. **Report findings** in this format:

   ```
   ## Design Check Report

   ### <filename>

   **Pass** / **X violation(s) found**

   | # | Rule | Line(s) | Issue | Fix |
   |---|------|---------|-------|-----|
   | 1 | Paper Overlay | 42 | `backdrop-blur-sm` on dropdown | Use `DesignSystem.paperDropdown` (`.rating-dropdown`) |

   ### Summary
   - Files checked: N
   - Violations: N (X critical, Y minor)
   - Critical = backdrop-filter/backdrop-blur present anywhere, translucent overlay background, hardcoded colors
   - Minor = missing DesignSystem helper, non-standard animation duration
   ```

6. **Offer to fix.** After reporting, ask if the user wants violations auto-fixed.

## Severity

- **Critical:** `backdrop-filter`/`backdrop-blur` anywhere in the codebase, translucent (non-opaque) backgrounds on a dropdown/popover/modal/floating panel, hardcoded hex/rgb colors
- **Minor:** Could use DesignSystem helper, non-standard animation timing, missing entrance animation
