using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
        _sut = new UnitOfWork(_db, _auditContext, _auditLogger, _currentUser, new CommandTransactionCoordinator());
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DbContextOptions<AppDbContext> NewInMemoryOptions() => TestDbContextOptions.NewInMemory();

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
        var sut = new UnitOfWork(ambientTxDb, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());

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

    [Fact]
    public async Task ExecuteInTransactionAsync_LazyOpensTransaction_OnlyAfterWorkFlushes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        var beginDuringWork = -1;
        await sut.ExecuteInTransactionAsync(() =>
        {
            // The entity is staged but not flushed mid-work, so no transaction is opened yet.
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "u@test.com", CreatedAt = DateTimeOffset.UtcNow });
            beginDuringWork = facade.BeginCount;
            return Task.FromResult(0);
        }, ct);

        Assert.Equal(0, beginDuringWork);                 // lazy: nothing opened during the work
        Assert.Equal(1, facade.BeginCount);               // opened by the trailing flush
        await facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NoOpCommand_OpensNoTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        var hookRan = false;
        await sut.ExecuteInTransactionAsync(() =>
        {
            // No tracked changes, no audit: a true no-op command.
            sut.OnAfterCommit(_ => { hookRan = true; return Task.CompletedTask; });
            return Task.FromResult(0);
        }, ct);

        Assert.Equal(0, facade.BeginCount);
        await facade.Tx.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        Assert.True(hookRan, "after-commit hooks must still drain for a no-op command");
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NestedNonJoinFrameBeforeFirstFlush_OnlyOuterCommits()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        await sut.ExecuteInTransactionAsync(async () =>
        {
            // Nested send dispatched BEFORE any flush: CurrentTransaction is still null, so the inner frame
            // does NOT join — it nests (depth 2). The inner flush opens the single tx; only the outer commits.
            await sut.ExecuteInTransactionAsync(() =>
            {
                db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "inner@test.com", CreatedAt = DateTimeOffset.UtcNow });
                return Task.FromResult(0);
            }, ct);
            return 0;
        }, ct);

        Assert.Equal(1, facade.BeginCount);
        await facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await facade.Tx.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NestedAfterFirstFlush_JoinsViaCurrentTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        await sut.ExecuteInTransactionAsync(async () =>
        {
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "outer@test.com", CreatedAt = DateTimeOffset.UtcNow });
            await sut.SaveChangesAsync(ct);   // outer flush opens the tx; CurrentTransaction now non-null

            // Nested send now JOINS via CurrentTransaction (returns immediately, no second begin/commit).
            await sut.ExecuteInTransactionAsync(() =>
            {
                db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "inner@test.com", CreatedAt = DateTimeOffset.UtcNow });
                return Task.FromResult(0);
            }, ct);
            return 0;
        }, ct);

        Assert.Equal(1, facade.BeginCount);
        await facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await facade.Tx.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_NestedThrow_RollsBackExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        // Two non-join frames (nested before the first flush): the inner opens the tx then throws, so BOTH
        // catch frames run. The _rolledBack guard must collapse them to a single RollbackAsync (a second
        // RollbackAsync on the same transaction would throw).
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<int>(async () =>
            {
                await sut.ExecuteInTransactionAsync<int>(async () =>
                {
                    db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "inner@test.com", CreatedAt = DateTimeOffset.UtcNow });
                    await sut.SaveChangesAsync(ct);   // opens the tx
                    throw new InvalidOperationException("boom");
                }, ct);
                return 0;
            }, ct));

        Assert.Equal(1, facade.BeginCount);
        await facade.Tx.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await facade.Tx.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_Throws_DisposesAndResetsScope()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        // First scope opens a tx, flushes, then throws — the finally must dispose it and reset every scoped flag.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<int>(async () =>
            {
                db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "first@test.com", CreatedAt = DateTimeOffset.UtcNow });
                await sut.SaveChangesAsync(ct);
                throw new InvalidOperationException("boom");
            }, ct));

        // A second scope on the SAME sut must open a fresh transaction — proving the reset.
        await sut.ExecuteInTransactionAsync<int>(async () =>
        {
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "second@test.com", CreatedAt = DateTimeOffset.UtcNow });
            await sut.SaveChangesAsync(ct);
            return 0;
        }, ct);

        Assert.Equal(2, facade.BeginCount);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_ThrowingAfterCommitHook_DoesNotRollBackCommittedTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;

        // The work flushes (opening + committing the tx), then an after-commit hook throws. The commit
        // already happened, so the failure must NOT trigger a rollback of committed data — and the hook's
        // own exception must surface, not a rollback-after-commit error.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<int>(() =>
            {
                db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "u@test.com", CreatedAt = DateTimeOffset.UtcNow });
                sut.OnAfterCommit(_ => throw new InvalidOperationException("hook failed"));
                return Task.FromResult(0);
            }, ct));

        Assert.Equal("hook failed", ex.Message);
        await facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await facade.Tx.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await facade.Tx.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RolledBackCommandHook_DoesNotLeakToNextCommand()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var hookRuns = 0;

        // First command registers an after-commit hook then throws → rolled back, hook must be discarded.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<int>(() =>
            {
                sut.OnAfterCommit(_ => { hookRuns++; return Task.CompletedTask; });
                throw new InvalidOperationException("boom");
            }, ct));

        // A second command on the SAME scoped unit of work commits — the rolled-back command's hook must
        // not leak into it.
        await sut.ExecuteInTransactionAsync(() =>
        {
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "u@test.com", CreatedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(0);
        }, ct);

        Assert.Equal(0, hookRuns);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_RollbackFailure_DoesNotMaskOriginalException()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new UnitOfWork(db, new AuditContext(), _auditLogger, _currentUser, new CommandTransactionCoordinator());
        var facade = db.Facade;
        facade.Tx.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("rollback failed")));

        // The handler throws (rollback trigger); the rollback itself then fails. The ORIGINAL handler
        // exception must propagate, not the rollback's.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExecuteInTransactionAsync<int>(async () =>
            {
                db.Users.Add(new AppUser { Id = Guid.NewGuid(), Email = "u@test.com", CreatedAt = DateTimeOffset.UtcNow });
                await sut.SaveChangesAsync(ct);   // opens the tx
                throw new InvalidOperationException("original failure");
            }, ct));

        Assert.Equal("original failure", ex.Message);
        await facade.Tx.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }
}
