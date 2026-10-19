---
name: spartan-regen
description: "Homestead-specific recipe for re-installing / regenerating Spartan helm components and the post-regen verification chain. Use whenever the user wants to reinstall, regenerate, refresh, or upgrade Spartan/helm components (or bump @spartan-ng/brain / @spartan-ng/cli), or hits the CLI's 'already installed' skip. For everyday Spartan usage (composing components, styling, forms, adding a brand-new component, healthcheck) use the official `spartan` skill instead — this skill only covers what that one doesn't know about this repo."
---

# Spartan helm regen (Homestead)

The official `spartan` skill (`.claude/skills/spartan`) covers composition, styling, the CLI, and adding new components. This skill covers the one thing it has no story for — **getting fresh helm templates for components that are already installed** — plus the runtime gotchas this repo has hit doing it.

Project facts (also in `Homestead.WebApp/CLAUDE.md`):

- Run every CLI command from `Homestead.WebApp/` via `npx ng g @spartan-ng/cli:<generator>` (Angular CLI workspace; bare `ng` is not on PATH).
- `components.json`: `componentsPath` = `src/app/shared/components/spartan`, `importAlias` = `@/shared/components/spartan`. Confirm with `npx ng g @spartan-ng/cli:info --json`.
- Vendored helm is 1.6.3-generated and the installed `@spartan-ng/cli`/`brain` are 1.6.3, so a regen today is same-version. Bump the packages first if you want newer templates.
- Before a version bump, scope the helm impact by diffing the published CLI tarballs (not the GitHub compare API — it silently caps at 300 files): `npm pack @spartan-ng/cli@<old> @spartan-ng/cli@<new>` into an empty scratch dir, extract each, then `diff -rq <old>/package/src/generators/ui <new>/package/src/generators/ui`. Changed `libs/<name>/files/` templates for installed components need that component regenerated; a changed `style-nova.css` (the class map applied to every helm file — `components.json` style is `nova`) or generator `.js` logic means a full regen. 1.5.0 → 1.6.3 touched only `dropdown-menu` (trigger + sub-trigger).
- Never hand-edit files under `componentsPath` — work around helm bugs in consuming code or flag upstream.

## Re-installing a single component

There is no `update`/`reinstall` generator. `hlmBaseGenerator` checks for the component's tsconfig path alias and, if present, prints "Skipping — it's already installed!" and exits. So deleting the directory alone does nothing — the alias must go too.

```bash
cd Homestead.WebApp
rm -rf src/app/shared/components/spartan/<name>
# remove "@/shared/components/spartan/<name>" from tsconfig.json compilerOptions.paths
npx ng g @spartan-ng/cli:ui <name>
```

The generator recreates the files **and** re-adds the alias (`"@/shared/components/spartan/<name>": ["./src/app/shared/components/spartan/<name>/src/index.ts"]`). Verify the directory, `src/index.ts` barrel, and alias exist.

## Bulk re-install (all components)

The no-arg `npx ng g @spartan-ng/cli:ui` opens an interactive multi-select that can't be scripted (`</dev/null` cancels it and generates nothing). Loop one component per command — `utils` first, others import from it:

```bash
cd Homestead.WebApp
names=$(ls src/app/shared/components/spartan/)
rm -rf src/app/shared/components/spartan/*/
# remove every "@/shared/components/spartan/*" alias from tsconfig.json (keep "@/*")
npx ng g @spartan-ng/cli:ui utils </dev/null
for c in $names; do [ "$c" = utils ] || npx ng g @spartan-ng/cli:ui "$c" </dev/null; done
```

The regen re-adds every alias, so `git diff tsconfig.json` should be empty afterwards. A full regen on a clean tree is also the cheapest way to confirm the vendored helm has no local drift — the diff should contain only the upstream template changes.

## Known regen gotchas

A green `ng build` **and** a clean `healthcheck` are **not sufficient** — the worst regressions here are runtime-only. Always finish with the `self-qa` subagent.

- **There is no `icon` helm.** Upstream removed it in 1.1.0 and this app no longer vendors one; icons are plain `<ng-icon>` + `NgIcon`, sized per the official skill's `rules/icons.md`. If a regen or `ui icon` ever recreates `components/spartan/icon`, delete it and its alias.
- **Date-picker `min`/`max` → `minDate`/`maxDate` (1.3.x).** Build error, so it's caught: rename bindings on every `<hlm-date-picker>` (8 component files). `hlm-calendar` still takes `min`/`max`.
- **`HlmButton` via `hostDirectives` → runtime NG0309.** `hlmAlertDialogCancel`, `hlmAlertDialogAction`, `hlmInputGroupButton`, `hlmSidebarTrigger` bring `HlmButton` themselves (exposing `variant`/`size`). Adding `hlmBtn` alongside double-applies `BrnButton` → `NG0309 "matches multiple times"` at runtime only (build passes; only opening the dialog / self-QA catches it). Drop the redundant `hlmBtn`, pass `variant`/`size` as inputs. `hlmDialogClose`, `hlmSheetClose`, `hlmDialogTrigger`, `hlmAlertDialogTrigger`, `hlmPopoverTrigger`, `hlmDropdownMenuTrigger` do **not** bring it, so pairing those with `hlmBtn` stays correct. After a regen, `grep -r hostDirectives src/app/shared/components/spartan` and diff the set against this list.
- **`modules/documents/detail/ai-date-picker-input` copies the `hlm-date-picker-input` template.** It subclasses `HlmDatePickerInput` to put AI ✓/✗ in the addon (no projection slot there, and helm files aren't edited), so its template is a hand copy that a regen won't update. After any regen, diff `src/app/shared/components/spartan/date-picker/src/lib/hlm-date-picker-input.ts`'s template against it and port changes (new bindings, renamed brain members); the only intended differences are the `@if (aiPending())` branch and the `originalLabel` tooltip on the `<input>`.
- **`HlmDatePickerConfig.parseDate` returns `T | null`** (not `undefined`); `dateChange` emits `T | null`. Handled in `shared/utils/date-picker-config.ts` — keep the `null` returns.
- **Silent behavioural deltas to re-check** (no-ops as of 1.6.3): `hlm-sidebar.service` seeds open state from `HlmSidebarConfig.defaultOpen` (app never calls `provideHlmSidebarConfig`); date pickers' `align` input (default `'center'`) shadows any global `BrnPopoverConfig.align`; date-picker inputs stamp `aria-invalid`/`data-*` only when bound to an `NgControl` (this app binds signals, so nothing renders); `hlm-combobox-input` stamps `aria-label="Toggle options"`/`"Clear selection"`, which can make accessible-name selectors newly ambiguous in specs/E2E.

## Verification chain (after any regen)

```bash
cd Homestead.WebApp
npx ng g @spartan-ng/cli:healthcheck
npm run build      # check "Initial total" hasn't jumped — see CLAUDE.md bundle note
npm test
```

Then run the `self-qa` subagent against the running Aspire stack, exercising at least one alert-dialog, one date picker, the sidebar trigger, and a combobox.
