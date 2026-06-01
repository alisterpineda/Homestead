using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Test-only probe commands used to prove the request-scoped transaction rolls everything back on
/// failure. They are registered in <see cref="HomesteadApiFactory"/> and dispatched via <c>ISender</c>,
/// so the real command pipeline (Authorization → Validation → UnitOfWork → handler) wraps them — the
/// <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> opens the transaction, and the deliberate throw
/// disposes it without committing.
/// </summary>
public static class TransactionRollbackProbes
{
    /// <summary>
    /// Stages an audited <see cref="AppUser"/>, flushes mid-handler via
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> (read-your-writes), then throws. Proves both the entity
    /// insert and its <c>auditing.audit_logs</c> row are rolled back even though they were already flushed.
    /// </summary>
    public sealed record AuditedRollbackProbeCommand(Guid EntityId) : ICommand<Unit>;

    public sealed class AuditedRollbackProbeHandler(
        AppDbContext db,
        IAuditContext auditContext,
        IUnitOfWork unitOfWork)
        : IRequestHandler<AuditedRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(AuditedRollbackProbeCommand request, CancellationToken cancellationToken)
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

            throw new InvalidOperationException("rollback probe: intentional failure after mid-handler flush");
        }
    }

    /// <summary>
    /// Enqueues a job (tagged with <see cref="EnqueueRollbackProbeCommand.Marker"/>) then throws. Proves
    /// the enqueue participates in the command transaction: the job row must not survive the rollback, and
    /// the deferred processor signal must never fire.
    /// </summary>
    public sealed record EnqueueRollbackProbeCommand(Guid Marker) : ICommand<Unit>;

    public sealed class EnqueueRollbackProbeHandler(IJobQueue jobQueue)
        : IRequestHandler<EnqueueRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(EnqueueRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            await jobQueue.EnqueueAsync(
                ProcessingJobType.UploadDocument,
                opts =>
                {
                    opts.EntityType = ProcessingEntityType.Document;
                    opts.EntityId = request.Marker;
                },
                cancellationToken);

            throw new InvalidOperationException("rollback probe: intentional failure after enqueue");
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
    public sealed record ExecuteUpdateRollbackProbeCommand(Guid GroupId, string MarkerName) : ICommand<Unit>;

    public sealed class ExecuteUpdateRollbackProbeHandler(AppDbContext db)
        : IRequestHandler<ExecuteUpdateRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(ExecuteUpdateRollbackProbeCommand request, CancellationToken cancellationToken)
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

            throw new InvalidOperationException(
                "rollback probe: intentional failure after ExecuteUpdate + flushed tracked write");
        }
    }

    /// <summary>
    /// Runs a change-tracker-bypassing <c>ExecuteDeleteAsync</c> against a real row with NO manual
    /// transaction call, then throws. Proves <c>CommandWriteCommandInterceptor</c> makes the bulk delete
    /// atomic with the command by construction: the deleted row must survive the rollback. Without the
    /// interceptor the <c>ExecuteDelete</c> would autocommit on its own connection and the row would vanish.
    /// </summary>
    public sealed record ExecuteDeleteNoEnsureRollbackProbeCommand(Guid GroupId) : ICommand<Unit>;

    public sealed class ExecuteDeleteNoEnsureRollbackProbeHandler(AppDbContext db)
        : IRequestHandler<ExecuteDeleteNoEnsureRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(ExecuteDeleteNoEnsureRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            await db.Groups
                .Where(g => g.Id == request.GroupId)
                .ExecuteDeleteAsync(cancellationToken);

            throw new InvalidOperationException(
                "rollback probe: intentional failure after ExecuteDelete");
        }
    }

    /// <summary>
    /// Stages an <see cref="AppUser"/> and flushes it with a direct <c>db.SaveChangesAsync</c> (bypassing
    /// <see cref="IUnitOfWork.SaveChangesAsync"/>) — NO manual transaction call — then throws. Proves
    /// <c>CommandSaveChangesInterceptor</c> opens the command tx before EF builds its own autocommit batch,
    /// so even a direct save lands inside the command transaction and rolls back with the failure.
    /// </summary>
    public sealed record DirectSaveChangesNoEnsureRollbackProbeCommand(Guid EntityId) : ICommand<Unit>;

    public sealed class DirectSaveChangesNoEnsureRollbackProbeHandler(AppDbContext db)
        : IRequestHandler<DirectSaveChangesNoEnsureRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(DirectSaveChangesNoEnsureRollbackProbeCommand request, CancellationToken cancellationToken)
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

            throw new InvalidOperationException(
                "rollback probe: intentional failure after direct db.SaveChangesAsync");
        }
    }
}
