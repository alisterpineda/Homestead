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
- Docker env vars: `OIDC_AUTHORITY`, `OIDC_METADATA_ADDRESS`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_DISPLAY_NAME`, `OIDC_ROLE_CLAIM`, `OIDC_ADMIN_CLAIM_VALUES` (comma-separated), `OIDC_ADDITIONAL_SCOPES` — mapped in `Program.cs` (full table in the root `README.md`)
- Endpoints registered in `IdentityModule`: `/api/auth/oidc/login`, `/api/auth/oidc/callback`, `/api/auth/oidc/logout`
- `OidcUserProvisioner` auto-provisions users from OIDC claims, matches by email, and syncs the Admin role from a configurable claim — OIDC is the source of truth (claim value matches any `AdminClaimValues` entry → Role=Admin; no match → demoted to Contributor)
- The OIDC handler writes to a short-lived `ExternalOidc` bridge cookie; the callback reads it, provisions the user, then signs in to the main app cookie scheme

## Architecture: Vertical Slice + CQRS (MediatR)

The WebApi uses vertical slice architecture organized by module, with MediatR for CQRS handler dispatch:

- **Modules/{Module}/Features/{SubDomain}/{Operation}/**: Each operation has up to four files: `Endpoint.cs`, `Handler.cs`, `Command.cs` or `Query.cs`, `Response.cs`
- **Modules** map to frontend areas: `Admin`, `Documents`, `Finances`, `Identity`, `Processing`, `Status`
- **Sub-domains** group related operations within a module (e.g., `Documents/Features/Tags/`, `Admin/Features/Groups/`)
- **Services** (`Modules/{Module}/Services/`): Module-scoped service interfaces and implementations shared across features (e.g., `ITagPathResolver`, `IAiClassificationService`)
- **Processing** (`Modules/{Module}/Processing/`, e.g. `Modules/Documents/Processing/`, `Modules/Finances/Processing/`): Background job handlers, payloads, and exceptions — not vertical slices
- **Naming convention**: Use `*Command` suffix for mutations (POST/PUT/PATCH/DELETE) and `*Query` suffix for reads (GET). Both implement `IRequest<TResponse>` from MediatR
- **Handlers** implement `IRequestHandler<TRequest, TResponse>` from MediatR and are auto-registered via `AddMediatR()` assembly scanning in `Program.cs`
- **Endpoints** are static classes with a `Map(RouteGroupBuilder)` method — no controllers. They inject `ISender` from MediatR and dispatch requests via `sender.Send(request, ct)`
- **Modules** (e.g., `Modules/Documents/DocumentsModule.cs`) register versioned route groups on `IVersionedEndpointRouteBuilder`
- **API versioning** is URL-based: `/api/v{version:apiVersion}/documents`
- **Persistence**: EF Core with PostgreSQL. Migrations auto-run on startup in `Program.cs`
- **Primary keys**: UUIDv7, minted in one place — `UuidV7ValueGenerator` is registered for every Guid key in `AppDbContext.OnModelCreating`. Never set `Id = Guid.NewGuid()` on an entity; leave `Id` unset and read it after `db.Set.Add(entity)` (EF assigns it at `Add`, not `SaveChanges`). Link children via navigation properties rather than copying a parent id you minted yourself. `UuidV7PrimaryKeyTests` fails the build on any new `Guid.NewGuid()` in the WebApi outside its allowlist of non-key uses

### Project Structure

| Directory | Description |
|---|---|
| `Authentication/` | Auth setup, claims, OIDC options, current user accessor |
| `Authorization/` | Document-level auth handlers, policies, MediatR auth behavior |
| `Common/` | Cross-cutting concerns: exceptions (`Exceptions/`), MediatR behaviors (`Behaviors/`), audit infrastructure (`Auditing/`), pagination, query extensions |
| `Domain/` | Domain model layer: EF entities, enums, value objects, audit snapshots |
| `Modules/` | Modules with explicit `Features/`, `Services/`, and `Processing/` subdirectories |
| `Infrastructure/` | External integrations: `Ai/` (AI-provider chat clients), `TextExtraction/` (OCR engines), `Thumbnails/` (generation) |
| `Persistence/` | EF Core: `AppDbContext`, `DesignTimeDbContextFactory`, entity `Configurations/` |
| `Processing/` | Generic job queue framework, SignalR hub for real-time updates |

### Adding a New Endpoint

1. Create files under `Modules/{Module}/Features/{SubDomain}/{Operation}/`: `Endpoint.cs`, `Handler.cs`, `Command.cs` or `Query.cs`, `Response.cs`
2. The command/query record should implement `IRequest<TResponse>` from MediatR. Use `*Command` for mutations, `*Query` for reads
3. The handler class should implement `IRequestHandler<TRequest, TResponse>` from MediatR with a `Handle` method
4. The endpoint should inject `ISender` and call `sender.Send(request, ct)`
5. Register the endpoint in the module class (e.g., `DocumentsModule.cs`)
6. Module-scoped services (interfaces + implementations shared across features) go in `Modules/{Module}/Services/`
7. Background job handlers go in `Modules/{Module}/Processing/`. A failure is terminal unless the job type opts into retries by overriding `IJobHandler.MaxAttempts` (and optionally `ShouldRetry`). Opt in only if `HandleAsync` is safe to re-run from the top. Mark a domain entity failed in `OnFinallyFailedAsync`, never in a catch block, which would run on every attempt. See `docs/product-design/processing/overview.md` › Retries.

## Full-Text Search

Document search uses PostgreSQL FTS with a stored `SearchVector` (`tsvector`) column on the `documents` table, indexed with GIN.

- **Weights**: `Document.Name` = A, `DocumentVersion.ExtractedText` (latest version) = B, `Document.Summary` = C
- **DB-owned**: `SearchVector` is maintained entirely by database triggers (migration `AddSearchVectorTriggers`) — there is no app-side updater. A `BEFORE` trigger on `documents` recomputes the vector when `Name`/`Summary` change; an `AFTER` trigger on `document_versions` recomputes the parent document's vector when the latest version's `ExtractedText` changes or a version is added/removed. Because the vector spans two tables (latest version's text lives on `document_versions`), a `GENERATED ALWAYS AS … STORED` column is not sufficient — hence triggers.
- Uses `WebSearchToTsQuery` for user-friendly query parsing and `ts_rank_cd` for relevance ordering

## AI Integration

AI features (document classify / summarize, statement parsing) use **Microsoft.Extensions.AI 10.10.1** (`Microsoft.Extensions.AI.Abstractions` + `.OpenAI` + `Azure.AI.OpenAI`) through **named provider instances** defined in static config under `Ai:Providers:{key}` — types `OpenAiCompatible` (Ollama/OpenWebUI/OpenAI) and `AzureFoundry` (Azure OpenAI in an AI Foundry resource; `Model` = deployment name). Everything lives in `Infrastructure/Ai/`: `AiProviderOptions` (one bag per instance, nullable `Type` required for every key), `AiProviderConfiguration` (read + per-type validation, error-string posture, no `ValidateOnStart`), `AiProvider` (lazy build-once `IChatClient`), registered as `IEnumerable<IAiProvider>` singletons. Consumers resolve one via `IAiProviderResolver` — chain: explicit key → `Ai:DefaultProvider` → sole defined provider → null (= AI unconfigured, graceful no-op). A missing/misconfigured key resolves to unavailable-with-reason, **never silently substituting** another provider. Per-call knobs (`MaxInputChars`, `StatementMaxInputChars`, `ImageInput`, `ImageRenderMaxEdge`) live on the provider. The classification flow is `Modules/Documents/Services/AiClassificationService.cs`.

- **Strict structured-output gotcha:** M.E.AI only emits OpenAI's `strict: true` — which makes the JSON schema *enforced* rather than advisory — when `ChatOptions.AdditionalProperties["strict"] = true` is set; `ChatResponseFormat.ForJsonSchema(...)` **alone leaves it unset**. Both are set together in `AiClassificationService.GetResponseWithFallbackAsync`; keep them in sync if you change the response format.
- **Degradation ladder:** `GetResponseWithFallbackAsync` retries an HTTP 400 up to 3 attempts, dropping a custom `Temperature` and/or structured output as `DiagnoseUnsupportedOption` implicates them — so a model that rejects strict `json_schema` or a non-default temperature still succeeds with reduced options. A 400 that implicates neither is rethrown unchanged.
- **Signal:** the `"Retrying with reduced options"` warning in `aspire logs homestead-webapi` means the model rejected the strict schema or temperature and the ladder kicked in; its absence means both were accepted on the first attempt.

## Text extraction engines

Engines are **named config instances** under `Ocr:Engines:{key}` — `OcrEngineOptions` (one bag per instance) and `OcrEngineConfiguration` (read + per-type validation, error-string posture, no `ValidateOnStart`), registered as `IEnumerable<ITextExtractionEngine>` singletons in `Infrastructure/TextExtraction/`; the built-in `tesseract` instance is always seeded. **Which engine to use is also config:** `Ocr:DefaultEngine`, resolved by the singleton `ITextExtractionEngineResolver` (same directory) — chain: explicit per-run key → `Ocr:DefaultEngine` → `tesseract`. A dangling default is returned **verbatim** by the resolver and substituted only by `TextExtractionHelper`, which records the fallback in the version's provenance; an explicit pick is never substituted (unknown/unavailable → `BusinessRuleException` at enqueue). The Documents settings page only *reports* engines and the default. Price sources follow the same rule: `PriceSources:{Source}:*` (enablement + credentials) is read into per-provider options bags, and `IPriceSourceRegistry` (scoped, over the keyed `IPriceProvider`s) is the single definition of enabled/configured for valuation and imports.

## Audit Logging

Field-level change tracking in `auditing.audit_logs` (JSONB). No FK constraints — survives entity deletion.

- **Domain types** in `Domain/Auditing/`: `AuditLog`, `AuditAction` enum, polymorphic `FieldChange` (`ScalarFieldChange`/`ArrayFieldChange`), `IAuditSnapshot<TSelf, TSource>` interface
- **Snapshots** per entity (e.g., `DocumentSnapshot`) implement `IAuditSnapshot` with `From()` and `Diff()`
- **`IAuditLogger`** — DI-injected service (`Common/Auditing/AuditLogger.cs`) — call `LogCreated`/`LogUpdated`/`LogDeleted` before `SaveChangesAsync`. Auto-resolves the current user from `ICurrentUserAccessor` — handlers do not pass `userId`. Background job handlers use `auditLogger.ForUser(job.CreatedByUserId)` to attribute audit entries to the user who initiated the job.
- **`IAuditContext`** — scoped service (`Common/Auditing/AuditContext.cs`) for batching audit entries within a request, flushed alongside persistence by `UnitOfWork.SaveChangesAsync` (driven by `UnitOfWorkBehavior`)

## Transactions & Unit of Work

Every command runs inside a single database transaction owned by the `IUnitOfWork` chokepoint (`Common/UnitOfWork.cs`). `UnitOfWorkBehavior` is registered **innermost** in the MediatR pipeline (outer→inner: Authorization → Validation → UnitOfWork → handler), so authorization and validation run *outside* the transaction and a rejected request never opens one.

- **A command = one transaction, committed exactly once** by `UnitOfWork.ExecuteInTransactionAsync`. The `ICommand` marker is the gate: queries are not `ICommand`, run with no transaction, and are guaranteed read-only.
- **The transaction opens lazily on the first flush that has work to persist** — not eagerly at the top of the handler. Slow pre-write external calls (AI, HTTP, file streaming) therefore hold **no** pooled connection: the connection is leased only once there is something to write. A no-op command (nothing tracked, no pending audit) opens no transaction at all.
- **`unitOfWork.SaveChangesAsync` flushes, it does not commit — but it is no longer a *pure* flush.** Handlers may call it mid-handler so a follow-up read/service sees their writes (read-your-writes), but nothing is durable until the transaction commits at the end of the pipeline. Because the transaction is lazy, **a mid-handler `SaveChangesAsync` (or any change-tracker-bypassing write) is what opens the transaction and leases a connection** — so it is no longer free to call early. It is idempotent, so a mid-handler flush plus the final commit never double-write or double-audit. Any failure anywhere rolls the whole command back.
- **Side effects that must outlive or observe the commit must go through `unitOfWork.OnAfterCommit`, never inline.** This includes SignalR/`IProcessingNotifier` sends, file deletes, and **job-processor signals** (the processor reads committed rows from a separate connection, so signalling before commit would lose the wakeup until the 30s poll). After-commit hooks are drained outside the transaction once the commit succeeds. `JobQueue.EnqueueAsync` already defers its signal this way when inside a command transaction.
- **Do slow external calls (AI, HTTP, file streaming) *before* the first write, or defer them — this is now load-bearing for the fix's correctness, not just a nicety.** Lazy-tx *relocates* the open to the first flush; it does **not** *enforce* ordering. A handler that flushes and *then* does slow work re-pins the connection for that work's duration, reintroducing the idle-in-transaction hold the lazy mechanism exists to prevent. `Modules/Documents/Processing/UploadDocumentJobHandler` → `CreateDocumentFromUploadHandler` and `ReprocessVersionTextCommand` are the canonical *slow-work-then-thin-persist* shape to copy.
- **Change-tracker-bypassing writes (`ExecuteUpdateAsync`/`ExecuteDeleteAsync`, or a direct `db.SaveChangesAsync`) are made atomic with the command transaction *automatically* — no manual call, no ceremony.** A handler can bulk-write freely and stay atomic with the command and its rollback *by construction*. The coverage is two EF interceptors (`Common/CommandTransactionInterceptor.cs`) that open the command transaction through the shared scoped `CommandTransactionCoordinator` (`Common/CommandTransactionCoordinator.cs`) before the write reaches the database: `CommandSaveChangesInterceptor` (`SavingChanges`) covers a direct `db.SaveChangesAsync`; `CommandWriteCommandInterceptor` (`NonQueryExecuting`) covers `ExecuteUpdate`/`ExecuteDelete` and re-binds the command onto the owned transaction. The interceptor is **scope-gated**: outside a command scope (queries, the `JobProcessorService` background scopes, the scheduler's own scope) it is a no-op, so those writes keep autocommitting exactly as before. The interceptors are scoped services attached per request by the **non-pooled** `AppDbContext` registration in `Program.cs` (its `(IServiceProvider, options)` callback resolves them from the request scope). **`AppDbContext` is registered non-pooled on purpose:** a scoped interceptor — whose `CommandTransactionCoordinator` holds per-request transaction state — cannot be attached to a *pooled* context, whose `DbContextOptions` are a singleton (it would be resolved once from the root provider, silently no-op every request, and crash Development scope-validation at startup). `EnrichNpgsqlDbContext` keeps Aspire's health checks / telemetry / retry on the self-registered context. (`JobQueue.EnqueueAsync`, `DeleteGroupHandler`, and `DeleteProcessingScheduleHandler` rely on this; none calls anything manual.) Queries are deliberately uncovered — we don't hook `ReaderExecuting`/`ScalarExecuting`, so ordinary `SELECT`s stay read-only and connection-light.
- EF execution-strategy **retries are disabled** (`Program.cs`, `DisableRetry = true`) so an explicit `BeginTransactionAsync` is legal without an execution-strategy re-run. Don't open your own `BeginTransactionAsync`/`CreateExecutionStrategy` inside a command handler — it would nest illegally; rely on the pipeline transaction (opened lazily, or by the interceptor for bulk writes) instead. With retries off, the real defense against idle-timeout aborts is **not holding the connection during slow work** — achieved here by construction (lazy open + slow-work-before-write).

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
