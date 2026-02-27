using System.Security.Claims;
using Asp.Versioning;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Features.Documents;
using Homestead.WebApi.Features.Processing;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Processing.Hubs;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.TextExtraction;
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

var connectionString = builder.Configuration.GetConnectionString("postgresdb");

if (!string.IsNullOrEmpty(connectionString))
{
    builder.AddNpgsqlDbContext<AppDbContext>(connectionName: "postgresdb");
}

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
builder.Services.AddHandlers();
builder.Services.AddProcessing();
builder.Services.AddTextExtraction(builder.Configuration);

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

// Serve static frontend assets before auth so they don't require authentication
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<ProcessingHub>("/hubs/processing").RequireAuthorization();

// Identity API endpoints under /api/auth
var authGroup = app.MapGroup("/api/auth");
authGroup.MapIdentityApi<AppUser>();

// Custom registration endpoint that accepts displayName
authGroup.MapPost("/custom-register", async (
    CustomRegisterRequest request,
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager) =>
{
    var user = new AppUser
    {
        UserName = request.Email,
        Email = request.Email,
        DisplayName = request.DisplayName
    };

    var result = await userManager.CreateAsync(user, request.Password);
    if (!result.Succeeded)
    {
        return Results.ValidationProblem(
            result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
    }

    await signInManager.SignInAsync(user, isPersistent: true);
    return Results.Ok();
}).AllowAnonymous();

// Logout endpoint
authGroup.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.Ok();
}).RequireAuthorization();

// User info endpoint for the SPA to check session status
authGroup.MapGet("/user-info", async (
    HttpContext context,
    UserManager<AppUser> userManager) =>
{
    var user = context.User;
    if (user.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var appUser = await userManager.GetUserAsync(user);
    if (appUser is null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        id = appUser.Id,
        email = appUser.Email,
        displayName = appUser.DisplayName
    });
}).RequireAuthorization();

var versionedApi = app.NewVersionedApi();
versionedApi.MapDocumentsEndpoints();
versionedApi.MapProcessingEndpoints();

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

// SPA fallback for client-side routing
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

record CustomRegisterRequest(string Email, string DisplayName, string Password);
