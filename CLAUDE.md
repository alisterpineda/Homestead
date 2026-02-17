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

## Key Conventions

- Backend uses Minimal APIs (no controllers), Scalar for API docs (at `/scalar/v1` in dev)
- Frontend build output goes to `HomeSuiteHome.WebApi/wwwroot` and is served as static files with SPA fallback
- The Vite dev server proxies `/api` and `/openapi` requests to the backend
- Orval reads a local `openapi.json` file in the WebApp directory — update this file when API changes are made, then run `npm run generate-api`
