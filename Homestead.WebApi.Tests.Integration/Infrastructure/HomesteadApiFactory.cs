using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Modules.Finances.Seeding;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Disable the autonomous scheduler tick so tests can drive it deterministically
        // by resolving JobSchedulerService and calling TryFireOneDueScheduleAsync.
        builder.UseSetting("Processing:Scheduling:Enabled", "false");

        // Point Tesseract at the host's installed tessdata so PDF pages without an
        // embedded text layer (e.g. scanned fixtures) can OCR. The default relative
        // "tessdata" path doesn't exist next to the test bin on Linux runners.
        var tessDataPath = ResolveTessDataPath();
        if (tessDataPath is not null)
            builder.UseSetting($"{TesseractOptions.SectionName}:{nameof(TesseractOptions.TessDataPath)}", tessDataPath);

        builder.ConfigureServices(services =>
        {
            // Register test-only force-login endpoint for inactive user authentication
            services.AddSingleton<IStartupFilter, TestForceLoginStartupFilter>();

            // Substitute the AI service so classify/summarize flows are deterministic and never make
            // a live model call. Benign by default (only reached when a provider resolves, which no
            // existing test triggers); summarize/classify tests seed an enabled provider to exercise it.
            services.RemoveAll<IAiClassificationService>();
            services.AddScoped<IAiClassificationService, FakeAiClassificationService>();

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

    private static string? ResolveTessDataPath()
    {
        var candidates = new[]
        {
            "/usr/share/tesseract-ocr/5/tessdata",
            "/usr/share/tesseract-ocr/4.00/tessdata",
            "/usr/share/tessdata",
            "/opt/homebrew/share/tessdata",
            "/usr/local/share/tessdata"
        };
        return candidates.FirstOrDefault(p => Directory.Exists(p) && File.Exists(Path.Combine(p, "eng.traineddata")));
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
