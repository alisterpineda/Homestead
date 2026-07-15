# Homestead.WebApp

For generic Angular/TypeScript guidance (reactivity, components, forms, routing, accessibility, Tailwind), consult the `angular-developer` skill. For Spartan/shadcn CLI operations, consult the `spartan-ng` skill. This file covers Homestead-specific architecture and conventions.

## Commands

All commands run from `Homestead.WebApp/`:

```bash
npm install          # Install dependencies
npm start            # Dev server on port 4200 with API proxy
npm run build        # Production build
npm test             # Run tests (Vitest)
npm run generate-api # Regenerate API clients from Homestead.WebApi/obj/Homestead.WebApi.json via ng-openapi-gen
```

## Architecture

- **UI Library**: Spartan.NG (Angular port of shadcn/ui) — uses `@spartan-ng/brain` primitives + `hlm` (helm) UI directives/components generated via `@spartan-ng/cli`
- **Icons**: `@ng-icons/core` + `@ng-icons/lucide` — usage: `<ng-icon hlmIcon name="lucideIconName" />` with `provideIcons()` in component providers
- **Modules** (`src/app/modules/`): Self-contained feature areas organized by domain-first vertical slices. Each module groups its pages, components, and services into sub-domain directories (not flat `components/`/`pages/`/`services/` folders). Registered in `modules/registry.ts`
  - **Documents** (`modules/documents/`): `layout/` (shell + sidebar), `list/` (document list page + table/grid/upload/bulk-edit components), `detail/` (document detail page + version/reprocess dialogs + edit-state services), `tags/` (tag list + table/badge/input/create/edit + color-picker), `parties/` (party list + table/badge/input/create/edit), `trash/` (trash list page)
  - **Finances** (`modules/finances/`): `layout/` (shell + sidebar), `accounts/` (account list + detail pages, table/card-grid/row-actions, activity + positions tabs, create dialog + edit/trade/transfer/dividend/drip/opening-balance/balance-entry/add-position sheets), `commodities/` (commodity list + detail pages, prices section/table, create dialog/edit sheet, price dialogs, list/prices state services), `reports/` (net-worth chart + range/month pickers), `transactions/` (shared transaction display helpers), `settings/` (general + price-sources), `shared/` (account/commodity/currency pickers, date & currency formatting, finance-settings + privacy auto-mask services)
  - **Admin** (`modules/admin/`): `admin-sidebar/`, `admin-index/`, `users/` (user list + table + create dialog/edit sheet), `groups/` (group list + detail + create/edit/member dialogs), `schedules/` (schedule list + create dialog), `settings/`, `jobs/`
- **Routing**: Angular Router with lazy-loaded feature routes defined in `app.routes.ts`. Each module has its own `*.routes.ts` file. `/login` and `/pending-activation` are public routes; the `AppShellComponent` route is protected by `authGuard`
- **Authentication**: Cookie-based (raw cookie auth, no ASP.NET Core Identity), with OIDC as the sole sign-in method. `AuthService` (`shared/services/auth.service.ts`) manages auth state and exposes `isAuthenticated`, `isActive`, `userName`, `userEmail`, `role`, and `oidcDisplayName` signals. `authGuard` redirects unauthenticated users to `/login` and inactive users to `/pending-activation`. `loginGuard` bounces already-authenticated users to `/documents`. The empty-path `/` (the PWA cold-start `start_url`) restores the last-used top-level module via a functional `redirectTo` (`shared/guards/last-module-redirect.ts` reading `UserPreferencesService.lastModulePath`), falling back to `/documents` when nothing valid is recorded — module-root granularity only, and `/admin` restore is still gated by the child `adminGuard`; login/OIDC landing is unchanged (`/documents`). The auth interceptor redirects to `/login` on 401 responses. No Bearer tokens — cookies are sent automatically for same-origin requests. OIDC login/logout use full-page redirects to backend endpoints (not AJAX)
- **Auth pages**: Login (`shared/components/auth/login/`) renders a single SSO button; Pending Activation (`shared/components/auth/pending-activation/`) shows when a user is authenticated but not yet activated
- **API layer**: ng-openapi-gen generates functions and TypeScript models from the backend OpenAPI spec (`src/app/api/generated/`). Domain-scoped service wrappers in `src/app/api/services/` (e.g., `DocumentApiService`, `AdminApiService`) encapsulate `HttpClient`/`ApiConfiguration` and unwrap `StrictHttpResponse<T>` to `Observable<T>`. Components inject these services — never import generated functions or `HttpClient` directly for API calls. Barrel export from `@/api/services/index.ts`
- **Page Header**: The app shell renders a single unified header bar with sidebar trigger, page title, and optional action buttons. Page titles come from route `data: { title }` in each `*.routes.ts` file. Action buttons are projected via `PageHeaderService` (`shared/services/page-header.service.ts`) — pages with actions define an `<ng-template #headerActions>` and register it with `PageHeaderService.setActions()` in `afterNextRender`, clearing in `ngOnDestroy`. Actions are also auto-cleared on `NavigationStart`. Pages without actions need no service interaction — just set the route `data.title`.
- **Document List Search**: The document list uses URL query param `search` for full-text search, debounced at 300ms. The search term is maintained as a writable signal (`searchInput`) synced to the URL, with a separate `searchTerm` signal reading from query params for the API call pipeline.
- **Shared**: `src/app/shared/components/layout/` for app shell/sidebar, `src/app/shared/components/theme/` for dark mode, `src/app/shared/services/` for shared services
- **State persistence tiers**:
  - **`localStorage`** via `persistedSignal()` (`shared/utils/persisted-signal.ts`): User preferences that survive across sessions (theme, view mode, column visibility, page size, last-used module). `UserPreferencesService` (`shared/services/user-preferences.service.ts`) owns cross-cutting preferences
  - **`sessionStorage`**: Transient editing state that survives refresh but not session close (document edit drafts via `DocumentEditStateService`)
  - **In-memory signals**: Ephemeral state that resets on navigation (search filters, selections, sort, URL params via list state services)
