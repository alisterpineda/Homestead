using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

/// <summary>
/// Shared builder for the InMemory <see cref="AppDbContext"/> options used by the transaction-seam unit
/// tests (the recording/ambient facades below read the lifecycle; the InMemory store backs SaveChanges).
/// </summary>
internal static class TestDbContextOptions
{
    public static DbContextOptions<AppDbContext> NewInMemory() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
}

/// <summary>
/// A <see cref="TestAppDbContext"/> whose <see cref="DatabaseFacade"/> reports an open ambient
/// transaction, used to exercise the join branch of <see cref="WebApi.Common.UnitOfWork"/> /
/// <see cref="WebApi.Common.CommandTransactionCoordinator.TryBeginScope"/> (the InMemory provider always
/// reports <c>CurrentTransaction == null</c>, so the branch is otherwise unreachable in a unit test).
/// </summary>
internal sealed class AmbientTransactionDbContext(DbContextOptions<AppDbContext> options)
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

/// <summary>
/// A <see cref="TestAppDbContext"/> whose <see cref="DatabaseFacade"/> records
/// <c>BeginTransactionAsync</c> calls and exposes the begun transaction as
/// <see cref="DatabaseFacade.CurrentTransaction"/> (the InMemory provider keeps it null even after a
/// begin). The real InMemory store still backs <c>SaveChangesAsync</c> — only the transaction lifecycle
/// is observed — so lazy-open / commit / rollback / dispose can be asserted in a unit test.
/// </summary>
internal sealed class RecordingTransactionDbContext(DbContextOptions<AppDbContext> options)
    : TestAppDbContext(options)
{
    private RecordingFacade? _database;
    public RecordingFacade Facade => (RecordingFacade)Database;
    public override DatabaseFacade Database => _database ??= new RecordingFacade(this);

    internal sealed class RecordingFacade : DatabaseFacade
    {
        public int BeginCount { get; private set; }
        public IDbContextTransaction Tx { get; }
        private IDbContextTransaction? _current;

        public RecordingFacade(DbContext context) : base(context)
        {
            Tx = Substitute.For<IDbContextTransaction>();
            // Clear CurrentTransaction on dispose (mirrors a real provider) so a second command scope on
            // the same SUT sees no ambient tx and opens a fresh one. When/Do keeps this a side effect
            // only — it is not counted as a received DisposeAsync call.
            Tx.When(t => t.DisposeAsync()).Do(_ => _current = null);
        }

        public override IDbContextTransaction? CurrentTransaction => _current;

        public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        {
            BeginCount++;
            _current = Tx;   // visible as CurrentTransaction post-begin (like Npgsql)
            return Task.FromResult(Tx);
        }
    }
}
