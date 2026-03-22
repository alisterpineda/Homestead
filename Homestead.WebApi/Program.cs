using Asp.Versioning;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Features.Admin;
using Homestead.WebApi.Features.Identity;
using Homestead.WebApi.Features.Documents;
using Homestead.WebApi.Features.Documents.v1;
using Homestead.WebApi.Features.Processing;
using Homestead.WebApi.Features.Status;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Processing.Hubs;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Features.Documents.TextExtraction;
using Homestead.WebApi.Features.Documents.Thumbnails;
using Microsoft.AspNetCore.Identity;
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
    builder.Configuration["Oidc:AdminClaimValue"] =
        Environment.GetEnvironmentVariable("OIDC_ADMIN_CLAIM_VALUE") ?? "";

    var disableLocal = Environment.GetEnvironmentVariable("OIDC_DISABLE_LOCAL_LOGIN");
    if (!string.IsNullOrEmpty(disableLocal))
        builder.Configuration["Oidc:DisableLocalLogin"] = disableLocal;

    var additionalScopes = Environment.GetEnvironmentVariable("OIDC_ADDITIONAL_SCOPES");
    if (!string.IsNullOrEmpty(additionalScopes))
    {
        var scopes = additionalScopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < scopes.Length; i++)
            builder.Configuration[$"Oidc:AdditionalScopes:{i}"] = scopes[i];
    }
}

builder.AddNpgsqlDbContext<AppDbContext>(connectionName: "postgresdb");

// Serialize enums as strings in JSON responses and OpenAPI spec,
// without interfering with query parameter binding (which uses Enum.TryParse).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Add services to the container.

builder.AddAppAuthentication();
builder.Services.AddSignalR();
builder.Services.AddScoped<IProcessingNotifier, ProcessingNotifier>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IDocumentQueryService, DocumentQueryService>();
builder.Services.AddScoped<ISearchVectorUpdater, SearchVectorUpdater>();
builder.Services.AddScoped<ITagPathResolver, TagPathResolver>();
builder.Services.AddScoped<IDocumentMetadataService, DocumentMetadataService>();
builder.Services.AddScoped<IRoleSourceManager, RoleSourceManager>();
builder.Services.AddExceptionHandler<ForbiddenAccessExceptionHandler>();
builder.Services.AddExceptionHandler<NotFoundExceptionHandler>();
builder.Services.AddExceptionHandler<BadRequestExceptionHandler>();
builder.Services.AddExceptionHandler<ConflictExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.Lifetime = ServiceLifetime.Scoped;
});
builder.Services.AddProcessing();
builder.Services.AddTextExtraction(builder.Configuration);
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
                Name = ".AspNetCore.Identity.Application",
                Description = "ASP.NET Core Identity authentication cookie"
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
});

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
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ProcessingHub>("/hubs/processing").RequireAuthorization(AuthPolicies.RequireContributor);

app.MapIdentityEndpoints();
app.MapStatusEndpoints();

var versionedApi = app.NewVersionedApi();
versionedApi.MapDocumentsEndpoints();
versionedApi.MapProcessingEndpoints();
versionedApi.MapAdminEndpoints();

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

// SPA fallback for client-side routing
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
