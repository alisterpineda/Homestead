using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

/// <summary>
/// Directly exercises the transaction state-machine primitives extracted out of <see cref="UnitOfWork"/>.
/// These absorb the migrated <c>EnsureTransactionAsync_*</c> cases: the coordinator is now the single seam
/// the interceptors and the unit of work share, so its open/commit/rollback/scope semantics are pinned here.
/// </summary>
public class CommandTransactionCoordinatorTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private static DbContextOptions<AppDbContext> NewInMemoryOptions() => TestDbContextOptions.NewInMemory();

    [Fact]
    public void TryBeginScope_WhenTransactionAlreadyOpen_JoinsWithoutEnteringScope()
    {
        using var db = new AmbientTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        // An ambient transaction is open → join (return true) and do not increment depth.
        Assert.True(sut.TryBeginScope(db.Database));
        Assert.Equal(0, sut.Depth);
    }

    [Fact]
    public void TryBeginScope_WhenNoTransaction_EntersScopeAndIncrementsDepth()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        Assert.False(sut.TryBeginScope(db.Database));
        Assert.Equal(1, sut.Depth);
    }

    [Fact]
    public async Task EnsureStartedAsync_OutsideScope_IsNoOp()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        // No command scope active, so this must neither open a transaction nor throw.
        await sut.EnsureStartedAsync(db.Database, CT);

        Assert.Equal(0, db.Facade.BeginCount);
    }

    [Fact]
    public async Task EnsureStartedAsync_InsideScope_OpensTransactionOnce()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        sut.TryBeginScope(db.Database);   // enter scope
        await sut.EnsureStartedAsync(db.Database, CT);

        Assert.Equal(1, db.Facade.BeginCount);
        Assert.NotNull(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task EnsureStartedAsync_SecondCall_IsIdempotent()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        sut.TryBeginScope(db.Database);
        await sut.EnsureStartedAsync(db.Database, CT);
        await sut.EnsureStartedAsync(db.Database, CT);   // the _ownedTx guard must not re-begin

        Assert.Equal(1, db.Facade.BeginCount);
    }

    [Fact]
    public async Task EnsureStartedAsync_WhenCurrentTransactionAlreadyOpen_DoesNotBegin()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        sut.TryBeginScope(db.Database);                  // _inCommandScope = true (CurrentTransaction null)
        await db.Database.BeginTransactionAsync(CT);     // external begin: CurrentTransaction now non-null
        await sut.EnsureStartedAsync(db.Database, CT);   // the CurrentTransaction guard must short-circuit

        Assert.Equal(1, db.Facade.BeginCount);           // did NOT open a second transaction
    }

    [Fact]
    public async Task CommitAsync_CommitsThenReleasesOwnership()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        sut.TryBeginScope(db.Database);
        await sut.EnsureStartedAsync(db.Database, CT);

        await sut.CommitAsync(CT);
        await db.Facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());

        // Ownership released — a second commit is a no-op (it does not re-commit the transaction).
        await sut.CommitAsync(CT);
        await db.Facade.Tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackOnceAsync_RollsBackAtMostOnce()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();

        sut.TryBeginScope(db.Database);
        await sut.EnsureStartedAsync(db.Database, CT);

        await sut.RollbackOnceAsync();
        await sut.RollbackOnceAsync();   // the _rolledBack guard collapses this to a single rollback

        await db.Facade.Tx.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollbackOnceAsync_SwallowsRollbackFailure()
    {
        using var db = new RecordingTransactionDbContext(NewInMemoryOptions());
        var sut = new CommandTransactionCoordinator();
        db.Facade.Tx.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("rollback failed")));

        sut.TryBeginScope(db.Database);
        await sut.EnsureStartedAsync(db.Database, CT);

        // A failed rollback must not surface — it cannot be allowed to mask the original exception.
        await sut.RollbackOnceAsync();
        await db.Facade.Tx.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }
}
