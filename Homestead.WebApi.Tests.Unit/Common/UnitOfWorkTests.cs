using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

public class UnitOfWorkTests : IDisposable
{
    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly AuditContext _auditContext = new();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly UnitOfWork _sut;

    public UnitOfWorkTests()
    {
        // ForUser returns the same mock so we can assert which actor was resolved + the log calls on it.
        _auditLogger.ForUser(Arg.Any<Guid?>()).Returns(_auditLogger);
        _sut = new UnitOfWork(_db, _auditContext, _auditLogger, _currentUser);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveChangesAsync_NoChangesNoAudit_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;

        var affected = await _sut.SaveChangesAsync(ct);

        Assert.Equal(0, affected);
        // Idempotent: nothing to flush, so the audit seam is never engaged.
        _auditLogger.DidNotReceive().ForUser(Arg.Any<Guid?>());
    }

    [Fact]
    public async Task SaveChangesAsync_WithAuditEntry_FlushesViaCurrentUser_WhenNoActorSet()
    {
        var ct = TestContext.Current.CancellationToken;
        var currentUserId = Guid.NewGuid();
        _currentUser.UserId.Returns(currentUserId);

        var entityId = Guid.NewGuid();
        _auditContext.Created("Document", entityId);

        await _sut.SaveChangesAsync(ct);

        _auditLogger.Received(1).ForUser(currentUserId);
        _auditLogger.Received(1).LogCreated("Document", entityId, null);
    }

    [Fact]
    public async Task SaveChangesAsync_WithActorOverride_AttributesAuditToActor_NotCurrentUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var currentUserId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _currentUser.UserId.Returns(currentUserId);

        var entityId = Guid.NewGuid();
        _auditContext.Created("Document", entityId);

        _sut.SetActor(actorId);
        await _sut.SaveChangesAsync(ct);

        _auditLogger.Received(1).ForUser(actorId);
        _auditLogger.DidNotReceive().ForUser(currentUserId);
    }

    [Fact]
    public async Task RunAfterCommitHooksAsync_RunsRegisteredHooks_Once()
    {
        var ct = TestContext.Current.CancellationToken;
        var runCount = 0;
        _sut.OnAfterCommit(_ => { runCount++; return Task.CompletedTask; });

        await _sut.RunAfterCommitHooksAsync(ct);
        await _sut.RunAfterCommitHooksAsync(ct); // hooks are cleared after the first drain

        Assert.Equal(1, runCount);
    }

    [Fact]
    public async Task RunAfterCommitHooksAsync_NoHooks_DoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.RunAfterCommitHooksAsync(ct);
    }
}
