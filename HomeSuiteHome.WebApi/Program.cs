using Asp.Versioning;
using HomeSuiteHome.WebApi.Authentication;
using HomeSuiteHome.WebApi.Common;
using HomeSuiteHome.WebApi.Features.Documents;
using HomeSuiteHome.WebApi.Features.Auth;
using HomeSuiteHome.WebApi.Features.Tags;
using HomeSuiteHome.WebApi.Persistence;
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
        var dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "homesuitehome";
        var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";

        builder.Configuration["ConnectionStrings:postgresdb"] =
            $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";
    }
}

// Map OIDC env vars into Auth config (only if set)
var oidcIssuer = Environment.GetEnvironmentVariable("OIDC_ISSUER");
if (!string.IsNullOrEmpty(oidcIssuer))
    builder.Configuration["Auth:Authority"] = oidcIssuer;

var oidcClientId = Environment.GetEnvironmentVariable("OIDC_CLIENT_ID");
if (!string.IsNullOrEmpty(oidcClientId))
    builder.Configuration["Auth:Audience"] = oidcClientId;

var oidcClaimSubject = Environment.GetEnvironmentVariable("OIDC_CLAIM_SUBJECT");
if (!string.IsNullOrEmpty(oidcClaimSubject))
    builder.Configuration["Auth:ClaimMappings:Subject"] = oidcClaimSubject;

var oidcClaimEmail = Environment.GetEnvironmentVariable("OIDC_CLAIM_EMAIL");
if (!string.IsNullOrEmpty(oidcClaimEmail))
    builder.Configuration["Auth:ClaimMappings:Email"] = oidcClaimEmail;

var oidcClaimName = Environment.GetEnvironmentVariable("OIDC_CLAIM_NAME");
if (!string.IsNullOrEmpty(oidcClaimName))
    builder.Configuration["Auth:ClaimMappings:Name"] = oidcClaimName;

var oidcClaimRoles = Environment.GetEnvironmentVariable("OIDC_CLAIM_ROLES");
if (!string.IsNullOrEmpty(oidcClaimRoles))
    builder.Configuration["Auth:ClaimMappings:Roles"] = oidcClaimRoles;

var connectionString = builder.Configuration.GetConnectionString("postgresdb");

if (!string.IsNullOrEmpty(connectionString))
{
    builder.AddNpgsqlDbContext<AppDbContext>(connectionName: "postgresdb");
}

// Add services to the container.

builder.AddAppAuthentication();
builder.Services.AddHandlers();

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
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "JWT Bearer token from the identity provider"
            }
        };

        foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations ?? []))
        {
            operation.Value.Security ??= [];
            operation.Value.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

if (!string.IsNullOrEmpty(connectionString))
{
    app.UseMiddleware<UserProvisioningMiddleware>();
}

var versionedApi = app.NewVersionedApi();
versionedApi.MapAuthEndpoints();
versionedApi.MapDocumentsEndpoints();
versionedApi.MapTagsEndpoints();

if (!string.IsNullOrEmpty(connectionString))
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

app.UseHttpsRedirection();

// Serve the frontend
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();