# Homestead.WebApi

## Commands

```bash
# Build the solution
dotnet build

# Run WebApi directly (requires PostgreSQL already running)
cd Homestead.WebApi && dotnet run

# Add an EF Core migration
dotnet ef migrations add <MigrationName> --project Homestead.WebApi
```

## Authentication

OIDC is the only sign-in method. ASP.NET Core Identity has been removed — `AppUser` is a plain EF entity and cookie auth is registered directly via `AddCookie()`.

- `AppUser` is a plain POCO with `Id`, `Email`, `DisplayName`, `Role` (AppRole enum), `IsActive`, `CreatedAt`
- `AppDbContext` extends `DbContext` (no Identity base) and implements `IDataProtectionKeyContext`
- Auth endpoints at `/api/auth`: `/user-info`, `/oidc/login`, `/oidc/callback`, `/oidc/logout`
- `GET /api/status` (anonymous) returns `{ displayName }` — used by the frontend to label the SSO button
- Main app cookie `.Homestead.Auth` configured with `HttpOnly`, `SameSite=Lax` (dev) / `Strict` (prod), 14-day sliding expiration
- SPA-friendly: returns 401/403 status codes instead of redirects
- Protected endpoints use explicit `.RequireAuthorization()` (on modules, hub, and custom auth endpoints); policies use a single `ClaimTypes.Role` claim populated from `AppUser.Role`
- `ICurrentUserAccessor.UserId` reads `ClaimTypes.NameIdentifier`; `.Role` reads `ClaimTypes.Role` — both flow from the cookie claims set at sign-in
- `ActiveUserHandler` reads the `IsActive` claim (set at sign-in) with no DB fallback
- User data lives in the default schema — no `identity` schema

### OIDC (Required)