- **Styling**: Tailwind CSS v4 with Spartan.NG OKLCH CSS variable theming (neutral palette)

- **Error handling**: Centralized via `ErrorHandlerService` (`shared/services/error-handler.service.ts`). The backend returns RFC 9457 Problem Details (`ApiProblemDetails` type in `shared/types/`). Key patterns:
  - **ErrorHandlerService**: `getErrorMessage(err, fallback)` extracts user-friendly message (prefers `detail`, then first validation error, then status-based fallback). Also: `getValidationErrors()`, `getErrorCode()`, `handleError()` (auto-toasts via ngx-sonner)
  - **Error interceptor** (`shared/interceptors/error.interceptor.ts`): Auto-toasts 5xx and network errors (status 0). 4xx errors pass through for component-level handling
  - **RxJS operators** (`shared/utils/rxjs-error.utils.ts`): `handleMutationError(errorHandler, loadingSignal, fallback)` — resets loading, toasts 4xx only (5xx handled by interceptor), completes stream. `handleQueryError(loadingSignal, errorSignal, fallbackValue)` — resets loading, sets error flag, emits fallback (no toast — renders error state in UI)

Generated files (`src/app/api/generated/` and `src/app/shared/components/spartan/`) should not be manually edited.

## Self-QA

Before declaring a UI-affecting change complete, validate it in a real browser via the **`self-qa` subagent** (`.claude/agents/self-qa.md`) — it drives Playwright against the running Aspire stack. The durable regression suite in `Homestead.E2E/` is separate.

## Spartan.NG Patterns

- Spartan uses **brain** (headless primitives from `@spartan-ng/brain`) + **helm** (styled UI from generated `src/app/shared/components/spartan/`)
- Many Spartan UI elements are **directives** applied to native elements (e.g., `hlmBtn` on `<button>`, `hlmInput` on `<input>`, `hlmBadge` on `<span>`)
- Others are **components** (e.g., `<hlm-card>`, `<hlm-tabs>`, `<hlm-sidebar>`)
- Always import from the local `@/shared/components/spartan/` path aliases, not directly from `@spartan-ng/brain`
- **`hlm-checkbox` aria-label**: Use input binding `[aria-label]="expr"`, NOT attribute binding `[attr.aria-label]="expr"`. The host nullifies the attribute and forwards the input to the inner `brn-checkbox`. Same applies to `aria-labelledby` and `aria-describedby`
- **Bundle: never barrel-import in eagerly-loaded files.** Spartan helm barrels (`@/shared/components/spartan/<name>` → `src/index.ts`) re-export every component variant, so importing even a pure provider function from a barrel in an eager file (e.g. `app.config.ts`) drags the whole component stack into the initial bundle. Measured: pulling `provideHlmDatePickerConfig` from the date-picker barrel added ~176 kB raw / ~35 kB gzip (calendar + popover + icons + variants), loaded even on `/login`. In eager files, deep-import from the token/provider file instead — e.g. `@/shared/components/spartan/date-picker/src/lib/hlm-date-picker.token` (the `@/*` alias reaches it). Lazy-route components can keep using barrels. Check `Initial total` in `npm run build` when touching `app.config.ts` providers.
- **`HlmSidebarService.isMobile()` is inclusive at 768px** — it watches `matchMedia('(max-width: 768px)')`, TRUE at exactly 768px, whereas Tailwind's `max-md:` is `< 768px` and `md:` is `>= 768px`. At exactly 768px (iPad portrait) the service says "mobile" while CSS applies desktop classes, so pairing it with `md:`/`max-md:` classes yields malformed layouts at the boundary. For JS-driven responsive behavior, watch `matchMedia('(min-width: 768px)')` (the exact complement of `max-md:` at every width). The `injectSheetSide()` helper (`modules/inventory/shared/sheet-side.ts`) already does this test/SSR-safely — reuse it; don't inject `HlmSidebarService` just for breakpoint detection.

### Control sizing & search conventions

The vendored helm (nova, Spartan 1.1.0) sizes buttons, inputs, select-triggers, and input-groups uniformly at **h-8** (the `default`/`sm` split is h-8/h-7). Keep controls consistent with these rules:

