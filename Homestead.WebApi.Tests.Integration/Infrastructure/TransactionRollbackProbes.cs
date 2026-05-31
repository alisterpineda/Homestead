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
    /// Reproduces the <c>DeleteGroupHandler</c> shape under lazy-tx: open the command transaction via
    /// <see cref="IUnitOfWork.EnsureTransactionAsync"/>, run a change-tracker-bypassing
    /// <c>ExecuteUpdateAsync</c> against a real table, stage a tracked write, then throw. Proves the seam
    /// makes the bulk update atomic with the failure — without <c>EnsureTransactionAsync</c> the
    /// <c>ExecuteUpdate</c> would autocommit independently and survive the rollback.
    /// </summary>
    public sealed record ExecuteUpdateRollbackProbeCommand(Guid GroupId, string MarkerName) : ICommand<Unit>;

    public sealed class ExecuteUpdateRollbackProbeHandler(
        AppDbContext db,
        IUnitOfWork unitOfWork)
        : IRequestHandler<ExecuteUpdateRollbackProbeCommand, Unit>
    {
        public async Task<Unit> Handle(ExecuteUpdateRollbackProbeCommand request, CancellationToken cancellationToken)
        {
            // Open the lazy command transaction before the bulk update, so both land in it.
            await unitOfWork.EnsureTransactionAsync(cancellationToken);

            await db.Groups
                .Where(g => g.Id == request.GroupId)
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, request.MarkerName), cancellationToken);

            // A tracked write that shares the same transaction.
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                Email = $"probe-{Guid.NewGuid():N}@test.local",
                DisplayName = "execute-update rollback probe",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            throw new InvalidOperationException(
                "rollback probe: intentional failure after ExecuteUpdate + tracked write");
        }
    }
}
