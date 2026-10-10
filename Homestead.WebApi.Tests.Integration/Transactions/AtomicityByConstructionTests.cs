using System.Net.Http.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Admin.Features.Groups.DeleteGroup;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Homestead.WebApi.Tests.Integration.Infrastructure.TransactionRollbackProbes;

namespace Homestead.WebApi.Tests.Integration.Transactions;

/// <summary>
/// Proves atomicity holds <em>by construction</em>: a command handler can do a change-tracker-bypassing
/// write (<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>/direct <c>db.SaveChangesAsync</c>) with no manual
/// transaction call and still roll back with the command, while the same write outside a command scope
/// keeps autocommitting. The coverage comes from the EF interceptors
/// (<c>CommandSaveChangesInterceptor</c> + <c>CommandWriteCommandInterceptor</c>) firing per-request under
/// the real production DbContext wiring — <see cref="Infrastructure.HomesteadApiFactory"/> performs no
/// DbContext swap, so this exercises the exact non-pooled <c>AddDbContext</c> + interceptor registration
/// from <c>Program.cs</c>. Real transaction semantics need a relational provider, so these live in the
/// integration suite.
/// </summary>
public class AtomicityByConstructionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteUpdate_NoManualEnsure_ThenThrows_IsReverted()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Original Name");

        var response = await CreateAnonymousClient().PostAsJsonAsync(
            ExecuteUpdateRollbackRoute, new ExecuteUpdateRollbackProbeCommand(groupId, "Mutated Name"), CT);
        Assert.Equal(ProbeFailedStatusCode, (int)response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var group = await db.Groups.AsNoTracking().FirstAsync(g => g.Id == groupId, CT);
            Assert.Equal("Original Name", group.Name);
        }
    }

    [Fact]
    public async Task ExecuteDelete_NoManualEnsure_ThenThrows_RowSurvives()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Survives Rollback");

        var response = await CreateAnonymousClient().PostAsJsonAsync(
            ExecuteDeleteNoEnsureRollbackRoute, new ExecuteDeleteNoEnsureRollbackProbeCommand(groupId), CT);
        Assert.Equal(ProbeFailedStatusCode, (int)response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // The bulk delete ran inside the command tx (interceptor) and rolled back, so the row is intact.
            Assert.True(await db.Groups.AsNoTracking().AnyAsync(g => g.Id == groupId, CT));
        }
    }

    [Fact]
    public async Task DirectSaveChanges_NoManualEnsure_ThenThrows_IsNotPersisted()
    {
        var entityId = Guid.NewGuid();

        var response = await CreateAnonymousClient().PostAsJsonAsync(
            DirectSaveChangesNoEnsureRollbackRoute, new DirectSaveChangesNoEnsureRollbackProbeCommand(entityId), CT);
        Assert.Equal(ProbeFailedStatusCode, (int)response.StatusCode);

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // The SavingChanges interceptor opened the command tx, so the direct save rolled back too.
            Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.Id == entityId, CT));
        }
    }

    [Fact]
    public async Task BulkWrite_HappyPath_Commits()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "To Delete");

        // DeleteGroupCommand does a change-tracker-bypassing ExecuteUpdate plus a tracked Groups.Remove and
        // flush. On the happy path the interceptor-opened command tx commits both.
        using (var scope = CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<DeleteGroupHandler>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var deleted = await unitOfWork.ExecuteInTransactionAsync(
                () => handler.Handle(new DeleteGroupCommand(groupId), CT), CT);
            Assert.True(deleted);
        }

        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Groups.AsNoTracking().AnyAsync(g => g.Id == groupId, CT));
        }
    }

    [Fact]
    public async Task ExecuteUpdate_OutsideCommandScope_AutocommitsImmediately()
    {
        var adminClient = await CreateAdminClientAsync();
        var groupId = await CreateGroupAsync(adminClient, "Original Name");

        // A bulk write run in a plain scope (no command transaction) mirrors the JobRunner
        // background path: the interceptor stays a no-op (no command tx is opened), so the write autocommits.
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Groups
            .Where(g => g.Id == groupId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, "Background Updated"), CT);

        // No command scope was active, so the interceptor must NOT have opened a transaction.
        Assert.Null(db.Database.CurrentTransaction);

        // Visible from a SECOND scope/connection immediately → it committed, it was not held uncommitted.
        using var scope2 = CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = await db2.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => g.Name)
            .FirstAsync(CT);
        Assert.Equal("Background Updated", name);
    }
}