- **Default button (h-8) is the norm** for page toolbars, page headers (`#headerActions`), and dialog/sheet footers. Reserve `size="sm"` (h-7) for genuinely dense contexts — table row actions, card footers, inline chip/preset actions, and filter-popover internals. Same split for icon buttons: `size="icon"` (size-8) in toolbars, `size="icon-sm"`/`icon-xs` for dense/inline.
- **Size controls at the group level, not per-button.** A toolbar/row is sized as a unit — never bump one control to h-8 while its row-mates stay h-7 (a lone mismatched control looks worse than a uniformly-smaller row). Some searchless list toolbars (e.g. `account-list`, `area-list`) are an intentional uniform-compact **h-7** group (`icon-sm` view toggles + `sm` sort/create) — leave them whole or bump the whole group, don't touch the create button alone. Ghost header triggers (documents Upload, app-shell global search) follow the **h-8** header-action norm, not a smaller "utility" tier.
- **Search boxes** always use the shared `app-search-input` (`shared/components/search-input/`) — never hand-roll an absolute-positioned icon + `hlmInput`. It's a `model()`-based two-way `[(value)]` control that inherits h-8 + zoom-safe font from the helm input-group.
- **Custom picker trigger buttons** (account/commodity/currency/brand/location/area-parent pickers) use the `appPickerTrigger` directive (`shared/directives/picker-trigger.directive.ts`), which mirrors `hlm-select-trigger` so they sit flush beside real select triggers and date pickers.
- **iOS zoom guard**: any native `input`/`textarea`/`select` (or a bare focusable input not using `hlmInput`) must keep a ≥16px font below `md` — compose `hlmInput`, or add `text-base md:text-sm`. Never leave a bare `text-sm`/`text-xs` as the only font size on a focusable control (mobile Safari zooms the viewport on focus). Prefer `hlm-select` over a native `<select>`.
- **No unconditional font-size/height overrides** on hlm components (e.g. `class="h-8 text-xs"` on a select-trigger, `size="sm" class="h-7 text-xs"` on a button) — pick the right variant `size` instead. Overrides that fight the variant are the anti-pattern this convention exists to prevent.
- **`max-md:h-11`** is the sanctioned mobile touch-target pattern for primary tap actions (reference: inventory quick-panels) — it's an intentional responsive bump, not a variant-fighting override.

## Homestead State Management Conventions

- Use `persistedSignal()` from `shared/utils/persisted-signal.ts` for state that must survive page refresh — never access `localStorage`/`sessionStorage` directly
- All storage keys must use the `homestead-` prefix
- Add new cross-cutting preferences to `UserPreferencesService` (`shared/services/user-preferences.service.ts`)
- **Edit state seeded from API data**: Seed writable form-field signals via `linkedSignal({ source, computation })` keyed on a composite source (entity id + mode + open/closed), so re-fetches of the same entity (e.g. via `refreshTrigger` or SignalR) don't silently overwrite pending user edits. Seven of the eight account sheets in `modules/finances/accounts/` (all except `add-position-sheet`) are the canonical example, via the `seededLinkedSignal` helper (`shared/utils/seeded-linked-signal.ts`). For deeper guidance on `linkedSignal`, `computed`, and when *not* to use `effect`, consult the `angular-developer` skill (`references/linked-signal.md`, `references/effects.md`). (`document-detail-page.component.ts` still uses an older imperative `initializedDocId`/`seededVersionKey` guard; left in place for now, but new code should follow the `linkedSignal` pattern.) Note the mirror-image hazard: a seed key must identify the *data*, not just the entity. `document-detail-page`'s version fields key on `${versionId}|${extractedAt}`, because reprocess rewrites a version's text **in place** — keyed on the id alone the textarea keeps the old text while the rest of the page moves on, and saving PATCHes it back over the new extraction. `seededLinkedSignal` cannot express this (its computation is `untracked`, so it re-seeds only on key change); fold the identity into the key, as `openGeneration` does for reopen.

## Theming

- Uses Spartan.NG default theme convention with OKLCH CSS variables defined in `src/styles.css`
- Semantic color tokens: `background`/`foreground`, `primary`/`primary-foreground`, `secondary`/`secondary-foreground`, `muted`/`muted-foreground`, `accent`/`accent-foreground`, `destructive`/`destructive-foreground`, `card`/`card-foreground`, `popover`/`popover-foreground`, `input`, `ring`, `border`, `sidebar`/`sidebar-foreground`/`sidebar-accent`/`sidebar-accent-foreground`/`sidebar-border`
- Dark mode via `.dark` class on `<html>` (toggled by `ThemeService`). `ThemeService` is eagerly instantiated in `App` root component so theming works on all routes including pre-auth pages
- Border radius system via `--radius` variable (`radius-sm`, `radius-md`, `radius-lg`, `radius-xl`)
- Font: Inter (self-hosted via `@fontsource/inter`)
- Use Tailwind classes like `bg-primary`, `text-foreground`, `text-muted-foreground`, `bg-muted`, `border-border`, etc.
- Do NOT use raw color values (e.g. `bg-gray-500`); always use semantic tokens
