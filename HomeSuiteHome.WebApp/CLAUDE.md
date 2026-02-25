# HomeSuiteHome.WebApp

## Commands

All commands run from `HomeSuiteHome.WebApp/`:

```bash
npm install          # Install dependencies
npm start            # Dev server on port 4200 with API proxy
npm run build        # Production build
npm test             # Run tests (Vitest)
npm run generate-api # Regenerate API clients from openapi.json via ng-openapi-gen
```

## Architecture

- **Modules** (`src/app/modules/`): Self-contained feature areas (documents, admin) each with pages, components, routes, and sidebar config. Registered in `modules/registry.ts`
- **Routing**: Angular Router with lazy-loaded feature routes defined in `app.routes.ts`. Each module has its own `*.routes.ts` file
- **API layer**: ng-openapi-gen generates Angular services and TypeScript models from the backend OpenAPI spec. Config in `ng-openapi-gen.json`, output in `src/app/api/generated/`
- **Shared**: `src/app/shared/components/ui/` for base components, `src/app/shared/components/layout/` for app shell/sidebar, `src/app/shared/components/theme/` for dark mode, `src/app/shared/services/` for shared services
- **State**: Signals for local state, `computed()` for derived state, RxJS observables for async operations
- **Styling**: Tailwind CSS v4 with shadcn/ui-style CSS variable theming (OKLCH colors)

Generated files (`src/app/api/generated/`) should not be manually edited.

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

- Uses shadcn/ui default theme convention with OKLCH CSS variables defined in `src/styles.css`
- Semantic color tokens: `background`/`foreground`, `primary`/`primary-foreground`, `secondary`/`secondary-foreground`, `muted`/`muted-foreground`, `accent`/`accent-foreground`, `destructive`/`destructive-foreground`, `card`/`card-foreground`, `popover`/`popover-foreground`, `input`, `ring`, `border`, `sidebar`/`sidebar-foreground`/`sidebar-accent`/`sidebar-accent-foreground`/`sidebar-border`
- Dark mode via `.dark` class on `<html>` (toggled by `ThemeService`)
- Border radius system via `--radius` variable (`radius-sm`, `radius-md`, `radius-lg`, `radius-xl`)
- Font: Inter (self-hosted via `@fontsource/inter`)
- Use Tailwind classes like `bg-primary`, `text-foreground`, `text-muted-foreground`, `bg-muted`, `border-border`, etc.
- Do NOT use raw color values (e.g. `bg-gray-500`); always use semantic tokens
