# Homestead.WebApp

## Commands

All commands run from `Homestead.WebApp/`:

```bash
npm install          # Install dependencies
npm start            # Dev server on port 4200 with API proxy
npm run build        # Production build
npm test             # Run tests (Vitest)
npm run generate-api # Regenerate API clients from openapi.json via ng-openapi-gen
```

## Architecture

- **UI Library**: Spartan.NG (Angular port of shadcn/ui) — uses `@spartan-ng/brain` primitives + `hlm` (helm) UI directives/components generated via `@spartan-ng/cli`
- **Icons**: `@ng-icons/core` + `@ng-icons/lucide` — usage: `<ng-icon hlmIcon name="lucideIconName" />` with `provideIcons()` in component providers
- **Modules** (`src/app/modules/`): Self-contained feature areas organized by domain-first vertical slices. Each module groups its pages, components, and services into sub-domain directories (not flat `components/`/`pages/`/`services/` folders). Registered in `modules/registry.ts`
  - **Documents** (`modules/documents/`): `layout/` (shell + sidebar), `list/` (document list page + table/grid/upload/bulk-edit components), `detail/` (document detail page + version/reprocess dialogs + edit-state services), `tags/` (tag list + table/badge/input/create/edit + color-picker), `parties/` (party list + table/badge/input/create/edit), `trash/` (trash list page)
  - **Admin** (`modules/admin/`): `admin-sidebar/`, `admin-index/`, `users/` (user list + table/create/edit/password dialogs), `groups/` (group list + detail + create/edit/member dialogs), `text-extraction/` (engine list + create/edit dialogs + config), `settings/`, `jobs/`
- **Routing**: Angular Router with lazy-loaded feature routes defined in `app.routes.ts`. Each module has its own `*.routes.ts` file. `/login` and `/setup` are public routes; the `AppShellComponent` route is protected by `authGuard`
- **Authentication**: Cookie-based via ASP.NET Core Identity, with optional OIDC. `AuthService` (`shared/services/auth.service.ts`) manages auth state and exposes `isAuthenticated`, `needsSetup`, `userName`, `userEmail` signals plus OIDC state (`oidcEnabled`, `oidcDisplayName`, `localLoginEnabled` — derived via `computed()` from a single `oidcStatus` signal). `authGuard` protects routes (redirects to `/setup` or `/login`; skips setup redirect when OIDC is enabled). `setupGuard` restricts `/setup` to when no users exist. The auth interceptor redirects to `/login` on 401 responses. No Bearer tokens — cookies are sent automatically for same-origin requests. OIDC login/logout use full-page redirects to backend endpoints (not AJAX)
- **First-time setup**: When the database has zero users, `GET /api/status` returns `needsSetup: true`. The app redirects to `/setup` where the first user creates an admin account. After setup, `/setup` redirects to `/login`. Self-registration is disabled — the `POST /api/auth/register` endpoint returns 403 when users already exist
- **Auth pages**: Login (`shared/components/auth/login/`) and Setup (`shared/components/auth/setup/`) are standalone lazy-loaded components
- **API layer**: ng-openapi-gen generates functions and TypeScript models from the backend OpenAPI spec (`src/app/api/generated/`). Domain-scoped service wrappers in `src/app/api/services/` (e.g., `DocumentApiService`, `AdminApiService`) encapsulate `HttpClient`/`ApiConfiguration` and unwrap `StrictHttpResponse<T>` to `Observable<T>`. Components inject these services — never import generated functions or `HttpClient` directly for API calls. Barrel export from `@/api/services/index.ts`
- **Page Header**: The app shell renders a single unified header bar with sidebar trigger, page title, and optional action buttons. Page titles come from route `data: { title }` in each `*.routes.ts` file. Action buttons are projected via `PageHeaderService` (`shared/services/page-header.service.ts`) — pages with actions define an `<ng-template #headerActions>` and register it with `PageHeaderService.setActions()` in `afterNextRender`, clearing in `ngOnDestroy`. Actions are also auto-cleared on `NavigationStart`. Pages without actions need no service interaction — just set the route `data.title`.
- **Document List Search**: The document list uses URL query param `search` for full-text search, debounced at 300ms. The search term is maintained as a writable signal (`searchInput`) synced to the URL, with a separate `searchTerm` signal reading from query params for the API call pipeline.
- **Shared**: `src/app/shared/components/layout/` for app shell/sidebar, `src/app/shared/components/theme/` for dark mode, `src/app/shared/services/` for shared services
- **State**: Signals for local state, `computed()` for derived state, RxJS observables for async operations. State persistence uses three tiers:
  - **`localStorage`** via `persistedSignal()` (`shared/utils/persisted-signal.ts`): User preferences that survive across sessions (theme, view mode, column visibility, page size). `UserPreferencesService` (`shared/services/user-preferences.service.ts`) owns cross-cutting preferences
  - **`sessionStorage`**: Transient editing state that survives refresh but not session close (document edit drafts via `DocumentEditStateService`)
  - **In-memory signals**: Ephemeral state that resets on navigation (search filters, selections, sort, URL params via list state services)
- **Styling**: Tailwind CSS v4 with Spartan.NG OKLCH CSS variable theming (neutral palette)

