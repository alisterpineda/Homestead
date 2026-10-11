using System.Net.Http.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Admin.Features.Schedules.DeleteProcessingSchedule;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Tests.Integration.Infrastructure.TransactionRollbackProbes;

namespace Homestead.WebApi.Tests.Integration.Transactions;

/// <summary>
/// Validates that change-tracker-bypassing writes (<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>) are atomic
/// with the rest of the command — now provided by the command-transaction interceptors (by construction),
/// with the lazy-open seam governing the tracked-write side. Real transaction semantics need a relational
/// provider, so these live in the integration suite (InMemory ignores them).
/// </summary>
public class LazyTransactionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task FailedCommand_RollsBackExecuteUpdate_AtomicWithTrackedWrite()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Original Name");

        // The probe bulk-updates the group name (no manual transaction call), stages a tracked write, then
        // throws. CommandWriteCommandInterceptor makes the bulk update atomic by construction, so it must
        // roll back with the failed command.
        var response = await CreateAnonymousClient().PostAsJsonAsync(
            ExecuteUpdateRollbackRoute, new ExecuteUpdateRollbackProbeCommand(groupId, "Mutated Name"), CT);
        Assert.Equal(ProbeFailedStatusCode, (int)response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var group = await db.Groups.AsNoTracking().FirstAsync(g => g.Id == groupId, CT);
            Assert.Equal("Original Name", group.Name);

            // The tracked probe write must have rolled back too — nothing from the failed command survives.
            Assert.False(await db.Users.AsNoTracking()
                .AnyAsync(u => u.DisplayName == "execute-update rollback probe", CT));
        }
    }

    [Fact]
    public async Task DeleteProcessingSchedule_RemovesRow_OnSuccess()
    {
        // Behavior-preservation check: the single ExecuteDelete still deletes on the happy path. The
        // interceptor opens the command tx as the delete reaches the DB and the unit of work commits it.
        var scheduleId = await SeedScheduleAsync();

        using (var scope = CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<DeleteProcessingScheduleHandler>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var deleted = await unitOfWork.ExecuteInTransactionAsync(
                () => handler.HandleAsync(scheduleId, CT), CT);
            Assert.True(deleted);
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.ProcessingSchedules.AsNoTracking().AnyAsync(s => s.Id == scheduleId, CT));
        }
    }
}
