using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

public class HomesteadApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private string _mediaPath = null!;
    private string _cachePath = null!;

    public HomesteadApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string MediaPath => _mediaPath;
    public string CachePath => _cachePath;

    public ValueTask InitializeAsync()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "homestead-tests", Guid.NewGuid().ToString());
        _mediaPath = Path.Combine(basePath, "media");
        _cachePath = Path.Combine(basePath, "cache");
        Directory.CreateDirectory(_mediaPath);
        Directory.CreateDirectory(_cachePath);
        return ValueTask.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:postgresdb", _connectionString);
        builder.UseSetting("FileStorage:MediaPath", _mediaPath);
        builder.UseSetting("FileStorage:CachePath", _cachePath);

        // Satisfy OidcStartupValidator. OIDC is never actually exercised in integration tests
        // (auth happens through /test/force-login), so placeholder values are fine.
        builder.UseSetting("Oidc:Authority", "https://test-oidc.local");
        builder.UseSetting("Oidc:ClientId", "test-client");
        builder.UseSetting("Oidc:ClientSecret", "test-secret");

        builder.ConfigureServices(services =>
        {
            // Register test-only force-login endpoint for inactive user authentication
            services.AddSingleton<IStartupFilter, TestForceLoginStartupFilter>();

            // Remove ALL Aspire/EF Core DbContext-related registrations to avoid lifetime conflicts.
            // Aspire's AddNpgsqlDbContext registers pooling services (singleton IDbContextPool)
            // that conflict with a plain AddDbContext (scoped DbContextOptions).
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType == typeof(AppDbContext) ||
                    (d.ServiceType.IsGenericType &&
                        (d.ServiceType.GetGenericTypeDefinition().FullName?.Contains("IDbContextPool") == true ||
                         d.ServiceType.GetGenericTypeDefinition().FullName?.Contains("IScopedDbContextLease") == true ||
                         d.ServiceType.GetGenericTypeDefinition().FullName?.Contains("IDbContextOptionsConfiguration") == true)))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(_connectionString);
                // Re-register the seeding hooks — ConfigureServices above stripped the
                // Aspire DbContext registration that originally carried them.
                options.UseSeeding((ctx, created) =>
                {
                    CurrencySeeder.Seed(ctx, created);
                    SystemAccountSeeder.Seed(ctx, created);
                });
                options.UseAsyncSeeding(async (ctx, created, ct) =>
                {
                    await CurrencySeeder.SeedAsync(ctx, created, ct);
                    await SystemAccountSeeder.SeedAsync(ctx, created, ct);
                });
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(_mediaPath))
        {
            try { Directory.Delete(_mediaPath, recursive: true); }
            catch { /* best effort cleanup */ }
        }

        if (Directory.Exists(_cachePath))
        {
            try { Directory.Delete(_cachePath, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }
}
