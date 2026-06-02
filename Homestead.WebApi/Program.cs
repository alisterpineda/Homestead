using Asp.Versioning;
using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Logging;
using Homestead.WebApi.Common.Settings;
using Homestead.WebApi.Modules.Admin;
using Homestead.WebApi.Modules.Identity;
using Homestead.WebApi.Modules.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Modules.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Modules.Processing;
using Homestead.WebApi.Modules.Status;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Processing.Hubs;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Download;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Infrastructure.Thumbnails;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Reloadable in-memory source for runtime log-level overrides. The provider is both the
// IConfigurationSource and the ConfigurationProvider (Build(_) => this), so the same instance is added to
// the config builder AND registered in DI — the apply path pushes to the very instance the config root
// reads. Added LAST so it wins over appsettings/env; MEL rebinds LoggerFilterOptions when its reload token
// fires, so a save takes effect with no restart.
var runtimeLogLevels = new RuntimeLogLevelProvider();
((IConfigurationBuilder)builder.Configuration).Add(runtimeLogLevels);
builder.Services.AddSingleton(runtimeLogLevels);

// Read-only inspector over the live config + logger factory: reports the effective level for any namespace
// and where it comes from (override / appsettings / env / code), for the admin "effective log levels" view.
builder.Services.AddSingleton<ILogLevelInspector, LogLevelInspector>();

// Multi-instance fan-out for settings changes: the writer NOTIFYs on a Postgres channel and every
// instance (including the writer — Postgres echoes a channel's NOTIFY back to its own listeners) re-reads
// the committed row and re-applies, so a save propagates with no restart. Postgres is the only shared
// dependency the repo runs, so it doubles as the signal. Today this only covers logging; see
// SettingsChangeListener for when to graduate to a real bus.
builder.Services.AddSingleton<ISettingsChangeNotifier, PgNotifySettingsChangeNotifier>();
builder.Services.AddHostedService<SettingsChangeListener>();

// When running outside Aspire (e.g. Docker), map self-hoster-friendly env vars into .NET config
if (string.IsNullOrEmpty(builder.Configuration.GetConnectionString("postgresdb")))
{
    var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
    if (!string.IsNullOrEmpty(dbPassword))
    {
        var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "homestead";
        var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";

        builder.Configuration["ConnectionStrings:postgresdb"] =
            $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";
    }
}

// When running outside Aspire (e.g. Docker), map self-hoster-friendly OIDC env vars into .NET config
var oidcAuthority = Environment.GetEnvironmentVariable("OIDC_AUTHORITY");
if (!string.IsNullOrEmpty(oidcAuthority))
{
    builder.Configuration["Oidc:Authority"] = oidcAuthority;
    var oidcMetadata = Environment.GetEnvironmentVariable("OIDC_METADATA_ADDRESS");
    if (!string.IsNullOrEmpty(oidcMetadata))
        builder.Configuration["Oidc:MetadataAddress"] = oidcMetadata;
    builder.Configuration["Oidc:ClientId"] =
        Environment.GetEnvironmentVariable("OIDC_CLIENT_ID") ?? "";
    builder.Configuration["Oidc:ClientSecret"] =
        Environment.GetEnvironmentVariable("OIDC_CLIENT_SECRET") ?? "";
    builder.Configuration["Oidc:DisplayName"] =
        Environment.GetEnvironmentVariable("OIDC_DISPLAY_NAME") ?? "SSO";
    builder.Configuration["Oidc:RoleClaimType"] =
        Environment.GetEnvironmentVariable("OIDC_ROLE_CLAIM") ?? "";

    MapCsvEnvToConfigArray("OIDC_ADMIN_CLAIM_VALUES", "Oidc:AdminClaimValues");
    MapCsvEnvToConfigArray("OIDC_ADDITIONAL_SCOPES", "Oidc:AdditionalScopes");
}

