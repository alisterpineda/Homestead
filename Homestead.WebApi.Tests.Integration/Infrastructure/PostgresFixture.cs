using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
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

        Respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // finances.commodities holds the ISO 4217 seed. Respawn will wipe it
            // along with the rest of the schema; IntegrationTestBase restores the
            // seed with CurrencySeeder.Seed() after each reset.
            SchemasToInclude = ["public", "identity", "auditing", "documents", "auth", "settings", "finances"],
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