- **Error handling**: Centralized via `ErrorHandlerService` (`shared/services/error-handler.service.ts`). The backend returns RFC 9457 Problem Details (`ApiProblemDetails` type in `shared/types/`). Key patterns:
  - **ErrorHandlerService**: `getErrorMessage(err, fallback)` extracts user-friendly message (prefers `detail`, then first validation error, then status-based fallback). Also: `getValidationErrors()`, `getErrorCode()`, `handleError()` (auto-toasts via ngx-sonner)
  - **Error interceptor** (`shared/interceptors/error.interceptor.ts`): Auto-toasts 5xx and network errors (status 0). 4xx errors pass through for component-level handling
  - **RxJS operators** (`shared/utils/rxjs-error.utils.ts`): `handleMutationError(errorHandler, loadingSignal, fallback)` — resets loading, toasts 4xx only (5xx handled by interceptor), completes stream. `handleQueryError(loadingSignal, errorSignal, fallbackValue)` — resets loading, sets error flag, emits fallback (no toast — renders error state in UI)
  - **Standalone utils** in `shared/utils/api-error.utils.ts` are **deprecated** — use `ErrorHandlerService` methods instead

Generated files (`src/app/api/generated/` and `src/app/shared/components/spartan/`) should not be manually edited.

---

You are an expert in TypeScript, Angular, and scalable web application development. You write functional, maintainable, performant, and accessible code following Angular and TypeScript best practices.

## TypeScript Best Practices

- Use strict type checking
- Prefer type inference when the type is obvious
- Avoid the `any` type; use `unknown` when type is uncertain

## Angular Best Practices

- Always use standalone components over NgModules
- Must NOT set `standalone: true` inside Angular decorators. It's the default in Angular v20+.
- Use signals for state management
- Implement lazy loading for feature routes
- Do NOT use the `@HostBinding` and `@HostListener` decorators. Put host bindings inside the `host` object of the `@Component` or `@Directive` decorator instead
- Use `NgOptimizedImage` for all static images.
  - `NgOptimizedImage` does not work for inline base64 images.

## Spartan.NG Patterns

- Spartan uses **brain** (headless primitives from `@spartan-ng/brain`) + **helm** (styled UI from generated `src/app/shared/components/spartan/`)
- Many Spartan UI elements are **directives** applied to native elements (e.g., `hlmBtn` on `<button>`, `hlmInput` on `<input>`, `hlmBadge` on `<span>`)
- Others are **components** (e.g., `<hlm-card>`, `<hlm-tabs>`, `<hlm-sidebar>`)
- Always import from the local `@/shared/components/spartan/` path aliases, not directly from `@spartan-ng/brain`
- **`hlm-checkbox` aria-label**: Use input binding `[aria-label]="expr"`, NOT attribute binding `[attr.aria-label]="expr"`. The host nullifies the attribute and forwards the input to the inner `brn-checkbox`. Same applies to `aria-labelledby` and `aria-describedby`

## Accessibility Requirements

- It MUST pass all AXE checks.
- It MUST follow all WCAG AA minimums, including focus management, color contrast, and ARIA attributes.

### Components

- Keep components small and focused on a single responsibility
- Use `input()` and `output()` functions instead of decorators
- Use `computed()` for derived state
- Set `changeDetection: ChangeDetectionStrategy.OnPush` in `@Component` decorator
- Prefer inline templates for small components
- Prefer Reactive forms instead of Template-driven ones
- Do NOT use `ngClass`, use `class` bindings instead
- Do NOT use `ngStyle`, use `style` bindings instead
- When using external templates/styles, use paths relative to the component TS file.

## State Management

- Use signals for local component state
- Use `computed()` for derived state
- Keep state transformations pure and predictable
- Do NOT use `mutate` on signals, use `update` or `set` instead
- Use `persistedSignal()` from `shared/utils/persisted-signal.ts` for state that must survive page refresh — never access `localStorage`/`sessionStorage` directly
- All storage keys must use the `homestead-` prefix
- Add new cross-cutting preferences to `UserPreferencesService` (`shared/services/user-preferences.service.ts`)
- **Effects that initialize edit state from API data**: Guard with an ID check so re-fetches of the same entity (e.g., triggered by `refreshTrigger` or SignalR) don't silently overwrite the user's pending edits. Reset the guard after a successful save so the post-save re-fetch reinitializes. See `document-detail-page.component.ts` for the pattern

## Templates

- Keep templates simple and avoid complex logic
- Use native control flow (`@if`, `@for`, `@switch`) instead of `*ngIf`, `*ngFor`, `*ngSwitch`
- Use the async pipe to handle observables
- Do not assume globals like (`new Date()`) are available.
- Do not write arrow functions in templates (they are not supported).

## Services

- Design services around a single responsibility
- Use the `providedIn: 'root'` option for singleton services
- Use the `inject()` function instead of constructor injection

## Theming & Styling

- Uses Spartan.NG default theme convention with OKLCH CSS variables defined in `src/styles.css`
- Semantic color tokens: `background`/`foreground`, `primary`/`primary-foreground`, `secondary`/`secondary-foreground`, `muted`/`muted-foreground`, `accent`/`accent-foreground`, `destructive`/`destructive-foreground`, `card`/`card-foreground`, `popover`/`popover-foreground`, `input`, `ring`, `border`, `sidebar`/`sidebar-foreground`/`sidebar-accent`/`sidebar-accent-foreground`/`sidebar-border`
- Dark mode via `.dark` class on `<html>` (toggled by `ThemeService`). `ThemeService` is eagerly instantiated in `App` root component so theming works on all routes including pre-auth pages
- Border radius system via `--radius` variable (`radius-sm`, `radius-md`, `radius-lg`, `radius-xl`)
- Font: Inter (self-hosted via `@fontsource/inter`)
- Use Tailwind classes like `bg-primary`, `text-foreground`, `text-muted-foreground`, `bg-muted`, `border-border`, etc.
- Do NOT use raw color values (e.g. `bg-gray-500`); always use semantic tokens
