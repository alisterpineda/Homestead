---
name: spartan-ng
description: "SpartanNG CLI operations: adding UI components, re-installing/updating components, running health checks, and managing Helm files. Use this skill whenever the user wants to add a new Spartan/shadcn-style UI component, regenerate or update an existing Spartan component, run Spartan health checks, or troubleshoot Spartan component issues. Also trigger when the user mentions 'hlm', 'helm', '@spartan-ng', or refers to UI primitives like button, dialog, select, checkbox, etc. in the context of adding or updating them."
---

# SpartanNG CLI

This skill covers how to use the `@spartan-ng/cli` to add, re-install, and maintain Spartan UI components in this project. Spartan follows a two-layer architecture (like shadcn/ui for Angular):

- **Brain** (`@spartan-ng/brain/*`): Headless, accessible primitives installed as npm packages. Updated via normal `npm update`.
- **Helm** (`hlm-*` files): Tailwind-styled wrappers **copied into your codebase** by the CLI. You own these files.

The CLI generates the Helm layer. Brain is just an npm dependency.

## Project Configuration

The CLI reads its configuration from `components.json` at the Angular workspace root (`Homestead.WebApp/components.json`). This file tells the CLI where to put generated files and what import alias to use:

```json
{
  "componentsPath": "src/app/shared/components/spartan",
  "importAlias": "@/shared/components/spartan"
}
```

- **`componentsPath`**: Where Helm component files are generated (relative to the Angular workspace root)
- **`importAlias`**: The TypeScript path alias prefix used for imports

These values drive the paths throughout this skill. If they change, the behavior follows — read `components.json` to confirm current values rather than assuming hardcoded paths.

## Working Directory

All CLI commands in this skill must run from the **Angular workspace root** (`Homestead.WebApp/`), not the repository root. The repo root is one level above. Either `cd` into it or use an explicit path:

```bash
cd Homestead.WebApp
```

The CLI expects `components.json`, `angular.json`, and `package.json` to be in the current directory.

## Adding a New Component

From the Angular workspace root:

```bash
npx ng g @spartan-ng/cli:ui <component-name>
```

The CLI reads `componentsPath` from `components.json` to determine where to place the files — no `--directory` flag needed.

Examples:

```bash
# Add the toggle component
npx ng g @spartan-ng/cli:ui toggle

# Add the switch component
npx ng g @spartan-ng/cli:ui switch

# Interactive picker (select one or more from the list)
npx ng g @spartan-ng/cli:ui
```

### After Adding a Component

The CLI generates the Helm files, installs the Brain dependency, and registers the TypeScript path alias in `tsconfig.json` automatically. Verify the alias was added correctly — it should follow this pattern in `compilerOptions.paths`:

```json
"<importAlias>/<component-name>": [
  "./<componentsPath>/<component-name>/src/index.ts"
]
```

For example, with the current config, after adding `toggle` you should see:

```json
"@/shared/components/spartan/toggle": [
  "./src/app/shared/components/spartan/toggle/src/index.ts"
]
```

If the alias is missing (check `tsconfig.json`), add it manually following the pattern above.

### Verifying the Component Was Added Correctly

1. Check the generated directory exists under the `componentsPath`
2. Check the barrel export exists at `<componentsPath>/<component-name>/src/index.ts`
3. Check the tsconfig path alias was registered
4. Verify imports resolve by building: `npx ng build`

## Re-installing / Updating a Component

The CLI has no dedicated "update" or "reinstall" command. Since Helm files are copies in your codebase, the way to get fresh templates is to delete, unregister, and regenerate.

The CLI's generator has an **"already installed" guard** (`hlmBaseGenerator` in `generators/base/generator.js`) that checks whether the component's tsconfig path alias exists. If it finds the alias, it prints "Skipping — it's already installed!" and exits without regenerating any files. So you must remove the tsconfig alias too, not just the files.

Steps:

1. **Delete the existing component directory** (under the `componentsPath`):
   ```bash
   rm -rf <componentsPath>/<component-name>
   ```

2. **Remove the tsconfig path alias** for the component from `tsconfig.json` `compilerOptions.paths`. For example, remove the `"<importAlias>/<component-name>"` entry. This unblocks the CLI's guard so it will actually regenerate.

3. **Re-run the CLI generator:**
   ```bash
   npx ng g @spartan-ng/cli:ui <component-name>
   ```
   The CLI regenerates the Helm files **and** re-adds the tsconfig path alias automatically.

4. **Verify** the files were regenerated and the tsconfig alias was re-added.

