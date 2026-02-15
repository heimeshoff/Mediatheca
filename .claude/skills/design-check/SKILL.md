---
name: design-check
description: >
  Audit UI code against the Mediatheca design system. Checks F# (Fable/Feliz) view files and CSS
  for violations of glassmorphism rules, typography hierarchy, theme tokens, DesignSystem.fs usage,
  backdrop-filter nesting, spacing/layout conventions, animation standards, and DaisyUI 5 patterns.
  Use when the user asks to "design check", "check design", "audit styles", "review UI code",
  "check glassmorphism", or any request to verify UI code follows the design system.
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

4. **Read each target file** and check against all 9 rule categories:
   - Glassmorphism on overlays
   - backdrop-filter nesting
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
   | 1 | Glassmorphism | 42 | Opaque `bg-base-200` on dropdown | Use `DesignSystem.glassDropdown` or `bg-base-200/60 backdrop-blur-[24px]` |

   ### Summary
   - Files checked: N
   - Violations: N (X critical, Y minor)
   - Critical = glassmorphism missing on overlay, backdrop-filter nesting, hardcoded colors
   - Minor = missing DesignSystem helper, non-standard animation duration
   ```

6. **Offer to fix.** After reporting, ask if the user wants violations auto-fixed.

## Severity

- **Critical:** Glassmorphism missing on overlays, backdrop-filter nesting bugs, hardcoded hex/rgb colors
- **Minor:** Could use DesignSystem helper, non-standard animation timing, missing entrance animation
