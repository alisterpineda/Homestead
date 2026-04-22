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
dotnet test --project Homestead.WebApi.Tests.Unit
```

**Backend integration tests** (requires Docker for Testcontainers):
```bash
dotnet test --project Homestead.WebApi.Tests.Integration
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

### Backend Test Filtering (xUnit v3 + MTP v2)

Backend tests use xUnit v3 with Microsoft Testing Platform v2 (opted in via `global.json`). This means the old VSTest `--filter "FullyQualifiedName~Foo"` syntax does **not** work. Use the xUnit-native filter switches instead.

**Simple filters** — include/exclude by class, method, namespace, or trait:

```bash
# Run a single test class (wildcard supported at start/end)
dotnet test --project Homestead.WebApi.Tests.Unit --filter-class "*JobQueueTests"

# Run a single test method
dotnet test --project Homestead.WebApi.Tests.Unit --filter-method "*EnqueueAsync_PersistsJobAndSignals"

# Run all tests in a namespace
dotnet test --project Homestead.WebApi.Tests.Unit --filter-namespace "Homestead.WebApi.Tests.Unit.Processing"

# Run multiple classes (OR logic — space-separated)
dotnet test --project Homestead.WebApi.Tests.Unit --filter-class "*JobQueueTests" "*JobProcessorServiceTests"

# Exclude a class
dotnet test --project Homestead.WebApi.Tests.Unit --filter-not-class "*JobQueueTests"

# Combine include + exclude (run namespace but skip one class)
dotnet test --project Homestead.WebApi.Tests.Unit \
  --filter-namespace "Homestead.WebApi.Tests.Unit.Processing" \
  --filter-not-class "*JobQueueTests"

# Filter by trait
dotnet test --project Homestead.WebApi.Tests.Unit --filter-trait "Category=Unit"
```

**Query filter language** — structured filter using `/assembly/namespace/type/method[trait=value]` (4 segments):

```bash
# Run a test class (type is the 3rd segment)
dotnet test --project Homestead.WebApi.Tests.Unit --filter-query "///JobQueueTests"

# Run a single method (method is the 4th segment)
dotnet test --project Homestead.WebApi.Tests.Unit --filter-query "////EnqueueAsync_PersistsJobAndSignals"

# Run all tests in a namespace (namespace is the 2nd segment)
dotnet test --project Homestead.WebApi.Tests.Unit --filter-query "//Homestead.WebApi.Tests.Unit.Processing//"
```

Simple filters and query filters **cannot be mixed** in the same invocation.

**Useful flags:**

```bash
# List matching tests without running them (verify your filter)
dotnet test --project Homestead.WebApi.Tests.Unit --list-tests --filter-class "*JobQueueTests"

# Stop on first failure
dotnet test --project Homestead.WebApi.Tests.Unit --stop-on-fail on

# Skip rebuild
dotnet test --project Homestead.WebApi.Tests.Unit --no-build
```

### Running Slow Tests (Integration + E2E)

**Do NOT pipe integration or E2E test output through `tail`, `head`, or other truncation commands.** These tests are slow to run. When they fail, the full output contains error messages, stack traces, and context needed to diagnose the issue. Truncating with `| tail -50` etc. loses this context and forces a costly re-run. Instead, use filters to narrow the scope of what runs:

- Use `--filter-class`, `--filter-method`, or `--filter-namespace` to run fewer tests
- Use `--stop-on-fail on` to stop at the first failure
- For Playwright E2E tests, use `--project=<name>` or pass a test file path to narrow scope

This does **not** apply to fast tests (backend unit tests, Angular unit tests) where truncation is fine.