OIDC is a startup requirement — the app throws at `IHostedService` start-up (`OidcStartupValidator`) if `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, and `OIDC_CLIENT_SECRET` are not all set. The validator skips itself when the entry assembly is `GetDocument.Insider` (build-time OpenAPI generation).

- Config in `Authentication/OidcOptions.cs` — bound from `Oidc:*` config section. `IsConfigured` is true when Authority, ClientId, and ClientSecret are all set
- Docker env vars: `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_DISPLAY_NAME`, `OIDC_ROLE_CLAIM`, `OIDC_ADMIN_CLAIM_VALUES` (comma-separated), `OIDC_ADDITIONAL_SCOPES` — mapped in `Program.cs`
- Endpoints registered in `IdentityModule`: `/api/auth/oidc/login`, `/api/auth/oidc/callback`, `/api/auth/oidc/logout`
- `OidcUserProvisioner` auto-provisions users from OIDC claims, matches by email, and syncs the Admin role from a configurable claim — OIDC is the source of truth (claim value matches any `AdminClaimValues` entry → Role=Admin; no match → demoted to Contributor)
- The OIDC handler writes to a short-lived `ExternalOidc` bridge cookie; the callback reads it, provisions the user, then signs in to the main app cookie scheme

## Architecture: Vertical Slice + CQRS (MediatR)

The WebApi uses vertical slice architecture organized by module, with MediatR for CQRS handler dispatch:

- **Modules/{Module}/Features/{SubDomain}/{Operation}/**: Each operation has up to four files: `Endpoint.cs`, `Handler.cs`, `Command.cs` or `Query.cs`, `Response.cs`
- **Modules** map to frontend areas: `Admin`, `Documents`, `Identity`, `Processing`, `Status`
- **Sub-domains** group related operations within a module (e.g., `Documents/Features/Tags/`, `Admin/Features/Groups/`)
- **Services** (`Modules/{Module}/Services/`): Module-scoped service interfaces and implementations shared across features (e.g., `ISearchVectorUpdater`, `ITagPathResolver`)
- **Processing** (`Modules/Documents/Processing/`): Background job handlers, payloads, and exceptions — not vertical slices
- **Naming convention**: Use `*Command` suffix for mutations (POST/PUT/PATCH/DELETE) and `*Query` suffix for reads (GET). Both implement `IRequest<TResponse>` from MediatR
- **Handlers** implement `IRequestHandler<TRequest, TResponse>` from MediatR and are auto-registered via `AddMediatR()` assembly scanning in `Program.cs`
- **Endpoints** are static classes with a `Map(RouteGroupBuilder)` method — no controllers. They inject `ISender` from MediatR and dispatch requests via `sender.Send(request, ct)`
- **Modules** (e.g., `Modules/Documents/DocumentsModule.cs`) register versioned route groups on `IVersionedEndpointRouteBuilder`
- **API versioning** is URL-based: `/api/v{version:apiVersion}/documents`
- **Persistence**: EF Core with PostgreSQL. Migrations auto-run on startup in `Program.cs`

### Project Structure

| Directory | Description |
|---|---|
| `Authentication/` | Auth setup, claims, OIDC options, current user accessor |
| `Authorization/` | Document-level auth handlers, policies, MediatR auth behavior |
| `Common/` | Cross-cutting concerns: exceptions (`Exceptions/`), MediatR behaviors (`Behaviors/`), audit infrastructure (`Auditing/`), pagination, query extensions |
| `Domain/` | Domain model layer: EF entities, enums, value objects, audit snapshots |
| `Modules/` | Modules with explicit `Features/`, `Services/`, and `Processing/` subdirectories |
| `Infrastructure/` | External integrations: `TextExtraction/` (OCR engines), `Thumbnails/` (generation) |
| `Persistence/` | EF Core: `AppDbContext`, `DesignTimeDbContextFactory`, entity `Configurations/` |
| `Processing/` | Generic job queue framework, SignalR hub for real-time updates |

### Adding a New Endpoint

1. Create files under `Modules/{Module}/Features/{SubDomain}/{Operation}/`: `Endpoint.cs`, `Handler.cs`, `Command.cs` or `Query.cs`, `Response.cs`
2. The command/query record should implement `IRequest<TResponse>` from MediatR. Use `*Command` for mutations, `*Query` for reads
3. The handler class should implement `IRequestHandler<TRequest, TResponse>` from MediatR with a `Handle` method
4. The endpoint should inject `ISender` and call `sender.Send(request, ct)`
5. Register the endpoint in the module class (e.g., `DocumentsModule.cs`)
6. Module-scoped services (interfaces + implementations shared across features) go in `Modules/{Module}/Services/`
7. Background job handlers go in `Modules/{Module}/Processing/`

## Full-Text Search

Document search uses PostgreSQL FTS with a stored `SearchVector` (`tsvector`) column on the `documents` table, indexed with GIN.

- **Weights**: `Document.Name` = A, `DocumentVersion.ExtractedText` (latest version) = B
- **`ISearchVectorUpdater.UpdateSearchVectorAsync()`** — DI-injected service (`Modules/Documents/Services/SearchVectorUpdater.cs`) that must be called after any mutation that changes a document's name or its latest version's extracted text
- **Mutation points**: `UploadDocumentJobHandler`, `UploadDocumentVersionJobHandler`, `PatchDocumentHandler`, `PatchDocumentVersionHandler`, `DeleteDocumentVersionHandler`
- Uses `WebSearchToTsQuery` for user-friendly query parsing and `ts_rank_cd` for relevance ordering

## Audit Logging

Field-level change tracking in `auditing.audit_logs` (JSONB). No FK constraints — survives entity deletion.

- **Domain types** in `Domain/Auditing/`: `AuditLog`, `AuditAction` enum, polymorphic `FieldChange` (`ScalarFieldChange`/`ArrayFieldChange`), `IAuditSnapshot<TSelf, TSource>` interface
- **Snapshots** per entity (e.g., `DocumentSnapshot`) implement `IAuditSnapshot` with `From()` and `Diff()`
- **`IAuditLogger`** — DI-injected service (`Common/Auditing/AuditLogger.cs`) — call `LogCreated`/`LogUpdated`/`LogDeleted` before `SaveChangesAsync`. Auto-resolves the current user from `ICurrentUserAccessor` — handlers do not pass `userId`. Background job handlers use `auditLogger.ForUser(job.CreatedByUserId)` to attribute audit entries to the user who initiated the job.
- **`IAuditContext`** — scoped service (`Common/Auditing/AuditContext.cs`) for batching audit entries within a request, flushed by `UnitOfWorkBehavior`

## Error Handling (RFC 9457)

All error responses use RFC 9457 Problem Details (`application/problem+json`). A single `DomainExceptionHandler` maps domain exceptions to responses via a switch expression — no per-exception handler classes.

### Exception Hierarchy (HTTP-decoupled)

| Exception | Maps to | Use when |
|---|---|---|
| `BusinessRuleException` | 400 | Business logic violation. Supports optional `Errors` (structured validation errors) |
| `EntityNotFoundException` | 404 | Resource not found |
| `AccessDeniedException` | 403 | Insufficient permissions |
| `DuplicateEntityException` | 409 | Uniqueness constraint violation |
| `ValidationException` (FluentValidation) | 400 | Request validation failure |
| `IdentityValidationException` | 400 | ASP.NET Identity operation failure |

Exceptions have **no knowledge of HTTP status codes** — the mapping lives exclusively in `DomainExceptionHandler` and `ProblemDetailsFactory`.

### ProblemDetailsFactory

Static factory (`Common/Exceptions/DomainExceptionHandler.cs`) for constructing `ProblemDetails` responses. Used by the exception handler, auth middleware, and endpoints directly:
- `Create(statusCode, title, detail?)` — generic
- `BusinessRule(detail)` / `BusinessRuleResult(detail)` — 400 with detail
- `Forbidden(detail, errorCode?)` / `Unauthorized(detail, errorCode)` — with optional `errorCode` extension
- `ValidationErrors(errors, detail?)` — 400 with field-level errors in `Extensions["errors"]`

The `errorCode` extension enables frontend-specific handling (e.g., `account_inactive`, `account_locked`) without parsing message strings.

## Conventions

- Minimal APIs (no controllers)
- Scalar for API docs (at `/scalar/v1` in dev)
- EF Core with PostgreSQL; migrations auto-run on startup
- MediatR for CQRS handler dispatch; handlers auto-registered via `AddMediatR()` assembly scanning
