using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Homestead.WebApi.Tests.Integration.Inventory;

/// <summary>
/// DB-schema-level coverage for the IsContainer → DurableKind migration: the column swap landed and
/// the rewritten check constraints enforce the fungible/non-fungible ↔ DurableKind null/non-null
/// pairing. Raw SQL is used to exercise the constraints directly, bypassing handler validation.
/// </summary>
[Collection("Integration")]
public class ItemDurableKindConstraintsTests : IntegrationTestBase
{
    public ItemDurableKindConstraintsTests(PostgresFixture fixture) : base(fixture) { }

    private const string CheckViolation = "23514";

    [Fact]
    public async Task Migration_DroppedIsContainer_AddedDurableKind()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var columns = await db.Database
            .SqlQueryRaw<string>(
                "SELECT column_name FROM information_schema.columns "
                + "WHERE table_schema = 'inventory' AND table_name = 'items'")
            .ToListAsync(CT);

        Assert.Contains("DurableKind", columns);
        Assert.DoesNotContain("IsContainer", columns);
    }

    [Fact]
    public async Task Constraint_RejectsFungibleRowWithNonNullDurableKind()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"Unit\", \"DurableKind\") "
            + "VALUES (gen_random_uuid(), 'Bad consumable', 'Fungible', 'Active', 'units', 'Container')", CT));

        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Constraint_RejectsNonFungibleRowWithNullDurableKind()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"DurableKind\") "
            + "VALUES (gen_random_uuid(), 'Bad durable', 'NonFungible', 'Active', NULL)", CT));

        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Constraint_AllowsNonFungibleRowWithDurableKind()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO inventory.items (\"Id\", \"Name\", \"Fungibility\", \"Status\", \"DurableKind\") "
            + "VALUES (gen_random_uuid(), 'Good assembly', 'NonFungible', 'Active', 'Assembly')", CT);

        Assert.Equal(1, affected);
    }
}
