# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

HomeSuiteHome is a full-stack document management application with a .NET 10 backend API and Angular v21 frontend. It uses .NET Aspire for local orchestration of the WebApi and PostgreSQL.

## Commands

### Backend (.NET)

```bash
# Run the full stack (WebApi + PostgreSQL via Aspire)
cd HomeSuiteHome.AppHost && dotnet run

# Build the solution
dotnet build

# Run WebApi directly (requires PostgreSQL already running)
cd HomeSuiteHome.WebApi && dotnet run

# Add an EF Core migration
dotnet ef migrations add <MigrationName> --project HomeSuiteHome.WebApi
```

### Frontend (Angular)

All frontend commands run from `HomeSuiteHome.WebApp/`:

```bash
npm install          # Install dependencies
npm start            # Dev server on port 4200 with API proxy
npm run build        # Production build
npm test             # Run tests (Vitest)
npm run generate-api # Regenerate API clients from openapi.json via ng-openapi-gen
```

## Architecture

### Backend: Vertical Slice + CQRS

The WebApi uses vertical slice architecture with CQRS handlers:

- **Features/{Domain}/v{n}/{Operation}/**: Each operation has four files: `Endpoint.cs`, `Handler.cs`, `Request.cs`, `Response.cs`
- **Handlers** implement `ICommandHandler<TReq, TRes>` or `IQueryHandler<TReq, TRes>` (defined in `Common/`) and are auto-registered via assembly scanning in `ServiceCollectionExtensions.AddHandlers()`
- **Modules** (e.g., `DocumentsModule.cs`) register versioned route groups on `IVersionedEndpointRouteBuilder`
- **Endpoints** are static classes with a `Map(RouteGroupBuilder)` method — no controllers
- **API versioning** is URL-based: `/api/v{version:apiVersion}/documents`
- **Persistence**: EF Core with PostgreSQL. Migrations auto-run on startup in `Program.cs`

To add a new endpoint: create the four files under the appropriate `Features/{Domain}/v1/{Operation}/` folder, then register it in the domain's module class.

### Frontend: Angular Standalone Components

- **Modules** (`src/app/modules/`): Self-contained feature areas (documents, admin) each with pages, components, routes, and sidebar config. Registered in `modules/registry.ts`
- **Routing**: Angular Router with lazy-loaded feature routes defined in `app.routes.ts`. Each module has its own `*.routes.ts` file
- **API layer**: ng-openapi-gen generates Angular services and TypeScript models from the backend OpenAPI spec. Config in `ng-openapi-gen.json`, output in `src/app/api/generated/`
- **Shared**: `src/app/shared/components/ui/` for base components, `src/app/shared/components/layout/` for app shell/sidebar, `src/app/shared/components/theme/` for dark mode, `src/app/shared/services/` for shared services
- **State**: Signals for local state, `computed()` for derived state, RxJS observables for async operations
- **Styling**: Tailwind CSS v4 with shadcn/ui-style CSS variable theming (OKLCH colors)

See `HomeSuiteHome.WebApp/CLAUDE.md` for detailed Angular conventions and best practices.

### Aspire Orchestration

`HomeSuiteHome.AppHost` orchestrates PostgreSQL (with pgWeb admin UI), the WebApi, and the Angular dev server. The WebApi connects to PostgreSQL via the `postgresdb` connection name. The Angular frontend is added via `AddJavaScriptApp` (from `Aspire.Hosting.JavaScript`) with an HTTP endpoint on port 4200. The Angular dev server proxies API requests to the WebApi via `proxy.conf.js`. `HomeSuiteHome.ServiceDefaults` provides shared config for OpenTelemetry, health checks, and HTTP resilience.

## Syncing Frontend with Backend API Changes

The OpenAPI spec is generated at build time via `Microsoft.Extensions.ApiDescription.Server` — no running backend needed.

When backend endpoints are added or modified, regenerate the frontend API client:

1. **Build the WebApi** to generate the OpenAPI spec:
   ```bash
   dotnet build HomeSuiteHome.WebApi
   ```
   This outputs the spec to `HomeSuiteHome.WebApi/obj/HomeSuiteHome.WebApi.json`.
2. **Regenerate the API client** (from `HomeSuiteHome.WebApp/`):
   ```bash
   npm run generate-api
   ```
   This runs ng-openapi-gen, which reads the generated spec and outputs Angular services and TypeScript models to `src/app/api/generated/`.
3. **Update frontend code** to use the new/modified generated services and models.

## Key Conventions

- Backend uses Minimal APIs (no controllers), Scalar for API docs (at `/scalar/v1` in dev)
- Frontend uses standalone components (default in Angular v21), signals for state, OnPush change detection
- Generated files (`src/app/api/generated/`) should not be manually edited
