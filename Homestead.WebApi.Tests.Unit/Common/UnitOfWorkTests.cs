using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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
    public async Task ExecuteInTransactionAsync_InvokesWork_AndReturnsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var workRan = false;

        var result = await _sut.ExecuteInTransactionAsync(() =>
        {
            workRan = true;
            return Task.FromResult("done");
        }, ct);

        Assert.True(workRan);
        Assert.Equal("done", result);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_PersistsWorkAndFlushesAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var currentUserId = Guid.NewGuid();
        _currentUser.UserId.Returns(currentUserId);

        var userId = Guid.NewGuid();

        await _sut.ExecuteInTransactionAsync(() =>
        {
            _db.Users.Add(new AppUser { Id = userId, Email = "u@test.com", CreatedAt = DateTimeOffset.UtcNow });
            _auditContext.Created("AppUser", userId);
            return Task.FromResult(userId);
        }, ct);

        // The work was flushed + committed by ExecuteInTransactionAsync (it calls SaveChangesAsync itself),
        // so a store-backed query now sees the row and the change tracker is clean.
        Assert.False(_db.ChangeTracker.HasChanges());
        Assert.True(await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, ct));
        _auditLogger.Received(1).ForUser(currentUserId);
        _auditLogger.Received(1).LogCreated("AppUser", userId, null);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_DrainsRegisteredHooks_OnceAfterCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var runCount = 0;

        await _sut.ExecuteInTransactionAsync(() =>
        {
            _sut.OnAfterCommit(_ => { runCount++; return Task.CompletedTask; });
            return Task.FromResult(0);
        }, ct);

        Assert.Equal(1, runCount);

        // Hooks are cleared after the first drain: a second transaction does not re-run them.
        await _sut.ExecuteInTransactionAsync(() => Task.FromResult(0), ct);
        Assert.Equal(1, runCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WhenTransactionAlreadyOpen_JoinsWithoutCommittingOrDraining()
    {
        // A nested command dispatched within an already-open transaction must join it: run the work and
        // return, but leave commit + hook draining to the outermost command. We can't open a real
        // ambient transaction on the InMemory provider (it always reports CurrentTransaction == null), so
        // we drive the join branch with a DbContext whose facade reports an open transaction.
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ambientTxDb = new AmbientTransactionDbContext(options);
        var sut = new UnitOfWork(ambientTxDb, new AuditContext(), _auditLogger, _currentUser);

        var workRan = false;
        var hookRan = false;

        var result = await sut.ExecuteInTransactionAsync(() =>
        {
            workRan = true;
            sut.OnAfterCommit(_ => { hookRan = true; return Task.CompletedTask; });
            return Task.FromResult("joined");
        }, ct);

        Assert.Equal("joined", result);
        Assert.True(workRan);
        // The outermost command owns the commit and hook draining, so the joined call must not drain.
        Assert.False(hookRan);
    }

    /// <summary>
    /// A <see cref="TestAppDbContext"/> whose <see cref="DatabaseFacade"/> reports an open ambient
    /// transaction, used to exercise the join branch of <see cref="UnitOfWork.ExecuteInTransactionAsync{T}"/>
    /// (the InMemory provider always reports <c>CurrentTransaction == null</c>, so the branch is otherwise
    /// unreachable in a unit test).
    /// </summary>
    private sealed class AmbientTransactionDbContext(DbContextOptions<AppDbContext> options)
        : TestAppDbContext(options)
    {
        private DatabaseFacade? _database;
        public override DatabaseFacade Database => _database ??= new StubTransactionFacade(this);

        private sealed class StubTransactionFacade(DbContext context) : DatabaseFacade(context)
        {
            public override IDbContextTransaction? CurrentTransaction { get; } =
                Substitute.For<IDbContextTransaction>();
        }
    }
}