This gives you a fresh copy of the Helm templates from the currently installed `@spartan-ng/cli` version. If you've upgraded the CLI package first (`npm update @spartan-ng/cli`), the regenerated files will reflect the newer templates.

**Important:** This project's convention is to keep generated Helm files faithful to the CLI templates — do not hand-edit files under the `componentsPath`. If a component has a bug or missing feature, work around it in the consuming component code or flag it as an upstream issue.

### Bulk Re-install

To re-install all components (e.g., after a major Spartan version upgrade):

1. **Remove all existing helm files:**
   ```bash
   rm -rf <componentsPath>/*/
   ```

2. **Remove all `<importAlias>/*` path aliases** from `tsconfig.json` `compilerOptions.paths` (keep non-Spartan aliases like `@/*`).

3. **Re-generate everything** (interactive picker — select all):
   ```bash
   npx ng g @spartan-ng/cli:ui
   ```
   The CLI re-adds all tsconfig aliases during generation.

Then verify the build passes and run health checks (see below).

**Driving generation non-interactively:** the no-arg `npx ng g @spartan-ng/cli:ui` opens an interactive multi-select picker that can't be driven from a script (piping `</dev/null` cancels it, generating nothing). Instead loop one component per command — `utils` first (others import from it), then the rest by directory name:

```bash
for c in $(ls src/app/shared/components/spartan/); do npx ng g @spartan-ng/cli:ui "$c" </dev/null; done
```

### Known regen gotchas (nova)

> **Versions are aligned:** the vendored helm is 1.3.2-generated and the installed `@spartan-ng/cli`/`@spartan-ng/brain` are 1.3.2, so a `ui:*` generation is a same-version regen again. The one deliberate exception is `components/spartan/icon` (see below). Still run the full verification chain (build + healthcheck + `npm test` + `self-qa`) after any regen.

A green `ng build` **and** a clean `healthcheck` are **not sufficient** after a regen — the worst classes here are runtime-only regressions. Always run the `self-qa` subagent afterward.

- **The `icon` helm was removed in 1.1.0 and has not come back.** There is still no `ui:icon` template; regenerated helm imports `NgIcon`/`provideIcons` straight from `@ng-icons/core`. But this app uses `HlmIconImports` (~86 files) and `<ng-icon hlm size="sm|xs">` (~448 sites) that rely on the `HlmIcon` directive's token→px sizing. **Do NOT migrate** — restore the vendored `icon` helm from git and keep its tsconfig alias; it's self-contained and decoupled from the new helm, and the `Helm Icons` healthcheck passes with it:
  ```bash
  git checkout HEAD -- Homestead.WebApp/src/app/shared/components/spartan/icon
  # then ensure "@/shared/components/spartan/icon" is still in tsconfig.json paths
  ```
- **Date-picker `min`/`max` → `minDate`/`maxDate` (1.3.x).** The only app-facing break of the 1.1.0 → 1.3.2 regen. It *is* a build error, so it's caught: rename the bindings on every `<hlm-date-picker>` (8 component files here). `hlm-calendar` still takes `min`/`max`; only the picker wrappers renamed.
- **`hlmAlertDialogCancel` / `hlmAlertDialogAction` (also `hlmInputGroupButton`, `hlmSidebarTrigger`) bring `HlmButton` via `hostDirectives`** (exposing `variant`/`size`). A button that adds `hlmBtn` alongside them double-applies `BrnButton` → **runtime `NG0309` "matches multiple times"** (build passes; only opening the dialog / self-QA catches it). Drop the redundant `hlmBtn`, keep the directive, pass `variant`/`size` as its inputs. Note `hlmDialogClose`, `hlmSheetClose`, `hlmDialogTrigger`, `hlmAlertDialogTrigger`, `hlmPopoverTrigger`, and `hlmDropdownMenuTrigger` do **not** bring `HlmButton`, so pairing them with `hlmBtn` remains correct. After a regen, re-grep the helm tree for `hostDirectives` and diff the set — that's the cheap way to catch a newly added `HlmButton` host directive before it becomes an NG0309.
- **`HlmDatePickerConfig.parseDate` returns `T | null`** (not `undefined`) and `dateChange` emits `T | null`. Already handled in `shared/utils/date-picker-config.ts`; the 1.3.2 regen left `hlm-date-picker.token.ts` byte-identical, so this did **not** re-fire. Keep the `null` returns if you ever rewrite that file.
- **Silent behavioral deltas to re-check after a regen** (all no-ops for this app as of 1.3.2, but cheap to re-verify): `hlm-sidebar.service` reads its initial open state from `HlmSidebarConfig.defaultOpen` (default `true`; app never calls `provideHlmSidebarConfig`); date pickers gained an `align` input defaulting `'center'`, which now shadows any global `BrnPopoverConfig.align` (app has no override, and the brain default is already `'center'`); date-picker *inputs* stamp `aria-invalid`/`data-invalid`/`data-touched`/`data-dirty` from an injected `BrnFieldControl`, but those signals stay `null` unless the control is bound to an `NgControl` (this app binds `[date]`/`(dateChange)` signals, so nothing new renders); `hlm-combobox-input` stamps `aria-label="Toggle options"`/`"Clear selection"` on its icon buttons, which can make an accessible-name selector newly ambiguous in specs/E2E.

