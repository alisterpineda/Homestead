using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Homestead.WebApi.Tests.Unit;

/// <summary>
/// An InMemory-compatible <see cref="AppDbContext"/> that excludes
/// Postgres-specific column types (e.g. NpgsqlTsVector).
/// </summary>
public class TestAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
{
    public static TestAppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new TestAppDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Document>().Ignore(d => d.SearchVector);
    }
}
