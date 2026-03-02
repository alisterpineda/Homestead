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

    public HomesteadApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string MediaPath => _mediaPath;

    public ValueTask InitializeAsync()
    {
        _mediaPath = Path.Combine(Path.GetTempPath(), "homestead-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_mediaPath);
        return ValueTask.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:postgresdb", _connectionString);
        builder.UseSetting("FileStorage:MediaPath", _mediaPath);

        builder.ConfigureServices(services =>
        {
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
    }
}
