using Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Homestead.WebApi.Tests.Integration.Infrastructure.TransactionRollbackProbes;

namespace Homestead.WebApi.Tests.Integration.Transactions;

/// <summary>
/// Validates the lazy-transaction seams that keep change-tracker-bypassing writes
/// (<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>) atomic with the rest of the command. Real transaction
/// semantics need a relational provider, so these live in the integration suite (InMemory ignores them).
/// </summary>
public class LazyTransactionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FailedCommand_RollsBackExecuteUpdate_AtomicWithTrackedWrite()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Original Name");

        // The probe opens the lazy tx via EnsureTransactionAsync, bulk-updates the group name, stages a
        // tracked write, then throws. The bulk update must roll back with the failed command.
        using (var scope = CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.Send(new ExecuteUpdateRollbackProbeCommand(groupId, "Mutated Name"), CT));
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var group = await db.Groups.AsNoTracking().FirstAsync(g => g.Id == groupId, CT);
            Assert.Equal("Original Name", group.Name);
        }
    }

    [Fact]
    public async Task DeleteProcessingSchedule_RemovesRow_OnSuccess()
    {
        // Behavior-preservation check for the mandatory EnsureTransactionAsync added to the handler: the
        // single ExecuteDelete still deletes on the happy path (the trailing no-op SaveChanges commits it).
        var scheduleId = await SeedScheduleAsync();

        using (var scope = CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var deleted = await sender.Send(new DeleteProcessingScheduleCommand(scheduleId), CT);
            Assert.True(deleted);
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.ProcessingSchedules.AsNoTracking().AnyAsync(s => s.Id == scheduleId, CT));
        }
    }
}
