using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Authorization;

/// <summary>
/// Covers the TrashFilter × access matrix of the document source methods. InMemory applies
/// Document's named soft-delete filter on root queries (which is all these methods build), so the
/// scope assertions are faithful; join-drop behavior through navigations is relational-only and
/// stays covered by integration tests.
/// </summary>
public class DocumentQueryServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ICurrentUserAccessor _currentUser = null!;
    private DocumentQueryService _sut = null!;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _memberGroupId = Guid.NewGuid();
    private readonly Guid _otherGroupId = Guid.NewGuid();

    private Document _activeGlobal = null!;
    private Document _trashedGlobal = null!;
    private Document _activeInMemberGroup = null!;
    private Document _trashedInMemberGroup = null!;
    private Document _activeInOtherGroup = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();

        var now = DateTimeOffset.UtcNow;
        _activeGlobal = Seed("active global");
        _trashedGlobal = Seed("trashed global", deletedAt: now);
        _activeInMemberGroup = Seed("active in member group", _memberGroupId);
        _trashedInMemberGroup = Seed("trashed in member group", _memberGroupId, now);
        _activeInOtherGroup = Seed("active in other group", _otherGroupId);
        await _db.SaveChangesAsync();

        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _sut = new DocumentQueryService(_currentUser, _db);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private Document Seed(string name, Guid? groupId = null, DateTimeOffset? deletedAt = null)
    {
        var document = new Document { Id = Guid.NewGuid(), Name = name, GroupId = groupId, DeletedAt = deletedAt };
        _db.Documents.Add(document);
        return document;
    }

    private void SetUser(AppRole role, GroupAccessLevel? memberGroupAccess = null)
    {
        _currentUser.Role.Returns(role);
        _currentUser.UserId.Returns(_userId);

        if (memberGroupAccess is { } access)
        {
            _db.GroupMembers.Add(new GroupMember
            {
                GroupId = _memberGroupId, UserId = _userId, AccessLevel = access,
            });
            _db.SaveChanges();
        }
    }

    private async Task<Guid[]> IdsOf(IQueryable<Document> query) =>
        await query.Select(d => d.Id).ToArrayAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task AccessibleDocuments_ActiveOnly_ExcludesTrashed()
    {
        SetUser(AppRole.Admin);

        var ids = await IdsOf(_sut.AccessibleDocuments());

        Assert.Equal(
            new[] { _activeGlobal.Id, _activeInMemberGroup.Id, _activeInOtherGroup.Id }.Order(),
            ids.Order());
    }

    [Fact]
    public async Task AccessibleDocuments_TrashedOnly_ReturnsOnlyTrashed()
    {
        SetUser(AppRole.Admin);

        var ids = await IdsOf(_sut.AccessibleDocuments(TrashFilter.TrashedOnly));

        Assert.Equal(
            new[] { _trashedGlobal.Id, _trashedInMemberGroup.Id }.Order(),
            ids.Order());
    }

    [Fact]
    public async Task AccessibleDocuments_IncludeTrashed_ReturnsActiveAndTrashed()
    {
        SetUser(AppRole.Admin);

        var ids = await IdsOf(_sut.AccessibleDocuments(TrashFilter.IncludeTrashed));

        Assert.Equal(5, ids.Length);
    }

    [Fact]
    public async Task AccessibleDocuments_Contributor_ScopesToGlobalAndMemberGroups()
    {
        SetUser(AppRole.Contributor, GroupAccessLevel.Viewer);

        var ids = await IdsOf(_sut.AccessibleDocuments(TrashFilter.IncludeTrashed));

        // Global + member-group docs (trashed included); the other group's doc is out of scope.
        Assert.Equal(
            new[] { _activeGlobal.Id, _trashedGlobal.Id, _activeInMemberGroup.Id, _trashedInMemberGroup.Id }.Order(),
            ids.Order());
    }

    [Fact]
    public async Task AccessibleDocuments_Guest_OnlyMemberGroupDocs()
    {
        SetUser(AppRole.Guest, GroupAccessLevel.Viewer);

        var ids = await IdsOf(_sut.AccessibleDocuments(TrashFilter.IncludeTrashed));

        Assert.Equal(
            new[] { _activeInMemberGroup.Id, _trashedInMemberGroup.Id }.Order(),
            ids.Order());
    }

    [Fact]
    public async Task WritableDocuments_TrashedOnly_ContributorEditor_ReturnsWritableTrashed()
    {
        SetUser(AppRole.Contributor, GroupAccessLevel.Editor);

        var ids = await IdsOf(_sut.WritableDocuments(TrashFilter.TrashedOnly));

        Assert.Equal(
            new[] { _trashedGlobal.Id, _trashedInMemberGroup.Id }.Order(),
            ids.Order());
    }

    [Fact]
    public async Task WritableDocuments_Viewer_ReturnsNothing()
    {
        SetUser(AppRole.Viewer, GroupAccessLevel.Viewer);

        var ids = await IdsOf(_sut.WritableDocuments(TrashFilter.IncludeTrashed));

        Assert.Empty(ids);
    }
}
