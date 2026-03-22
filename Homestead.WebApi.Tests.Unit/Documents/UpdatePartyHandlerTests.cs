using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Features.Documents.v1.UpdateParty;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class UpdatePartyHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly UpdatePartyHandler _sut;

    public UpdatePartyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new UpdatePartyHandler(_db, accessService, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_WithChanges_LogsUpdatedAudit()
    {
        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            Description = "Old desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Parties.Add(party);
        await _db.SaveChangesAsync();

        var command = new UpdatePartyCommand
        {
            Id = party.Id,
            Name = "New Name",
            Description = "New desc"
        };

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Party", party.Id,
            Arg.Is<List<FieldChange>>(c => c.Count > 0), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_NoChanges_LogsEmptyChanges()
    {
        var party = new Party
        {
            Id = Guid.NewGuid(),
            Name = "Same Name",
            Description = "Same desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Parties.Add(party);
        await _db.SaveChangesAsync();

        var command = new UpdatePartyCommand
        {
            Id = party.Id,
            Name = "Same Name",
            Description = "Same desc"
        };

        await _sut.HandleAsync(command, CancellationToken.None);

        // LogUpdated is still called, but AuditLogger.LogUpdated handles the empty-changes case
        _auditLogger.Received(1).LogUpdated("Party", party.Id,
            Arg.Is<List<FieldChange>>(c => c.Count == 0), Arg.Any<Guid?>());
    }
}
