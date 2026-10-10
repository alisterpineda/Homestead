using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Test-only probe commands used to prove the request-scoped transaction rolls everything back on
/// failure. <see cref="HomesteadApiFactory"/> registers the handlers and maps one POST endpoint per probe
/// under <see cref="RoutePrefix"/>, on a group carrying <c>.WithCommandTransaction()</c> — so a probe
/// request runs through the real <see cref="CommandTransactionFilter"/>, which opens the transaction, and
/// the deliberate throw disposes it without committing. The endpoints are unauthenticated: they exist
/// only in the test host.
/// </summary>
public static class TransactionRollbackProbes
{
    public const string RoutePrefix = "/__test/probes";

    /// <summary>
    /// What a probe request answers once its deliberate <see cref="RollbackProbeException"/> has passed back
    /// through the transaction filter. No other failure maps to it: a probe that broke before reaching its
    /// throw (a failed insert, a missing registration) answers 500, so a test asserting this status knows
    /// the probe's writes really happened and were then rolled back, rather than never made.
    /// </summary>
    public const int ProbeFailedStatusCode = StatusCodes.Status418ImATeapot;
    public const string AuditedRollbackRoute = RoutePrefix + "/audited-rollback";
    public const string EnqueueRollbackRoute = RoutePrefix + "/enqueue-rollback";
    public const string ExecuteUpdateRollbackRoute = RoutePrefix + "/execute-update-rollback";
    public const string ExecuteDeleteNoEnsureRollbackRoute = RoutePrefix + "/execute-delete-rollback";
    public const string DirectSaveChangesNoEnsureRollbackRoute = RoutePrefix + "/direct-save-changes-rollback";

    public static void MapProbeEndpoints(IEndpointRouteBuilder endpoints)
    {
        var probes = endpoints.MapGroup(RoutePrefix).WithCommandTransaction();

        MapProbe<AuditedRollbackProbeCommand, AuditedRollbackProbeHandler>(
            probes, AuditedRollbackRoute, (h, c, ct) => h.Handle(c, ct));
        MapProbe<EnqueueRollbackProbeCommand, EnqueueRollbackProbeHandler>(
            probes, EnqueueRollbackRoute, (h, c, ct) => h.Handle(c, ct));
        MapProbe<ExecuteUpdateRollbackProbeCommand, ExecuteUpdateRollbackProbeHandler>(
            probes, ExecuteUpdateRollbackRoute, (h, c, ct) => h.Handle(c, ct));
        MapProbe<ExecuteDeleteNoEnsureRollbackProbeCommand, ExecuteDeleteNoEnsureRollbackProbeHandler>(
            probes, ExecuteDeleteNoEnsureRollbackRoute, (h, c, ct) => h.Handle(c, ct));
        MapProbe<DirectSaveChangesNoEnsureRollbackProbeCommand, DirectSaveChangesNoEnsureRollbackProbeHandler>(
            probes, DirectSaveChangesNoEnsureRollbackRoute, (h, c, ct) => h.Handle(c, ct));
    }

    // Same shape as a production endpoint: bind the command from the body, inject the handler, call Handle.
    private static void MapProbe<TCommand, THandler>(
        RouteGroupBuilder probes, string route, Func<THandler, TCommand, CancellationToken, Task> handle)
        where THandler : notnull =>
        probes.MapPost(route[RoutePrefix.Length..],
            async (TCommand command, THandler handler, CancellationToken ct) =>
            {
                await handle(handler, command, ct);
                return Results.NoContent();
            });

    /// <summary>The deliberate failure every probe throws after its writes.</summary>
    public sealed class RollbackProbeException(string message) : Exception(message);

    /// <summary>
    /// Stages an audited <see cref="AppUser"/>, flushes mid-handler via
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> (read-your-writes), then throws. Proves both the entity
    /// insert and its <c>auditing.audit_logs</c> row are rolled back even though they were already flushed.
    /// </summary>
    public sealed record AuditedRollbackProbeCommand(Guid EntityId);

