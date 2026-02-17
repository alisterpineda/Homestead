# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

HomeSuiteHome is a full-stack document management application with a .NET 10 backend API and React 19 frontend. It uses .NET Aspire for local orchestration of the WebApi and PostgreSQL.

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

### Frontend (React)

All frontend commands run from `HomeSuiteHome.WebApp/`:

```bash
npm install          # Install dependencies
npm run dev          # Dev server with proxy to https://localhost:7248
npm run build        # Build to HomeSuiteHome.WebApi/wwwroot
npm run lint         # ESLint
npm run format       # Prettier
npm run generate-api # Regenerate API clients from openapi.json via Orval
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

### Frontend: Module-Based with TanStack

- **Modules** (`src/modules/`): Self-contained feature areas (documents, admin) each with pages, components, and sidebar config. Registered in `modules/registry.ts`
- **Routing**: TanStack Router with file-based routes in `src/routes/`. Route tree is auto-generated (`routeTree.gen.ts`)
- **API layer**: Orval generates React Query hooks and TypeScript types from the backend OpenAPI spec. Config in `orval.config.ts`, output in `src/api/generated/`. Uses a custom fetch client (`src/api/client.ts`)
- **Components**: `src/components/ui/` for base components, `src/components/layout/` for app shell/sidebar/header, `src/components/theme/` for dark mode
- **Path alias**: `@` maps to `src/`

### Aspire Orchestration

`HomeSuiteHome.AppHost` orchestrates PostgreSQL (with pgWeb admin UI) and the WebApi. The WebApi connects to PostgreSQL via the `postgresdb` connection name. `HomeSuiteHome.ServiceDefaults` provides shared config for OpenTelemetry, health checks, and HTTP resilience.

## Syncing Frontend with Backend API Changes

When backend endpoints are added or modified, the frontend API client must be regenerated:

1. **Run the backend** so the OpenAPI spec is available (via Aspire or `dotnet run` in WebApi)
2. **Download the updated OpenAPI spec** from the running backend:
   ```bash
   curl -k https://localhost:7248/openapi/v1.json -o HomeSuiteHome.WebApp/openapi.json
   ```
3. **Regenerate the API client** (from `HomeSuiteHome.WebApp/`):
   ```bash
   npm run generate-api
   ```
   This runs Orval, which reads `openapi.json` and outputs generated React Query hooks and TypeScript types to `src/api/generated/`.
4. **Update frontend code** to use the new/modified generated hooks and types.

### Building the Frontend for Production

The frontend must be rebuilt after changes for the WebApi to serve the updated app:

```bash
cd HomeSuiteHome.WebApp && npm run build
```

This compiles TypeScript and outputs to `HomeSuiteHome.WebApi/wwwroot/`, which the backend serves as static files with SPA fallback.

During development, use `npm run dev` instead — the Vite dev server proxies `/api` and `/openapi` requests to the backend at `https://localhost:7248`.

## Key Conventions

- Backend uses Minimal APIs (no controllers), Scalar for API docs (at `/scalar/v1` in dev)
- Frontend build output goes to `HomeSuiteHome.WebApi/wwwroot` and is served as static files with SPA fallback
- Generated files (`src/api/generated/`, `routeTree.gen.ts`) should not be manually edited