void MapCsvEnvToConfigArray(string envVar, string configPrefix)
{
    var raw = Environment.GetEnvironmentVariable(envVar);
    if (string.IsNullOrEmpty(raw))
        return;
    var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (var i = 0; i < values.Length; i++)
        builder.Configuration[$"{configPrefix}:{i}"] = values[i];
}

// AppDbContext is registered NON-POOLED on purpose. The command-transaction interceptors are scoped
// (their CommandTransactionCoordinator holds per-request transaction state), and a scoped interceptor
// cannot be attached to a pooled context: pooling builds the DbContextOptions once as a singleton, so the
// interceptors would be resolved a single time from the root provider — a no-op on every request (the
// interceptor's coordinator is never the request's coordinator) and a scope-validation crash in
// Development. A non-pooled context runs the (IServiceProvider, options) callback per request, so each
// request gets its own interceptors bound to its own coordinator. EnrichNpgsqlDbContext below keeps
// Aspire's health checks, telemetry, and retry configuration.
var postgresConnectionString = builder.Configuration.GetConnectionString("postgresdb");
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(postgresConnectionString);
    options.AddInterceptors(
        sp.GetRequiredService<CommandSaveChangesInterceptor>(),
        sp.GetRequiredService<CommandWriteCommandInterceptor>());
    options
        .UseSeeding((ctx, created) =>
        {
            CurrencySeeder.Seed(ctx, created);
            SystemAccountSeeder.Seed(ctx, created);
            CommodityPriceSourceSettingsSeeder.Seed(ctx, created);
        })
        .UseAsyncSeeding(async (ctx, created, ct) =>
        {
            await CurrencySeeder.SeedAsync(ctx, created, ct);
            await SystemAccountSeeder.SeedAsync(ctx, created, ct);
            await CommodityPriceSourceSettingsSeeder.SeedAsync(ctx, created, ct);
        });
});
// Disable EF execution-strategy retries: the command pipeline wraps each command in an explicit
// transaction (UnitOfWork.ExecuteInTransactionAsync), and BeginTransactionAsync is only legal without a
// retrying execution strategy. Enrich (not Add) so Aspire augments the registration above instead of
// registering its own pooled context.
builder.EnrichNpgsqlDbContext<AppDbContext>(configureSettings: settings => settings.DisableRetry = true);

// Serialize enums as strings in JSON responses and OpenAPI spec,
// without interfering with query parameter binding (which uses Enum.TryParse).
// NumberHandling.Strict makes numeric fields appear as `type: number` (not `[number, string]`)
// in the generated OpenAPI spec — the generator reflects whatever the HTTP JSON pipeline
// will accept, and the framework default (AllowReadingFromString) widens the schema.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Add services to the container.

