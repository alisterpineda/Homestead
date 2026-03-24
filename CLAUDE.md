# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Homestead is a full-stack document management application with a .NET 10 backend API and Angular v21 frontend. It uses .NET Aspire for local orchestration of the WebApi, PostgreSQL, and Keycloak. Authentication is handled by ASP.NET Core Identity with cookie-based auth, with optional OIDC for external providers (Keycloak, Authentik, Auth0).

## Project Structure

| Directory | Description |
|---|---|
| `Homestead.AppHost/` | .NET Aspire orchestrator — runs the full stack (includes Keycloak for local OIDC testing) |
| `Homestead.WebApi/` | .NET 10 backend API ([CLAUDE.md](Homestead.WebApi/CLAUDE.md)) |
| `Homestead.WebApp/` | Angular v21 frontend with Spartan.NG ([CLAUDE.md](Homestead.WebApp/CLAUDE.md)) |
| `Homestead.ServiceDefaults/` | Shared Aspire config (OpenTelemetry, health checks, HTTP resilience) |

## Running the Full Stack

```bash
cd Homestead.AppHost && dotnet run
```

This starts PostgreSQL (with pgWeb admin UI), Keycloak (with pre-imported realm), the WebApi, and the Angular dev server via Aspire. OIDC config is automatically passed to the WebApi.

## Aspire Orchestration

`Homestead.AppHost` orchestrates all services: PostgreSQL, Keycloak, WebApi, and the Angular dev server. The WebApi connects to PostgreSQL via the `postgresdb` connection name. Keycloak runs on port 8080 with a pre-imported `homestead` realm (`KeycloakRealms/homestead-realm.json`) containing test users (`admin@test.com`/`admin123`, `user@test.com`/`user123`). The Angular frontend is added via `AddJavaScriptApp` (from `Aspire.Hosting.JavaScript`) with an HTTP endpoint on port 4200. The Angular dev server proxies API requests to the WebApi via `proxy.conf.js`.

## Syncing Frontend with Backend API Changes

The OpenAPI spec is generated at build time via `Microsoft.Extensions.ApiDescription.Server` — no running backend needed.

When backend endpoints are added or modified, regenerate the frontend API client:

1. **Build the WebApi** to generate the OpenAPI spec:
   ```bash
   dotnet build Homestead.WebApi
   ```
   This outputs the spec to `Homestead.WebApi/obj/Homestead.WebApi.json`.
2. **Regenerate the API client** (from `Homestead.WebApp/`):
   ```bash
   npm run generate-api
   ```
   This runs ng-openapi-gen, which reads the generated spec and outputs Angular services and TypeScript models to `src/app/api/generated/`.
3. **Update frontend code** to use the new/modified generated services and models.

## Test Projects

| Project | Framework | Description |
|---|---|---|
| `Homestead.WebApi.Tests.Unit/` | xUnit v3 + NSubstitute + EF InMemory | Backend unit tests |
| `Homestead.WebApi.Tests.Integration/` | xUnit v3 + WebApplicationFactory + Testcontainers (PostgreSQL) + Respawn | Backend integration tests (requires Docker) |
| `Homestead.WebApp/` (ng test) | Angular unit-test builder (Vitest) | Frontend unit tests (`*.spec.ts` files) |
| `Homestead.E2E/` | Playwright + Testcontainers | End-to-end tests — auth flows + business workflows (requires Docker) |

### Running Tests

**Backend unit tests:**
```bash
dotnet test --project Homestead.WebApi.Tests.Unit/Homestead.WebApi.Tests.Unit.csproj
```

**Backend integration tests** (requires Docker for Testcontainers):
```bash
dotnet test --project Homestead.WebApi.Tests.Integration/Homestead.WebApi.Tests.Integration.csproj
```

**Frontend unit tests:**
```bash
cd Homestead.WebApp && npm test
```

**E2E tests** (requires Docker — spins up full stack via Testcontainers):
```bash
cd Homestead.E2E && npm run e2e
```

**E2E business workflow tests only** (faster — skips OIDC suites):
```bash
cd Homestead.E2E && npx playwright test --project=local-upload --project=local-tags --project=local-bulk-ops --project=local-trash
```
