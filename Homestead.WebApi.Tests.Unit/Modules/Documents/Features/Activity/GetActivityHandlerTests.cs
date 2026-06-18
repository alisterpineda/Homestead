using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Documents.Features.Activity.GetActivity;

namespace Homestead.WebApi.Tests.Unit.Modules.Documents.Features.Activity;

public class GetActivityHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetActivityHandler _sut;

    public GetActivityHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetActivityHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<AppUser> AddUserAsync(string displayName, string email)
    {
        var user = new AppUser { Id = Guid.NewGuid(), DisplayName = displayName, Email = email };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(Ct);
        return user;
    }

    private async Task<AuditLog> AddDocumentAuditAsync(
        Guid documentId, AuditAction action, Guid? userId, DateTimeOffset timestamp,
        List<FieldChange>? changes = null)
    {
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "Document",
            EntityId = documentId,
            Action = action,
            UserId = userId,
            Timestamp = timestamp,
            Changes = changes is null ? null : JsonSerializer.Serialize(changes, FieldChange.JsonOptions),
        };
        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync(Ct);
        return audit;
    }

    private async Task<AuditLog> AddVersionAuditAsync(
        Guid documentId, AuditAction action, Guid? userId, DateTimeOffset timestamp)
    {
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "DocumentVersion",
            EntityId = Guid.NewGuid(),
            ParentEntityId = documentId,
            Action = action,
            UserId = userId,
            Timestamp = timestamp,
        };
        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync(Ct);
        return audit;
    }

    private async Task<DocumentComment> AddCommentAsync(
        Guid documentId, string body, Guid? userId, DateTimeOffset timestamp)
    {
        var comment = new DocumentComment
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Body = body,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            CreatedByUserId = userId,
        };
        _db.DocumentComments.Add(comment);
        await _db.SaveChangesAsync(Ct);
        return comment;
    }

    [Fact]
    public async Task Handle_LeftJoinsUsers_NullAndDeletedActorsStillRender()
    {
        var documentId = Guid.NewGuid();
        var alice = await AddUserAsync("Alice", "alice@test.com");

        // Three Document audit rows: a known actor, a null (system/background) actor, and a since-deleted
        // actor (a UserId with no matching Users row). A LEFT join must keep all three; the old inner join
        // would drop the last two — the regression guarded here (Documents audit doc §3.1).
        await AddDocumentAuditAsync(documentId, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-3));
        await AddDocumentAuditAsync(documentId, AuditAction.Updated, userId: null, DateTimeOffset.UtcNow.AddMinutes(-2),
            changes: [new ScalarFieldChange("Name", "Old", "New")]);
        await AddDocumentAuditAsync(documentId, AuditAction.Updated, userId: Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1),
            changes: [new ScalarFieldChange("Notes", null, "Note")]);

        var result = await _sut.Handle(new GetActivityQuery { DocumentId = documentId }, Ct);

        Assert.Equal(3, result.TotalCount);

        var anonymous = result.Items.Where(i => i.UserDisplayName is null && i.UserEmail is null).ToList();
        Assert.Equal(2, anonymous.Count); // null actor + deleted actor — neither vanished
        Assert.All(anonymous, i => Assert.StartsWith("Someone ", i.Summary));

        var byAlice = Assert.Single(result.Items, i => i.UserDisplayName == "Alice");
        Assert.Equal("alice@test.com", byAlice.UserEmail);
        Assert.Equal("Alice created this document", byAlice.Summary);
    }

    [Fact]
    public async Task Handle_LeftJoinsUsers_NullActorOnDocumentVersionRowSurvives()
    {
        // The second Where arm (DocumentVersion rows matched by ParentEntityId) must survive the left join
        // too: a background reprocess job writes a version audit row with a null actor.
        var documentId = Guid.NewGuid();
        await AddVersionAuditAsync(documentId, AuditAction.Created, userId: null, DateTimeOffset.UtcNow);

        var result = await _sut.Handle(new GetActivityQuery { DocumentId = documentId }, Ct);

        var item = Assert.Single(result.Items);
        Assert.Equal("audit_log", item.Type);
        Assert.Null(item.UserDisplayName);
        Assert.Null(item.UserEmail);
        Assert.Equal("Someone added a new version", item.Summary);
    }

    [Fact]
    public async Task Handle_InterleavesAuditAndComments_NewestFirst()
    {
        var documentId = Guid.NewGuid();
        var alice = await AddUserAsync("Alice", "alice@test.com");

        await AddDocumentAuditAsync(documentId, AuditAction.Created, alice.Id, DateTimeOffset.UtcNow.AddMinutes(-4));
        await AddCommentAsync(documentId, "First comment", alice.Id, DateTimeOffset.UtcNow.AddMinutes(-3));
        await AddDocumentAuditAsync(documentId, AuditAction.Updated, userId: null, DateTimeOffset.UtcNow.AddMinutes(-2),
            changes: [new ScalarFieldChange("Name", "Old", "New")]);
        // A comment whose author has since been deleted (null CreatedByUserId) — must also survive.
        await AddCommentAsync(documentId, "Orphan comment", userId: null, DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _sut.Handle(new GetActivityQuery { DocumentId = documentId }, Ct);

        Assert.Equal(4, result.TotalCount);
        // Both sources are merged and ordered newest-first.
        Assert.Equal(["comment", "audit_log", "comment", "audit_log"], result.Items.Select(i => i.Type));
        Assert.Equal("Orphan comment", result.Items[0].Body);
        Assert.Null(result.Items[0].UserDisplayName);
        Assert.Equal(2, result.Items.Count(i => i.Type == "comment"));
        Assert.Equal(2, result.Items.Count(i => i.Type == "audit_log"));
    }

    [Fact]
    public async Task Handle_OrdersDeterministically_WhenTimestampsTie()
    {
        var documentId = Guid.NewGuid();
        var alice = await AddUserAsync("Alice", "alice@test.com");
        // Audit rows and a comment all sharing one timestamp (a coarse-resolution clock or same-tick batch),
        // spanning both ID spaces that Concat merges.
        var sameInstant = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
            await AddDocumentAuditAsync(documentId, AuditAction.Updated, alice.Id, sameInstant,
                changes: [new ScalarFieldChange("Notes", null, $"note {i}")]);
        await AddCommentAsync(documentId, "Tied comment", alice.Id, sameInstant);

        var first = await _sut.Handle(new GetActivityQuery { DocumentId = documentId }, Ct);
        var second = await _sut.Handle(new GetActivityQuery { DocumentId = documentId }, Ct);

        // The Id tiebreaker makes the order stable across requests, so paging can't drop/duplicate ties.
        Assert.Equal(first.Items.Select(x => x.Id), second.Items.Select(x => x.Id));
        Assert.Equal(
            first.Items.Select(x => x.Id).OrderByDescending(id => id),
            first.Items.Select(x => x.Id));
    }
}
