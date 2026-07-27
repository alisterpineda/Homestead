using Homestead.WebApi.Domain.Documents;
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
    public static TestAppDbContext Create() => Create(Guid.NewGuid().ToString());

    /// <summary>
    /// Opens a context over a named in-memory database, so several contexts can share one store.
    /// </summary>
    /// <remarks>
    /// Needed wherever the code under test resolves its own <c>AppDbContext</c> from a DI scope: the
    /// scope disposes what it created, so handing it the test's own instance would leave the test holding
    /// a disposed context. Sharing by database name gives each side its own lifetime over one store.
    /// </remarks>
    public static TestAppDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Document>().Ignore(d => d.SearchVector);
    }
}
