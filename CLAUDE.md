# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Homestead is a full-stack household-management application — document management plus personal-finance / net-worth tracking — with a .NET 10 backend API and Angular v22 frontend. It uses .NET Aspire for local orchestration of the WebApi, PostgreSQL, and Keycloak. OIDC is the only sign-in method — ASP.NET Core Identity has been removed; `AppUser` is a plain EF entity and authentication is cookie-based via `AddCookie()` with the OIDC handler bridging external sign-in. Any OIDC-compliant IdP works (Keycloak is used locally; Authentik / Auth0 / etc. work in production).

## Project Structure

| Directory | Description |
|---|---|
| `Homestead.AppHost/` | .NET Aspire orchestrator — runs the full stack (includes Keycloak for local OIDC testing) |
| `Homestead.WebApi/` | .NET 10 backend API ([CLAUDE.md](Homestead.WebApi/CLAUDE.md)) |
| `Homestead.WebApp/` | Angular v22 frontend with Spartan.NG ([CLAUDE.md](Homestead.WebApp/CLAUDE.md)) |
| `Homestead.ServiceDefaults/` | Shared Aspire config (OpenTelemetry, health checks, HTTP resilience) |

## Running the Full Stack

```bash
aspire run   # from repo root; auto-detects the AppHost
```

This starts PostgreSQL (with pgWeb admin UI), Keycloak (with pre-imported realm), the WebApi, and the Angular dev server via Aspire. OIDC config is automatically passed to the WebApi. Backend hot reload is on by default: the committed `aspire.config.json` sets `features.defaultWatchEnabled=true`, so `aspire run` (and `aspire start`) watch the AppHost and its referenced projects (the WebApi) — most C# edits are hot-reloaded into the running WebApi in place (no restart), and edits watch can't patch (e.g. constructor/signature changes) restart just the WebApi instead. Either way there's no manual stop/re-run, and the Postgres/Keycloak containers and their data volumes stay up. The Angular dev server already live-reloads on its own. The AppHost runs in CLI-bundle mode (`AspireUseCliBundle=true`): DCP and the dashboard come from the installed Aspire CLI's bundle (`~/.aspire/bundle`) rather than NuGet packages, so the CLI must be installed — the devcontainer's Aspire feature already does that. In bundle mode a plain `dotnet run` delegates to `aspire run --no-build`, which the CLI rejects while `defaultWatchEnabled` is on, so to start the stack **without** watch use `cd Homestead.AppHost && ASPIRE_SUPPRESS_CLI_RUN_HOOK=1 dotnet run` (the same bypass the CLI sets on its own launches); that runs the AppHost in-process with the bundle's DCP and dashboard.

> Watch mode is best-effort: most edits hot-reload, but some still need an explicit `aspire resource homestead-webapi restart` (or `rebuild`). Editing `AppHost.cs` restarts the whole app.

## Aspire Orchestration

`Homestead.AppHost` orchestrates all services: PostgreSQL, Keycloak, WebApi, and the Angular dev server. The WebApi connects to PostgreSQL via the `postgresdb` connection name. Keycloak runs on port 8080 with a pre-imported `homestead` realm (`KeycloakRealms/homestead-realm.json`) containing test users (`admin@test.com`/`admin123`, `user@test.com`/`user123`). The realm also holds `homestead-mcp`, the confidential OAuth client for the optional MCP server (secret `homestead-mcp-dev-secret`, 60 s access tokens, an audience mapper putting `homestead-mcp` in `aud`), and AppHost forwards `Mcp__Authority` (the realm's issuer, built like `OIDC_AUTHORITY`) and `Mcp__Audience=homestead-mcp`, so `/mcp` is on locally; `Mcp__PublicUrl` is deliberately unset (Development infers it from the request), and turning MCP off locally means editing that `WithEnvironment` block. Keycloak's `--import-realm` skips a realm that already exists in its data volume (and `aspire resource keycloak …` does not re-import), so a realm-file change needs a volume wipe: stop the stack, `docker volume rm $(docker volume ls -q | grep keycloak-data)`, re-run — Homestead's own Postgres volume is untouched. The same realm folder is bind-mounted into every E2E/CI Keycloak, so a realm change also needs the E2E `setup` + `auth` projects green. Claude Code connects with `claude mcp add --transport http … --client-id homestead-mcp --client-secret --callback-port 8765` — its OAuth callback port must not be 8080, which Keycloak owns, and 8765 is the one the realm's redirect URIs register. The Angular frontend is added via `AddJavaScriptApp` (from `Aspire.Hosting.JavaScript`) with an HTTP endpoint on port 4200. The Angular dev server proxies API requests to the WebApi via `proxy.conf.js`. AI is optional and off by default — it runs on the Claude API, configured via AppHost user-secrets (`Ai:ApiKey` + `Ai:Model`, optionally `Ai:Effort` / `Ai:RefusalFallback` and the input knobs), which AppHost forwards to the WebApi verbatim as stock `Ai__*` env vars (the same mechanism as `Ocr:*` and `PriceSources:*`). The backup mirror's *targets* follow the OCR engines' named-config-instance shape (`Backup:Targets:{name}:Kind/Path`); AppHost hard-codes one `local` target at `.homestead-data/backup` so the feature is exercisable under `aspire run`. A target path that overlaps `FileStorage:MediaPath`/`CachePath` is rejected as unavailable-with-reason. What actually gets mirrored where is *not* config: an admin pairs a code-registered source kind with a target at `/admin/backup`, creating a `backup.sources` row that owns enablement, schedule, and the delete-breaker latch. Per-kind engine knobs (batch caps, breaker thresholds) live under that kind's own section — `Backup:Documents:*`.

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
| `Homestead.E2E/` | Playwright + Testcontainers | End-to-end tests — auth flows, business workflows, roles, accessibility and layout, on desktop Chrome + Android Chrome + iPhone WebKit (requires Docker). Conventions in its [CLAUDE.md](Homestead.E2E/CLAUDE.md) |
| `Homestead.WebApi.Tests.Eval/` | xUnit v3 + Microsoft.Extensions.AI.Evaluation (LLM-judge) | AI eval harness — scores doc-classification title/summary. Opt-in, **not** in the default test gate. `report.html` is regenerated manually via `aieval report` and only after an eval run has populated the store. See its [README](Homestead.WebApi.Tests.Eval/README.md). |

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

**E2E, narrowed** (one module, or one device project):
```bash
cd Homestead.E2E && npx playwright test tests/documents                # one module, every device
cd Homestead.E2E && npx playwright test --project=desktop              # one device project
```

### Backend Test Filtering (xUnit v3 + MTP v2)

Backend tests use xUnit v3 (the `xunit.v3.mtp-v2` 4.x packages) with Microsoft Testing Platform v2 (opted in via `global.json`). xUnit 4 also accepts the old VSTest `--filter "FullyQualifiedName~Foo"` syntax, but prefer the xUnit-native filter switches below. A VSTest `--filter` can't be combined with any other filter switch.

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
- For Playwright E2E tests, pass a test directory/file path or `--project=<desktop|android|iphone|auth>` to narrow scope

This does **not** apply to fast tests (backend unit tests, Angular unit tests) where truncation is fine.
