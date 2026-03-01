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

Uses ASP.NET Core Identity with cookie-based auth:

- `AppUser` extends `IdentityUser<Guid>` with custom `DisplayName` and `CreatedAt` properties
- `AppDbContext` extends `IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>`
- Identity API endpoints mapped at `/api/auth` via `MapIdentityApi<AppUser>()`
- Custom endpoints: `/api/auth/custom-register` (accepts displayName), `/api/auth/logout`, `/api/auth/user-info`
- Cookie configured with `HttpOnly`, `SameSite=Strict`, 14-day sliding expiration
- SPA-friendly: returns 401/403 status codes instead of redirects
- Protected endpoints use explicit `.RequireAuthorization()` (on modules, hub, and custom auth endpoints)
- `ICurrentUserAccessor.UserId` reads `ClaimTypes.NameIdentifier` from the cookie claims
- All Identity tables are in the `identity` schema (configured in `Persistence/Configurations/IdentitySchemaConfiguration.cs`)

## Architecture: Vertical Slice + CQRS

The WebApi uses vertical slice architecture with CQRS handlers:

- **Features/{Domain}/v{n}/{Operation}/**: Each operation has four files: `Endpoint.cs`, `Handler.cs`, `Request.cs`, `Response.cs`
- **Handlers** implement `ICommandHandler<TReq, TRes>` or `IQueryHandler<TReq, TRes>` (defined in `Common/`) and are auto-registered via assembly scanning in `ServiceCollectionExtensions.AddHandlers()`
- **Modules** (e.g., `DocumentsModule.cs`) register versioned route groups on `IVersionedEndpointRouteBuilder`
- **Endpoints** are static classes with a `Map(RouteGroupBuilder)` method — no controllers
- **API versioning** is URL-based: `/api/v{version:apiVersion}/documents`
- **Persistence**: EF Core with PostgreSQL. Migrations auto-run on startup in `Program.cs`

### Adding a New Endpoint

1. Create four files under `Features/{Domain}/v1/{Operation}/`: `Endpoint.cs`, `Handler.cs`, `Request.cs`, `Response.cs`
2. Register the endpoint in the domain's module class (e.g., `DocumentsModule.cs`)

## Full-Text Search

Document search uses PostgreSQL FTS with a stored `SearchVector` (`tsvector`) column on the `documents` table, indexed with GIN.

- **Weights**: `Document.Name` = A, `DocumentVersion.ExtractedText` (latest version) = B
- **`ISearchVectorUpdater.UpdateSearchVectorAsync()`** — DI-injected service (`Persistence/SearchVectorUpdater.cs`) that must be called after any mutation that changes a document's name or its latest version's extracted text
- **Mutation points**: `UploadDocumentJobHandler`, `UploadDocumentVersionJobHandler`, `PatchDocumentHandler`, `PatchDocumentVersionHandler`, `DeleteDocumentVersionHandler`
- Uses `WebSearchToTsQuery` for user-friendly query parsing and `ts_rank_cd` for relevance ordering

## Audit Logging

Field-level change tracking in `auditing.audit_logs` (JSONB). No FK constraints — survives entity deletion.

- **Infrastructure** in `Domains/Auditing/`: `AuditLog`, `AuditAction` enum, polymorphic `FieldChange` (`ScalarFieldChange`/`ArrayFieldChange`), `IAuditSnapshot<TSelf, TSource>` interface
- **Snapshots** per entity (e.g., `DocumentSnapshot`) implement `IAuditSnapshot` with `From()` and `Diff()`
- **`IAuditLogger`** — DI-injected service (`Persistence/AuditLogger.cs`) — call `LogCreated`/`LogUpdated`/`LogDeleted` before `SaveChangesAsync`. Same pattern as `ISearchVectorUpdater`.

## Conventions

- Minimal APIs (no controllers)
- Scalar for API docs (at `/scalar/v1` in dev)
- EF Core with PostgreSQL; migrations auto-run on startup
- Handler registration via assembly scanning (`AddHandlers()`)
