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

Uses ASP.NET Core Identity with cookie-based auth, plus optional OIDC:

- `AppUser` extends `IdentityUser<Guid>` with custom `DisplayName` and `CreatedAt` properties
- `AppDbContext` extends `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`
- Identity API endpoints mapped at `/api/auth` via `MapIdentityApi<AppUser>()`
- Custom endpoints: `/api/auth/register` (setup only — returns 403 when users exist), `/api/auth/logout`, `/api/auth/user-info`
- `GET /api/status` (anonymous) returns `{ needsSetup, oidc? }` — used by frontend to decide setup vs login flow and whether to show SSO button
- Cookie configured with `HttpOnly`, `SameSite=Lax` (dev) / `Strict` (prod), 14-day sliding expiration
- SPA-friendly: returns 401/403 status codes instead of redirects
- Protected endpoints use explicit `.RequireAuthorization()` (on modules, hub, and custom auth endpoints)
- `ICurrentUserAccessor.UserId` reads `ClaimTypes.NameIdentifier` from the cookie claims
- All Identity tables are in the `identity` schema (configured in `Persistence/Configurations/IdentitySchemaConfiguration.cs`)

### OIDC (Optional)

Opt-in external authentication via any OpenID Connect provider. When OIDC config is absent, the app behaves exactly as before.

- Config in `Authentication/OidcOptions.cs` — bound from `Oidc:*` config section. `IsConfigured` property gates all OIDC behavior
- Docker env vars: `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_DISPLAY_NAME`, `OIDC_ROLE_CLAIM`, `OIDC_ADMIN_CLAIM_VALUE`, `OIDC_DISABLE_LOCAL_LOGIN`, `OIDC_ADDITIONAL_SCOPES` — mapped in `Program.cs`
- Endpoints (conditionally registered in `IdentityModule`): `/api/auth/oidc/login`, `/api/auth/oidc/callback`, `/api/auth/oidc/logout`
- `OidcUserProvisioner` auto-provisions users from OIDC claims, links by email, syncs Admin role from a configurable claim
- Uses `IdentityConstants.ExternalScheme` for the OIDC sign-in, then signs into the application cookie in the callback

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
