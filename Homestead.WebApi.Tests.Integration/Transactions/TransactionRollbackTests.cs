using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Homestead.WebApi.Tests.Integration.Infrastructure.TransactionRollbackProbes;

namespace Homestead.WebApi.Tests.Integration.Transactions;

/// <summary>
/// Proves the request-scoped transaction (opened by <c>UnitOfWorkBehavior</c> via
/// <c>UnitOfWork.ExecuteInTransactionAsync</c>) commits exactly once and rolls everything back on
/// failure — including writes a handler already flushed mid-handler and jobs it enqueued. Real rollback
/// needs a relational provider, so this lives in the integration suite (InMemory ignores transactions).
/// </summary>
public class TransactionRollbackTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FailedCommand_RollsBackMidHandlerFlush_OfEntityAndAuditRow()
    {
        var entityId = Guid.NewGuid();

        // Dispatch the probe through the real pipeline. It stages an audited user, flushes mid-handler,
        // then throws — so the flush happened inside the transaction but the transaction never commits.
        using (var scope = CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.Send(new AuditedRollbackProbeCommand(entityId), CT));
        }

        // Fresh scope (new connection): neither the entity nor its audit row may have survived.
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.Id == entityId, CT),
                "the mid-handler-flushed entity must be rolled back by the failed command's transaction");
            Assert.False(await db.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityId == entityId, CT),
                "the audit row flushed alongside the entity must be rolled back too");
        }
    }

    [Fact]
    public async Task FailedCommand_RollsBackEnqueuedJob()
    {
        var marker = Guid.NewGuid();

        // The probe enqueues a job (which flushes into the transaction and defers its processor signal to
        // after-commit), then throws. The rollback must take the job with it and the signal must not fire.
        using (var scope = CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.Send(new EnqueueRollbackProbeCommand(marker), CT));
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.ProcessingJobs.AsNoTracking().AnyAsync(j => j.EntityId == marker, CT),
                "the enqueued job must be rolled back by the failed command's transaction");
        }
    }
}
