using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Homestead.WebApi.Common;

/// <summary>
/// Opens the command's owned transaction (via <see cref="CommandTransactionCoordinator"/>) before EF
/// builds its own autocommit transaction for a <c>SaveChanges</c> batch — so a direct
/// <c>db.SaveChangesAsync</c> that bypasses <see cref="UnitOfWork.SaveChangesAsync"/> (e.g.
/// <see cref="Processing.JobQueue"/>) still lands inside the command transaction. Scope-gated by the
/// coordinator: a no-op outside a command scope, so queries and background scopes keep autocommitting.
/// </summary>
public sealed class CommandSaveChangesInterceptor(CommandTransactionCoordinator coordinator)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            // Rare sync path (production is all-async). BEGIN is protocol-level on Npgsql and ASP.NET Core
            // has no sync context, so blocking here is safe.
            coordinator.EnsureStartedAsync(eventData.Context.Database, CancellationToken.None)
                .GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            await coordinator.EnsureStartedAsync(eventData.Context.Database, cancellationToken);
        return result;
    }
}

/// <summary>
/// Opens the command's owned transaction before a change-tracker-bypassing bulk write
/// (<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, which execute as <c>NonQuery</c> on Npgsql) reaches the
/// database, then re-binds <see cref="DbCommand.Transaction"/> to it. EF binds <c>command.Transaction</c>
/// (to null) at <c>CreateDbCommand</c>, <em>before</em> this fires, so opening the transaction is not
/// enough — without the re-bind the bulk write would still autocommit on its own connection, outside the
/// command transaction, and survive a rollback. Mutating the command here is the supported, intended use
/// of a command interceptor. <c>SaveChanges</c>-generated INSERT/UPDATE/DELETE need no command interceptor:
/// <see cref="CommandSaveChangesInterceptor"/> already opened the transaction, and on Npgsql
/// <c>INSERT … RETURNING</c> runs through the reader path anyway. We deliberately do not hook
/// <c>ReaderExecuting</c>/<c>ScalarExecuting</c> — that would force-open a transaction for ordinary
/// <c>SELECT</c>s, which must stay read-only and connection-light.
/// </summary>
public sealed class CommandWriteCommandInterceptor(CommandTransactionCoordinator coordinator)
    : DbCommandInterceptor
{
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is null)
            return result;   // connection-only command — don't touch

        // Rare sync path (production is all-async); blocking is safe (see CommandSaveChangesInterceptor).
        coordinator.EnsureStartedAsync(eventData.Context.Database, CancellationToken.None)
            .GetAwaiter().GetResult();
        // Re-bind onto the owned tx (see NonQueryExecutingAsync for the rationale).
        command.Transaction = eventData.Context.Database.CurrentTransaction?.GetDbTransaction();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return result;   // connection-only command — don't touch

        await coordinator.EnsureStartedAsync(eventData.Context.Database, cancellationToken);
        // EF bound command.Transaction (to null) at CreateDbCommand, before this fired — re-bind it to the
        // just-opened owned tx so the bulk write runs INSIDE it instead of autocommitting. Unconditional: it
        // is the fix when EF does not re-derive the transaction post-interception, and a harmless no-op when
        // it does.
        command.Transaction = eventData.Context.Database.CurrentTransaction?.GetDbTransaction();
        return result;
    }
}
