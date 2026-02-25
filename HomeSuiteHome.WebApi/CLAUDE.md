# HomeSuiteHome.WebApi

## Commands

```bash
# Build the solution
dotnet build

# Run WebApi directly (requires PostgreSQL already running)
cd HomeSuiteHome.WebApi && dotnet run

# Add an EF Core migration
dotnet ef migrations add <MigrationName> --project HomeSuiteHome.WebApi
```

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

## Conventions

- Minimal APIs (no controllers)
- Scalar for API docs (at `/scalar/v1` in dev)
- EF Core with PostgreSQL; migrations auto-run on startup
- Handler registration via assembly scanning (`AddHandlers()`)