builder.AddAppAuthentication();
builder.Services.AddSignalR();
builder.Services.AddScoped<IProcessingNotifier, ProcessingNotifier>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IAuditContext, AuditContext>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
// The scoped state machine + EF interceptors that make change-tracker-bypassing writes
// (ExecuteUpdate/ExecuteDelete/direct db.SaveChangesAsync) atomic with the command transaction by
// construction. The interceptors are attached per request by the non-pooled AddDbContext registration
// above (its (sp, options) callback resolves them from the request scope, so each gets the request's
// coordinator). DesignTimeDbContextFactory (migrations) has no service provider, so the interceptors are
// intentionally absent there and migrations are unaffected.
builder.Services.AddScoped<CommandTransactionCoordinator>();
builder.Services.AddScoped<CommandSaveChangesInterceptor>();
builder.Services.AddScoped<CommandWriteCommandInterceptor>();
builder.Services.AddScoped<IDocumentQueryService, DocumentQueryService>();
builder.Services.AddScoped<ITagPathResolver, TagPathResolver>();
builder.Services.AddScoped<IDocumentMetadataService, DocumentMetadataService>();
builder.Services.AddScoped<IDocumentClassifier, DocumentClassifier>();
builder.Services.AddScoped<ITransactionValidator, TransactionValidator>();
builder.Services.AddScoped<ISystemAccountProvider, SystemAccountProvider>();
builder.Services.AddScoped<ILinkedPriceManager, LinkedPriceManager>();
builder.Services.AddScoped<IBalanceEntryReconciler, BalanceEntryReconciler>();
builder.Services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IBalanceEntryService, BalanceEntryService>();
builder.Services.AddScoped<IValuationService, ValuationService>();
builder.Services.AddScoped<IPriceSourceScheduleStore, PriceSourceScheduleStore>();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.Lifetime = ServiceLifetime.Scoped;
    // Order is outer→inner. UnitOfWork is registered last (innermost) so it owns the transaction
    // boundary around the handler alone: authorization + validation run OUTSIDE the transaction, so a
    // rejected request fails fast without opening (and rolling back) a transaction.
    cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
});
builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Scoped);
builder.Services.AddProcessing(builder.Configuration);
builder.Services.AddPriceProviders(builder.Configuration);
builder.Services.AddSingleton<IDownloadPathBuilder, TagBasedDownloadPathBuilder>();
builder.Services.AddTextExtraction(builder.Configuration);
builder.Services.AddAiServices();
builder.Services.AddScoped<IAiProviderResolver, AiProviderResolver>();
builder.Services.AddScoped<IAiClassificationService, AiClassificationService>();
builder.Services.AddThumbnails();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        // Replace {version} placeholder with actual version in paths
        var updatedPaths = new Microsoft.OpenApi.OpenApiPaths();
        foreach (var (key, value) in document.Paths)
        {
            updatedPaths.Add(key.Replace("{version}", "1"), value);
        }
        document.Paths = updatedPaths;

        // Remove the 'version' path parameter from all operations
        foreach (var pathItem in document.Paths.Values)
        {
            if (pathItem.Operations is null) continue;
            foreach (var operation in pathItem.Operations.Values)
            {
                if (operation.Parameters is null) continue;
                var versionParam = operation.Parameters
                    .FirstOrDefault(p => p.Name == "version" && p.In == Microsoft.OpenApi.ParameterLocation.Path);
                if (versionParam is not null)
                    operation.Parameters.Remove(versionParam);
            }
        }

        return Task.CompletedTask;
    });

    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["CookieAuth"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = Homestead.WebApi.Authentication.AuthenticationExtensions.AuthCookieName,
                Description = "Homestead authentication cookie"
            }
        };

        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations ?? []))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("CookieAuth", document)] = []
            });
        }

        return Task.CompletedTask;
    });

    options.AddOperationTransformer<ProblemDetailsResponseTransformer>();
    options.AddSchemaTransformer<NullableEnumSchemaTransformer>();
});

// Runs the OIDC requirement check at host start (as a hosted service) rather than at
// service-registration time. The build-time OpenAPI generator does start the host, so the validator
// self-skips that run via BuildTimeOpenApi.IsGenerating instead of relying on startup being bypassed.
builder.Services.AddHostedService<Homestead.WebApi.Authentication.OidcStartupValidator>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

// Production security headers
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Serve static frontend assets before auth so they don't require authentication
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        // index.html and service worker files must always revalidate so browsers pick up new deployments
        if (path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/ngsw.json", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/ngsw-worker.js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
        else
        {
            // Hashed assets (JS/CSS/fonts/images) are safe to cache indefinitely
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ProcessingHub>("/hubs/processing").RequireAuthorization(AuthPolicies.RequireContributor);

app.MapIdentityEndpoints();
app.MapStatusEndpoints();

var versionedApi = app.NewVersionedApi();
versionedApi.MapDocumentsEndpoints();
versionedApi.MapProcessingEndpoints();
versionedApi.MapAdminEndpoints();
versionedApi.MapFinancesEndpoints();

if (!string.IsNullOrEmpty(app.Configuration.GetConnectionString("postgresdb")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// SPA fallback for client-side routing — must also send no-cache since this always serves index.html
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
}).AllowAnonymous();

app.Run();
