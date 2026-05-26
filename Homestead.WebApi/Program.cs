using Asp.Versioning;
using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
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

builder.AddNpgsqlDbContext<AppDbContext>(
    connectionName: "postgresdb",
    configureDbContextOptions: opts => opts
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
        }));

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
builder.Services.AddScoped<IDocumentQueryService, DocumentQueryService>();
builder.Services.AddScoped<ISearchVectorUpdater, SearchVectorUpdater>();
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
    cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
    cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
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

// Hosted service (not a service-registration check) so build-time OpenAPI
// generation — which builds the host but never calls StartAsync — bypasses it.
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