## Updating Brain Primitives

Brain is a regular npm package — update it with your package manager:

```bash
npm update @spartan-ng/brain
```

After a major Brain update, re-install the Helm components too (see above) since templates may have changed to match new Brain APIs. Then run health checks.

## Health Checks

The healthcheck command scans your codebase for deprecated patterns, outdated imports, naming convention violations, and component migration issues. It runs 26 diagnostic checks.

```bash
npx ng g @spartan-ng/cli:healthcheck
```

With automatic fixing (no prompts):

```bash
npx ng g @spartan-ng/cli:healthcheck --autoFix
```

### What It Detects

- **Version check**: Whether `@spartan-ng/brain` and `@spartan-ng/cli` are on the latest version
- **Deprecated imports**: Old `@spartan-ng/ui-*-brain` or `@spartan-ng/ui-*-helm` import paths
- **Core imports**: Deprecated `@spartan-ng/ui-core` (should be `@spartan-ng/brain/core`)
- **Module imports**: NgModule-based imports that should be standalone
- **Naming conventions**: `Hlm*Directive`/`Hlm*Component` suffixes that were dropped in newer versions
- **Component-specific issues**: Migration issues for dialog, select, radio, toggle, checkbox, separator, accordion, progress, date-picker, scroll-area, sonner, icon, and menu components
- **Event renames**: Deprecated event names like `changed` that were renamed to `checkedChange`

### When to Run Health Checks

- After updating `@spartan-ng/brain` or `@spartan-ng/cli` packages
- After re-installing/regenerating Helm components
- When migrating to a new Spartan major version
- Periodically, to catch deprecated patterns early

## Available Components

The CLI can generate these 58 components:

accordion, alert, alert-dialog, aspect-ratio, autocomplete, avatar, badge, breadcrumb, button, button-group, calendar, card, carousel, checkbox, collapsible, combobox, command, context-menu, date-picker, dialog, dropdown-menu, empty, field, form-field, hover-card, icon, input, input-group, input-otp, item, kbd, label, menubar, native-select, navigation-menu, pagination, popover, progress, radio-group, resizable, scroll-area, select, separator, sheet, sidebar, skeleton, slider, sonner, spinner, switch, table, tabs, textarea, toggle, toggle-group, tooltip, typography, utils

## Migration Generators

For major version upgrades, Spartan provides dedicated migration generators:

```bash
npx ng g @spartan-ng/cli:migrate-brain-imports
npx ng g @spartan-ng/cli:migrate-helm-imports
npx ng g @spartan-ng/cli:migrate-naming-conventions
npx ng g @spartan-ng/cli:migrate-module-imports
npx ng g @spartan-ng/cli:migrate-select
npx ng g @spartan-ng/cli:migrate-icon
# ... and more (migrate-radio, migrate-separator, migrate-toggle-group, etc.)
```

Run the healthcheck first to identify which migrations are needed, then run the specific migration generators, or use `--autoFix` to let the healthcheck handle it.

## Quick Reference

All commands run from the Angular workspace root (`Homestead.WebApp/`).

| Task | Command |
|---|---|
| Add a component | `npx ng g @spartan-ng/cli:ui <name>` |
| Re-install a component | Delete dir + remove tsconfig alias, then re-run add command |
| Update Brain | `npm update @spartan-ng/brain` |
| Update CLI | `npm update @spartan-ng/cli` |
| Health check | `npx ng g @spartan-ng/cli:healthcheck` |
| Health check + auto-fix | `npx ng g @spartan-ng/cli:healthcheck --autoFix` |
| Initialize (first time) | `npx ng g @spartan-ng/cli:init` |
| Set up theme | `npx ng g @spartan-ng/cli:ui-theme` |
