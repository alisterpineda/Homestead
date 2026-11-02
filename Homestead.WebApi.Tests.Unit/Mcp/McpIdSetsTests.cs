using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

// The id-list tools' input rule (1..cap distinct ids) and their not_found_ids (requested − accessible).
public class McpIdSetsTests
{
    private static readonly Guid A = Guid.Parse("0197a000-0000-7000-8000-00000000000a");
    private static readonly Guid B = Guid.Parse("0197a000-0000-7000-8000-00000000000b");
    private static readonly Guid C = Guid.Parse("0197a000-0000-7000-8000-00000000000c");

    private static Guid[] FreshIds(int count) => Enumerable.Range(1, count).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

    [Fact]
    public void Missing_IsRequestedMinusFound_InRequestOrder()
    {
        Assert.Equal([C, A], McpIdSets.Missing(requested: [C, B, A], found: [B]));
    }

    [Fact]
    public void Missing_IsEmpty_WhenEverythingWasFound()
    {
        Assert.Empty(McpIdSets.Missing(requested: [A, B], found: [B, A]));
    }

    [Fact]
    public void Missing_ReportsADuplicatedRequestOnce()
    {
        Assert.Equal([A], McpIdSets.Missing(requested: [A, A, B], found: [B]));
    }

    [Fact]
    public void Missing_IgnoresFoundIdsThatWereNotRequested()
    {
        Assert.Equal([A], McpIdSets.Missing(requested: [A], found: [B, C]));
    }

    [Fact]
    public void RequireIds_ReturnsTheDistinctIds_InOrder()
    {
        Assert.Equal([B, A], McpIdSets.RequireIds([B, A, B], McpIdSets.TrashMaxIds, "document_ids"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public void RequireIds_AcceptsUpToTheTrashCap(int count)
    {
        Assert.Equal(count, McpIdSets.RequireIds(FreshIds(count), McpIdSets.TrashMaxIds, "document_ids").Count);
    }

    [Fact]
    public void RequireIds_21ForTrash_IsInvalid_NamingTheCap()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            McpIdSets.RequireIds(FreshIds(21), McpIdSets.TrashMaxIds, "document_ids"));

        Assert.Contains("document_ids", e.Message);
        Assert.Contains("20", e.Message);
    }

    [Fact]
    public void RequireIds_AcceptsUpToTheBulkUpdateCap()
    {
        Assert.Equal(100, McpIdSets.RequireIds(FreshIds(100), McpIdSets.BulkUpdateMaxIds, "document_ids").Count);
    }

    [Fact]
    public void RequireIds_101ForBulkUpdate_IsInvalid_NamingTheCap()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            McpIdSets.RequireIds(FreshIds(101), McpIdSets.BulkUpdateMaxIds, "document_ids"));

        Assert.Contains("100", e.Message);
    }

    [Fact]
    public void RequireIds_DuplicatesDoNotCountTowardsTheCap()
    {
        var ids = FreshIds(20).Concat(FreshIds(5)).ToArray();

        Assert.Equal(20, McpIdSets.RequireIds(ids, McpIdSets.TrashMaxIds, "document_ids").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequireIds_NoIds_IsInvalid(bool nullList)
    {
        var e = Assert.Throws<ArgumentException>(() =>
            McpIdSets.RequireIds(nullList ? null : [], McpIdSets.TrashMaxIds, "document_ids"));

        Assert.Contains("document_ids", e.Message);
    }
}