    public sealed class AuditedRollbackProbeHandler(
        AppDbContext db,
        IAuditContext auditContext,
        IUnitOfWork unitOfWork)
    {
        public async Task Handle(AuditedRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            db.Users.Add(new AppUser
            {
                Id = request.EntityId,
                Email = $"probe-{request.EntityId:N}@test.local",
                DisplayName = "rollback probe",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            auditContext.Created("AppUser", request.EntityId);

            // Mid-handler flush: writes the user + audit row INTO the open transaction (not committed).
            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw new RollbackProbeException("rollback probe: intentional failure after mid-handler flush");
        }
    }

    /// <summary>
    /// Enqueues a job (tagged with <see cref="EnqueueRollbackProbeCommand.Marker"/>) then throws. Proves
    /// the enqueue participates in the command transaction: the job row must not survive the rollback, and
    /// the deferred processor signal must never fire.
    /// </summary>
    public sealed record EnqueueRollbackProbeCommand(Guid Marker);

    public sealed class EnqueueRollbackProbeHandler(IJobQueue jobQueue)
    {
        public async Task Handle(EnqueueRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            await jobQueue.EnqueueAsync(
                ProcessingJobType.UploadDocument,
                opts =>
                {
                    opts.EntityType = ProcessingEntityType.Document;
                    opts.EntityId = request.Marker;
                },
                cancellationToken);

            throw new RollbackProbeException("rollback probe: intentional failure after enqueue");
        }
    }

    /// <summary>
    /// Reproduces the <c>DeleteGroupHandler</c> shape under by-construction atomicity: run a
    /// change-tracker-bypassing <c>ExecuteUpdateAsync</c> against a real table with NO manual transaction
    /// call, flush a tracked write into the same transaction, then throw. Proves
    /// <c>CommandWriteCommandInterceptor</c> opens the command transaction before the bulk update reaches
    /// the database and re-binds it onto the command transaction — so both the bulk update and the flushed
    /// tracked write roll back with the failure. Without the interceptor the <c>ExecuteUpdate</c> would
    /// autocommit independently and survive the rollback.
    /// </summary>
    public sealed record ExecuteUpdateRollbackProbeCommand(Guid GroupId, string MarkerName);

    public sealed class ExecuteUpdateRollbackProbeHandler(AppDbContext db)
    {
        public async Task Handle(ExecuteUpdateRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            // No manual EnsureTransaction call — the interceptor opens the command tx before this bulk
            // update reaches the database, so it lands atomically.
            await db.Groups
                .Where(g => g.Id == request.GroupId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, request.MarkerName), cancellationToken);

            // A tracked write FLUSHED into the same command tx (so its rollback is genuinely exercised, not
            // left un-flushed in the change tracker). The SavingChanges interceptor keeps it in the open tx.
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                Email = $"probe-{Guid.NewGuid():N}@test.local",
                DisplayName = "execute-update rollback probe",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);

            throw new RollbackProbeException(
                "rollback probe: intentional failure after ExecuteUpdate + flushed tracked write");
        }
    }

    /// <summary>
    /// Runs a change-tracker-bypassing <c>ExecuteDeleteAsync</c> against a real row with NO manual
    /// transaction call, then throws. Proves <c>CommandWriteCommandInterceptor</c> makes the bulk delete
    /// atomic with the command by construction: the deleted row must survive the rollback. Without the
    /// interceptor the <c>ExecuteDelete</c> would autocommit on its own connection and the row would vanish.
    /// </summary>
    public sealed record ExecuteDeleteNoEnsureRollbackProbeCommand(Guid GroupId);

    public sealed class ExecuteDeleteNoEnsureRollbackProbeHandler(AppDbContext db)
    {
        public async Task Handle(ExecuteDeleteNoEnsureRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            await db.Groups
                .Where(g => g.Id == request.GroupId)
                .ExecuteDeleteAsync(cancellationToken);

            throw new RollbackProbeException(
                "rollback probe: intentional failure after ExecuteDelete");
        }
    }

    /// <summary>
    /// Stages an <see cref="AppUser"/> and flushes it with a direct <c>db.SaveChangesAsync</c> (bypassing
    /// <see cref="IUnitOfWork.SaveChangesAsync"/>) — NO manual transaction call — then throws. Proves
    /// <c>CommandSaveChangesInterceptor</c> opens the command tx before EF builds its own autocommit batch,
    /// so even a direct save lands inside the command transaction and rolls back with the failure.
    /// </summary>
    public sealed record DirectSaveChangesNoEnsureRollbackProbeCommand(Guid EntityId);

    public sealed class DirectSaveChangesNoEnsureRollbackProbeHandler(AppDbContext db)
    {
        public async Task Handle(DirectSaveChangesNoEnsureRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            db.Users.Add(new AppUser
            {
                Id = request.EntityId,
                Email = $"probe-{request.EntityId:N}@test.local",
                DisplayName = "direct-save rollback probe",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            // Direct save (not unitOfWork.SaveChangesAsync): the SavingChanges interceptor must open the
            // command tx so this flush is atomic with the command, not an independent autocommit.
            await db.SaveChangesAsync(cancellationToken);

            throw new RollbackProbeException(
                "rollback probe: intentional failure after direct db.SaveChangesAsync");
        }
    }
}
