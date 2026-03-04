using Homestead.WebApi.Common;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Common;

public class QueryableExtensionsTests
{
    private static DbContextOptions<TestDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task ToPaginatedAsync_ReturnsCorrectPageAndCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new TestDbContext(CreateOptions());
        for (var i = 1; i <= 15; i++)
            db.Items.Add(new TestItem { Name = $"Item{i:D2}" });
        await db.SaveChangesAsync(ct);

        var result = await db.Items
            .OrderBy(x => x.Name)
            .ToPaginatedAsync(new PaginatedRequest { Page = 2, PageSize = 5 }, ct);

        Assert.Equal(15, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task ToPaginatedAsync_EmptyQuery_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new TestDbContext(CreateOptions());

        var result = await db.Items
            .OrderBy(x => x.Name)
            .ToPaginatedAsync(new PaginatedRequest { Page = 1, PageSize = 10 }, ct);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ToPaginatedAsync_LastPartialPage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new TestDbContext(CreateOptions());
        for (var i = 1; i <= 7; i++)
            db.Items.Add(new TestItem { Name = $"Item{i}" });
        await db.SaveChangesAsync(ct);

        var result = await db.Items
            .OrderBy(x => x.Name)
            .ToPaginatedAsync(new PaginatedRequest { Page = 2, PageSize = 5 }, ct);

        Assert.Equal(7, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    private class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestItem> Items => Set<TestItem>();
    }

    private class TestItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Name { get; set; }
    }
}
