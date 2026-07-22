using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Inventory;

/// <summary>
/// DB-schema-level coverage for the DurableKind → IsContainer migration: the column swap landed and
/// the rewritten check constraints enforce the fungible/non-fungible ↔ IsContainer null/non-null
/// pairing. Raw SQL is used to exercise the constraints directly, bypassing handler validation.
/// </summary>
[Collection("Integration")]
public class ItemIsContainerConstraintsTests : IntegrationTestBase
{
    public ItemIsContainerConstraintsTests(PostgresFixture fixture) : base(fixture) { }

    private const string CheckViolation = "23514";

    [Fact]
    public async Task Migration_DroppedDurableKind_AddedIsContainer()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database
            .SqlQueryRaw<string>(
                "SELECT column_name FROM information_schema.columns "
                + "WHERE table_schema = 'inventory' AND table_name = 'items'")
            .ToListAsync(CT);

        Assert.Contains("IsContainer", columns);
        Assert.DoesNotContain("DurableKind", columns);
    }

    [Fact]
    public async Task Constraint_RejectsFungibleRowWithNonNullIsContainer()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"Unit\", \"IsContainer\") "
            + "VALUES (gen_random_uuid(), 'Bad consumable', 'Fungible', 'Active', 'units', TRUE)", CT));

        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Constraint_RejectsNonFungibleRowWithNullIsContainer()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"IsContainer\") "
            + "VALUES (gen_random_uuid(), 'Bad durable', 'NonFungible', 'Active', NULL)", CT));

        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Constraint_AllowsNonFungibleContainer()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"IsContainer\") "
            + "VALUES (gen_random_uuid(), 'Good container', 'NonFungible', 'Active', TRUE)", CT);

        Assert.Equal(1, affected);
    }

    [Fact]
    public async Task Constraint_AllowsNonFungibleNonContainer()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"IsContainer\") "
            + "VALUES (gen_random_uuid(), 'Good regular item', 'NonFungible', 'Active', FALSE)", CT);

        Assert.Equal(1, affected);
    }
}
