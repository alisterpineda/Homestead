using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Features.Documents.Parties.UpdateParty;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class UpdatePartyHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly UpdatePartyHandler _sut;

    public UpdatePartyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _audit = Substitute.For<IAuditContext>();
        _sut = new UpdatePartyHandler(_db, accessService, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithChanges_LogsUpdatedAudit()
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

        await _sut.Handle(command, CancellationToken.None);

        _audit.Received(1).Track<PartySnapshot, Party>(Arg.Any<Party>(), "Party", party.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoChanges_StillCallsTrack()
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

        await _sut.Handle(command, CancellationToken.None);

        // Track is always called before mutations; diff computation is deferred
        _audit.Received(1).Track<PartySnapshot, Party>(Arg.Any<Party>(), "Party", party.Id, Arg.Any<Guid?>());
    }
}
