using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Persistence;
using Homestead.WebApi.Processing;
using MediatR;

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
}
