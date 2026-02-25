# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

HomeSuiteHome is a full-stack document management application with a .NET 10 backend API and Angular v21 frontend. It uses .NET Aspire for local orchestration of the WebApi and PostgreSQL.

## Project Structure

| Directory | Description |
|---|---|
| `HomeSuiteHome.AppHost/` | .NET Aspire orchestrator — runs the full stack |
| `HomeSuiteHome.WebApi/` | .NET 10 backend API ([CLAUDE.md](HomeSuiteHome.WebApi/CLAUDE.md)) |
| `HomeSuiteHome.WebApp/` | Angular v21 frontend ([CLAUDE.md](HomeSuiteHome.WebApp/CLAUDE.md)) |
| `HomeSuiteHome.ServiceDefaults/` | Shared Aspire config (OpenTelemetry, health checks, HTTP resilience) |

## Running the Full Stack

```bash
cd HomeSuiteHome.AppHost && dotnet run
```

This starts PostgreSQL (with pgWeb admin UI), the WebApi, and the Angular dev server via Aspire.

## Aspire Orchestration

`HomeSuiteHome.AppHost` orchestrates all services. The WebApi connects to PostgreSQL via the `postgresdb` connection name. The Angular frontend is added via `AddJavaScriptApp` (from `Aspire.Hosting.JavaScript`) with an HTTP endpoint on port 4200. The Angular dev server proxies API requests to the WebApi via `proxy.conf.js`.

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
