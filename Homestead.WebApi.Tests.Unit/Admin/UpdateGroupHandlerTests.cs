using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Features.Admin.v1.UpdateGroup;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateGroupHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly UpdateGroupHandler _sut;

    public UpdateGroupHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new UpdateGroupHandler(_db, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithChanges_LogsUpdatedAudit()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            Description = "Old desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();

        var command = new UpdateGroupCommand
        {
            Id = group.Id,
            Request = new UpdateGroupRequest { Name = "New Name", Description = "New desc" }
        };

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Group", group.Id,
            Arg.Is<List<FieldChange>>(c => c.Count > 0), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoChanges_LogsEmptyChanges()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Same",
            Description = "Same desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync();

        var command = new UpdateGroupCommand
        {
            Id = group.Id,
            Request = new UpdateGroupRequest { Name = "Same", Description = "Same desc" }
        };

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Group", group.Id,
            Arg.Is<List<FieldChange>>(c => c.Count == 0), Arg.Any<Guid?>());
    }
}
