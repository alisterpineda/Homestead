using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public HomesteadApiFactory Factory { get; private set; } = null!;
    public Respawner Respawner { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new HomesteadApiFactory(ConnectionString);
        await Factory.InitializeAsync();

        // Force the host to start (triggers migrations + role seeding) once
        _ = Factory.Server;

        // Build the Respawner once — schema introspection is expensive
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        // Derive the reset scope from the EF model so a new module's schema is cleaned
        // automatically — the 'inventory' schema was once missing from a hardcoded list here, and
        // its tables silently leaked across tests. The historical literals are kept as a baseline
        // union in case a schema ever holds only non-model tables (raw-SQL migrations).
        string[] baselineSchemas = ["public", "identity", "auditing", "documents", "auth", "settings", "finances"];
        string[] modelSchemas;
        using (var scope = Factory.Services.CreateScope())
        {
            var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;
            modelSchemas = model.GetEntityTypes()
                .Select(e => e.GetSchema() ?? model.GetDefaultSchema() ?? "public")
                .ToArray();
        }

        Respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // finances.commodities holds the ISO 4217 seed. Respawn will wipe it
            // along with the rest of the schema; IntegrationTestBase restores the
            // seed with CurrencySeeder.Seed() after each reset.
            SchemasToInclude = [.. baselineSchemas.Union(modelSchemas)],
            TablesToIgnore = [
                new Respawn.Graph.Table("identity", "roles"),
                new Respawn.Graph.Table("public", "__EFMigrationsHistory")
            ]
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}
