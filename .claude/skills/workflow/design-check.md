# Design Check Skill

Use when: User runs /design-check, asks to audit design system usage, or wants to find hardcoded class strings that should use DesignSystem.fs.

## Purpose

Enforcement/audit tool that scans client source files for hardcoded Tailwind/DaisyUI class strings that duplicate compositions already available in `src/Client/DesignSystem.fs`. Violations mean the code is bypassing the centralized design system, making future style changes harder to propagate.

## Step 1: Scan for Violations

Use Grep to search `src/Client/` F# files for hardcoded class strings that match DesignSystem compositions. **Exclude** `DesignSystem.fs` itself and anything under `fable_modules/`.

Run the following searches in parallel, grouping results by category:

### Typography violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `text-4xl font-display uppercase tracking-wider text-gradient-primary` | `DesignSystem.pageTitle` | required |
| `text-2xl font-display uppercase tracking-wider` | `DesignSystem.sectionHeader` | required |
| `text-lg font-display uppercase tracking-wider` | `DesignSystem.cardTitle` | required |
| `text-sm font-display uppercase tracking-wider` | `DesignSystem.subtitle` | required |

### Glass effect violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `bg-base-100/55 backdrop-blur` (any variant) | `DesignSystem.glassCard` | required |
| `bg-base-100/70 backdrop-blur-xl` | `DesignSystem.glassOverlay` | required |
| `bg-base-100/50 backdrop-blur-sm` | `DesignSystem.glassSubtle` | required |

### Layout violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `p-4 lg:p-6` at page level | `DesignSystem.pagePadding` | suggested |
| `grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6` | `DesignSystem.movieGrid` or `DesignSystem.cardGrid` | required |

### Animation violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `"animate-fade-in"` (as standalone class, not part of `animate-fade-in-up`) | `DesignSystem.animateFadeIn` | suggested |
| `"animate-fade-in-up"` | `DesignSystem.animateFadeInUp` | suggested |
| `"stagger-grid"` | `DesignSystem.staggerGrid` | suggested |

### Component class violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `"poster-card"` | `DesignSystem.posterCard` | required |
| `"poster-image-container"` | `DesignSystem.posterImageContainer` | required |
| `"poster-image"` | `DesignSystem.posterImage` | required |
| `"poster-shine"` | `DesignSystem.posterShine` | required |
| `"card-hover"` | `DesignSystem.cardHover` | required |
| `"stat-glow"` | `DesignSystem.statGlow` | required |

### Modal violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `fixed inset-0 z-50` with modal-like context | `DesignSystem.modalContainer` | required |
| `absolute inset-0 bg-black/30` | `DesignSystem.modalBackdrop` | required |

### Nav item violations

| Pattern to search | Should use | Severity |
|---|---|---|
| `nav-glow flex items-center` | `DesignSystem.navItemClass` | required |

### Search implementation notes

- Use `Grep` with `glob: "*.fs"` and `path: "src/Client/"`.
- For each search, exclude results from `DesignSystem.fs` by filtering out that filename from the results.
- Also exclude any results from paths containing `fable_modules`.
- For "suggested" severity: these patterns may appear as substrings in larger, legitimately different class strings. Verify the match is actually a duplication before reporting.
- For animation patterns, be careful: `animate-fade-in` should NOT match lines that contain `animate-fade-in-up` (unless both are present). Search for `animate-fade-in` then exclude lines that only have `animate-fade-in-up`.

## Step 2: Report Findings

For each violation found, report in a structured table per category:

```
### Typography Violations (X found)

| File | Line | Hardcoded String | Should Use | Severity |
|------|------|-----------------|------------|----------|
| src/Client/Pages/Movies/Views.fs | 42 | "text-4xl font-display uppercase tracking-wider text-gradient-primary" | DesignSystem.pageTitle | required |
```

If a category has zero violations, report it as clean:

```
### Typography Violations
All clean.
```

## Step 3: Summary

After reporting all categories, show a summary block:

```
## Design System Compliance Summary

- **Total violations:** X (Y required, Z suggested)
- **Files with violations:** list of filenames
- **Clean files:** count of scanned files with no violations
- **Most common violation type:** e.g., "Typography (5 occurrences)"
- **Compliance rate:** X% (files without violations / total files scanned)
```

To calculate compliance rate: count the total number of `.fs` files in `src/Client/` (excluding `DesignSystem.fs` and `fable_modules/`), count how many have zero violations, and compute the percentage.

## Step 4: Auto-fix Option

After presenting the report, ask the user:

> "Found X violations. Would you like me to auto-fix them? I will replace hardcoded class strings with references to `DesignSystem` module values."

**If YES:**

For each violation, use the Edit tool to replace the hardcoded string with the DesignSystem reference. The replacement depends on context:

- If the hardcoded string is the entire `prop.className` or `prop.classes` value, replace the string literal with the DesignSystem binding (e.g., `"text-4xl font-display uppercase tracking-wider text-gradient-primary"` becomes `DesignSystem.pageTitle`).
- If the hardcoded string is concatenated with other classes, replace only the matching portion and concatenate with the DesignSystem binding (e.g., `"text-4xl font-display uppercase tracking-wider text-gradient-primary mb-4"` becomes `DesignSystem.pageTitle + " mb-4"`).
- Ensure `open Mediatheca.Client.DesignSystem` or `open Mediatheca.Client` is present at the top of any modified file. If not, add it.
- After all fixes, run `npm run build` to verify the changes compile correctly.

**If NO:**

Acknowledge and end. The report remains for reference.
