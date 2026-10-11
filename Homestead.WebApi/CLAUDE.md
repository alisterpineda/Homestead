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

## Architecture: Vertical Slice + CQRS

The WebApi uses vertical slice architecture organized by module. There is no mediator: each endpoint injects its handler and calls it directly, and cross-cutting concerns live in endpoint filters and in the handler itself:

- **Modules/{Module}/Features/{SubDomain}/{Operation}/**: Each operation has `Endpoint.cs`, `Handler.cs` and `Response.cs`, plus a `Command.cs` or `Query.cs` record **only when ASP.NET binds it** (from the body, or via `[AsParameters]` from the query string), plus a body DTO (`*Request`, `*Body`, `*Model`) when the endpoint takes one
- **Modules** map to frontend areas: `Admin`, `Documents`, `Finances`, `Identity`, `Processing`, `Status`
- **Sub-domains** group related operations within a module (e.g., `Documents/Features/Tags/`, `Admin/Features/Groups/`)
- **Services** (`Modules/{Module}/Services/`): Module-scoped service interfaces and implementations shared across features (e.g., `ITagPathResolver`, `IAiClassificationService`)
- **Processing** (`Modules/{Module}/Processing/`, e.g. `Modules/Documents/Processing/`, `Modules/Finances/Processing/`): Background job handlers, payloads, and exceptions — not vertical slices
- **Naming convention**: A request record, where one exists, uses the `*Command` suffix for mutations (POST/PUT/PATCH/DELETE) and `*Query` for reads (GET). Records are plain records (no marker interface). `RequestRecordBindingTests` fails the build on a `*Command`/`*Query` under `Modules.*.Features.` that no endpoint delegate binds anywhere (a record that exists only to be built by hand); it cannot see a hand-built record whose type some other route binds, or one with another suffix, so those are caught only in review. `TransactionalHandlerSlowWorkTests` finds the handlers that run inside a command transaction by HTTP verb (plus those called from job handlers), not by name
- **Handlers** are plain classes named `*Handler` with a public `HandleAsync(<inputs>, CancellationToken ct)` method (returning `Task` when there is no result). The inputs are parameters: route values, query values and the bound body, e.g. `HandleAsync(Guid id, JsonPatchDocument<PatchTagModel> patchDoc, CancellationToken ct)`. `AddHandlers()` (`Common/HandlerServiceCollectionExtensions.cs`) registers every such class under `Modules.` (except `IJobHandler` implementations) as a scoped concrete type; `EndpointHandlerResolutionTests` fails on an endpoint whose handler the scan missed
- **Endpoints** are static classes with a `Map(RouteGroupBuilder)` method — no controllers. They inject the concrete handler (`PatchTagHandler handler`) and pass their delegate parameters straight through (`handler.HandleAsync(id, patchDoc, ct)`), so go-to-definition reaches it. `EndpointDispatchConventionTests` rejects mediator dispatch (`ISender`/`IMediator`) in any `*Endpoint.cs` or `*JobHandler.cs`
- **Modules** (e.g., `Modules/Documents/DocumentsModule.cs`) register versioned route groups on `IVersionedEndpointRouteBuilder`
- **API versioning** is URL-based: `/api/v{version:apiVersion}/documents`
- **Persistence**: EF Core with PostgreSQL. Migrations auto-run on startup in `Program.cs`
- **Primary keys**: UUIDv7, minted in one place — `UuidV7ValueGenerator` is registered for every Guid key in `AppDbContext.OnModelCreating`. Never set `Id = Guid.NewGuid()` on an entity; leave `Id` unset and read it after `db.Set.Add(entity)` (EF assigns it at `Add`, not `SaveChanges`). Link children via navigation properties rather than copying a parent id you minted yourself. `UuidV7PrimaryKeyTests` fails the build on any new `Guid.NewGuid()` in the WebApi outside its allowlist of non-key uses

### Project Structure

| Directory | Description |
|---|---|
| `Authentication/` | Auth setup, claims, OIDC options, current user accessor |
| `Authorization/` | Document-level auth handlers, policies, the `RequireDocumentAuthorization` endpoint filter |
| `Common/` | Cross-cutting concerns: exceptions (`Exceptions/`), the unit of work and `CommandTransactionFilter` (`.WithCommandTransaction()`), handler registration (`AddHandlers()`), audit infrastructure (`Auditing/`), pagination, query extensions |
| `Domain/` | Domain model layer: EF entities, enums, value objects, audit snapshots |
| `Modules/` | Modules with explicit `Features/`, `Services/`, and `Processing/` subdirectories |
| `Infrastructure/` | External integrations: `Ai/` (AI-provider chat clients), `TextExtraction/` (OCR engines), `Thumbnails/` (generation) |
| `Persistence/` | EF Core: `AppDbContext`, `DesignTimeDbContextFactory`, entity `Configurations/` |
| `Processing/` | Generic job queue framework, SignalR hub for real-time updates |

### Adding a New Endpoint

1. Create files under `Modules/{Module}/Features/{SubDomain}/{Operation}/`: `Endpoint.cs`, `Handler.cs`, `Response.cs`, plus a request record or body DTO only if step 2 calls for one
2. **A handler takes its inputs as parameters; a request record exists only when ASP.NET binds it.** A JSON body binds to a `*Command` record (`*Query` for a read sent as a POST, e.g. `GetTagsByIdsQuery`), or to a `*Request`/`*Body`/`*Model` DTO next to route values; a query string with several values binds via `[AsParameters]` to a `*Query` record, or to a parameters type next to route values (`[AsParameters] PaginatedRequest pagination`). Route values and single query values are plain delegate parameters. Never build a record in the endpoint just to pass values it already holds (`RequestRecordBindingTests` fails on a `*Command`/`*Query` that no endpoint binds; any other hand-built record is caught only in review). The one sanctioned `new XCommand(...)` in an endpoint is a single-id route reusing a bulk handler whose record the bulk route binds (`handler.HandleAsync(new BulkDeleteTagsCommand([id]), ct)`). Some older endpoints bind a `*Command` body and copy route ids into it (`body with { TransactionId = transactionId }`, e.g. `PatchFlowEndpoint`), so the handler and validator see the route id as a record property: that is a holdover awaiting collapse, not a pattern to copy. New code takes the route id as a handler parameter
3. The handler is a plain class named `*Handler` with a `public Task<TResponse> HandleAsync(<inputs>, CancellationToken ct)` method (`Task` for no result). Parameter order: route values in route-template order, then query values, then the body, then `ct`. Name each parameter like the endpoint delegate's (`id`, `documentId`, `request`, `body`, `patchDoc`). If a handler would take more than four values besides `ct`, keep a record instead and bind it with `[AsParameters]` (`[FromRoute]` / `[FromBody]` on its properties). `AddHandlers()` registers the handler; nothing else to wire
4. The endpoint injects the handler and passes its parameters through unchanged: `handler.HandleAsync(id, request, ct)`
5. **Validation goes in the handler.** A FluentValidation validator targets the body type the handler receives (the bound `*Command`, or the body DTO next to route values, e.g. `UpdatePartyRequestValidator : AbstractValidator<UpdatePartyRequest>`), never a route value: a route value is a handler parameter, so no validator sees it. A `{id:guid}` constraint checks the format only and accepts `Guid.Empty`; when collapsing a holdover whose validator rejects an empty route id (`RuleFor(x => x.TransactionId).NotEqual(Guid.Empty)`), keep that 400 as a guard in the handler unless dropping it is a deliberate, recorded decision. The handler injects `IValidator<TBody> validator` and calls `await validator.ValidateAndThrowAsync(body, ct);` as its first line, before any write; an optional body (`TBody? body`) is validated only when non-null. `ValidatorWiringTests` fails on a validator no handler injects (it would never run) and on a type with two validators (only the last registered would run). In handler unit tests, pass the real validator (`new UpdatePartyRequestValidator()`)
6. **Document authorization goes on the endpoint.** An endpoint whose route carries a document id adds `.RequireDocumentAuthorization(DocumentOperations.Read|Write, routeParameter: "id")` (default parameter: `documentId`, for sub-resources under the `{documentId}` group) next to its `.RequireAuthorization(...)` policy. Read verbs take `Read`, everything else `Write`. The filter fails closed (403) on a missing or unparseable id. `DocumentAuthorizationCoverageTests` fails on a documents route with `{id`/`{documentId` that carries no document authorization, outside its allowlist of bulk endpoints that authorize in the handler
7. **Mutations are transactional by HTTP verb.** Every module's top-level route group(s) call `.WithCommandTransaction()`, which runs any non-GET/HEAD/OPTIONS request inside one command transaction (see **Transactions & Unit of Work**). A new endpoint inside an existing group needs nothing; a new top-level group must add it. `CommandTransactionCoverageTests` fails on a mutating `/api/` endpoint outside such a group
8. Register the endpoint in the module class (e.g., `DocumentsModule.cs`)
9. Module-scoped services (interfaces + implementations shared across features) go in `Modules/{Module}/Services/`
10. Background job handlers go in `Modules/{Module}/Processing/`. They have no HTTP request, so no filter runs: a job handler that calls an internal command handler does its slow work first, then calls `unitOfWork.SetActor(job.CreatedByUserId)` and wraps the call in `unitOfWork.ExecuteInTransactionAsync(() => handler.HandleAsync(command, ct), ct)` itself (e.g. `ClassifyDocumentJobHandler`). Internal command handlers consumed by job handlers expose a small `I<Name>Handler` interface, registered in `Program.cs`, so job-handler tests can substitute them. A failure is terminal unless the job type opts into retries by overriding `IJobHandler.MaxAttempts` (and optionally `ShouldRetry`). Opt in only if the job handler's `IJobHandler.HandleAsync` is safe to re-run from the top. Mark a domain entity failed in `OnFinallyFailedAsync`, never in a catch block, which would run on every attempt. See `docs/product-design/processing/overview.md` › Retries.

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
- **`IAuditLogger`** — DI-injected service (`Common/Auditing/AuditLogger.cs`) — call `LogCreated`/`LogUpdated`/`LogDeleted` before `SaveChangesAsync`. Auto-resolves the current user from `ICurrentUserAccessor` — handlers do not pass `userId`. Background job handlers attribute audit entries to the user who initiated the job by calling `unitOfWork.SetActor(job.CreatedByUserId)` before the command transaction (see **Adding a New Endpoint** step 10); without it a job scope has no signed-in user and entries carry a null actor.
- **`IAuditContext`** — scoped service (`Common/Auditing/AuditContext.cs`) for batching audit entries within a request, flushed alongside persistence by `UnitOfWork.SaveChangesAsync` (driven by the command transaction)

## Transactions & Unit of Work

Every command runs inside a single database transaction owned by the `IUnitOfWork` chokepoint (`Common/UnitOfWork.cs`). Over HTTP, `CommandTransactionFilter` (`Common/CommandTransactionFilter.cs`) opens it: `.WithCommandTransaction()` adds the filter to each module's top-level route group, so it wraps every endpoint in the group and its nested groups. Job handlers have no HTTP request and call `unitOfWork.ExecuteInTransactionAsync` themselves (see **Adding a New Endpoint**). Authorization and validation throw before the first write, and the transaction opens lazily on the first write, so a rejected request never opens one.

- **A command = one transaction, committed exactly once** by `UnitOfWork.ExecuteInTransactionAsync`. **The HTTP verb is the gate:** any request other than GET/HEAD/OPTIONS runs inside the transaction, even a POST that only reads (free, because the open is lazy). GET/HEAD/OPTIONS run with no transaction.
- **GETs must not write.** Nothing enforces it, and a write under a read verb has no transaction: each explicit save (`unitOfWork.SaveChangesAsync`, a direct `db.SaveChangesAsync`, `ExecuteUpdate`/`ExecuteDelete`) autocommits on its own with no rollback; only `unitOfWork.SaveChangesAsync` flushes pending audit entries, so direct db writes lose theirs; tracked changes nobody saves are discarded, since there is no end-of-request flush; and `OnAfterCommit` hooks never run. The one known exception is `GET /api/auth/oidc/callback`, whose `OidcUserProvisioner` saves the provisioned user directly.
- **Nesting is safe.** A nested `ExecuteInTransactionAsync` inside an open command transaction joins it (`CommandTransactionCoordinator` tracks the depth); only the outermost frame commits and drains after-commit hooks.
- **The transaction opens lazily on the first flush that has work to persist** — not eagerly at the top of the handler. Slow pre-write external calls (AI, HTTP, file streaming) therefore hold **no** pooled connection: the connection is leased only once there is something to write. A no-op command (nothing tracked, no pending audit) opens no transaction at all.
- **`unitOfWork.SaveChangesAsync` flushes, it does not commit — but it is no longer a *pure* flush.** Handlers may call it mid-handler so a follow-up read/service sees their writes (read-your-writes), but nothing is durable until the transaction commits at the end of the request. Because the transaction is lazy, **a mid-handler `SaveChangesAsync` (or any change-tracker-bypassing write) is what opens the transaction and leases a connection** — so it is no longer free to call early. It is idempotent, so a mid-handler flush plus the final commit never double-write or double-audit. Any failure anywhere rolls the whole command back.
- **Side effects that must outlive or observe the commit must go through `unitOfWork.OnAfterCommit`, never inline.** This includes SignalR/`IProcessingNotifier` sends, file deletes, and **job-processor signals** (the processor reads committed rows from a separate connection, so signalling before commit would lose the wakeup until the 30s poll). After-commit hooks are drained outside the transaction once the commit succeeds. `JobQueue.EnqueueAsync` already defers its signal this way when inside a command transaction.
- **Do slow external calls (AI, HTTP, file streaming) *before* the first write, or defer them — this is now load-bearing for the fix's correctness, not just a nicety.** Lazy-tx *relocates* the open to the first flush; it does **not** *enforce* ordering. A handler that flushes and *then* does slow work re-pins the connection for that work's duration, reintroducing the idle-in-transaction hold the lazy mechanism exists to prevent. `Modules/Documents/Processing/UploadDocumentJobHandler` → `CreateDocumentFromUploadHandler` and `ReprocessVersionTextCommand` are the canonical *slow-work-then-thin-persist* shape to copy.
- **Change-tracker-bypassing writes (`ExecuteUpdateAsync`/`ExecuteDeleteAsync`, or a direct `db.SaveChangesAsync`) are made atomic with the command transaction *automatically* — no manual call, no ceremony.** A handler can bulk-write freely and stay atomic with the command and its rollback *by construction*. The coverage is two EF interceptors (`Common/CommandTransactionInterceptor.cs`) that open the command transaction through the shared scoped `CommandTransactionCoordinator` (`Common/CommandTransactionCoordinator.cs`) before the write reaches the database: `CommandSaveChangesInterceptor` (`SavingChanges`) covers a direct `db.SaveChangesAsync`; `CommandWriteCommandInterceptor` (`NonQueryExecuting`) covers `ExecuteUpdate`/`ExecuteDelete` and re-binds the command onto the owned transaction. The interceptor is **scope-gated**: outside a command scope (queries, the `JobProcessorService` background scopes, the scheduler's own scope) it is a no-op, so those writes keep autocommitting exactly as before. The interceptors are scoped services attached per request by the **non-pooled** `AppDbContext` registration in `Program.cs` (its `(IServiceProvider, options)` callback resolves them from the request scope). **`AppDbContext` is registered non-pooled on purpose:** a scoped interceptor — whose `CommandTransactionCoordinator` holds per-request transaction state — cannot be attached to a *pooled* context, whose `DbContextOptions` are a singleton (it would be resolved once from the root provider, silently no-op every request, and crash Development scope-validation at startup). `EnrichNpgsqlDbContext` keeps Aspire's health checks / telemetry / retry on the self-registered context. (`JobQueue.EnqueueAsync`, `DeleteGroupHandler`, and `DeleteProcessingScheduleHandler` rely on this; none calls anything manual.) Queries are deliberately uncovered — we don't hook `ReaderExecuting`/`ScalarExecuting`, so ordinary `SELECT`s stay read-only and connection-light.
- EF execution-strategy **retries are disabled** (`Program.cs`, `DisableRetry = true`) so an explicit `BeginTransactionAsync` is legal without an execution-strategy re-run. Don't open your own `BeginTransactionAsync`/`CreateExecutionStrategy` inside a command handler — it would nest illegally; rely on the command transaction (opened lazily, or by the interceptor for bulk writes) instead. With retries off, the real defense against idle-timeout aborts is **not holding the connection during slow work** — achieved here by construction (lazy open + slow-work-before-write).

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
- Endpoints call handlers directly (no mediator); handlers auto-registered via the `AddHandlers()` convention scan
