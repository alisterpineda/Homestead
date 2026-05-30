using Homestead.WebApi.Common.Auditing;
using MediatR;

namespace Homestead.WebApi.Common;

/// <summary>
/// Persistence/audit chokepoint for the MediatR pipeline. After the handler runs, commits the work
/// for any <see cref="ICommand"/> through <see cref="IUnitOfWork"/> (idempotent — a no-op when a
/// handler already saved), then drains post-commit side effects. Queries are not <see cref="ICommand"/>
/// and skip the commit entirely, so they are guaranteed read-only.
/// </summary>
public class UnitOfWorkBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IAuditContext auditContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Thread the explicit actor (internal job commands) before the handler runs, so any
        // SaveChangesAsync the handler calls itself attributes audit entries correctly. Set it for
        // every request (null when not IActorAttributed) so the scoped override never bleeds from a
        // previous send into a later, non-attributed one within the same scope.
        unitOfWork.SetActor(request is IActorAttributed actorAttributed ? actorAttributed.ActorUserId : null);

        var response = await next();

        // Commit through the single chokepoint for commands; the legacy fallback also commits modules
        // not yet migrated to the ICommand marker (Finances/Admin) that rely on this behavior to flush
        // audit. Queries never audit, so this never fires for a read — read-only holds. Either way,
        // drain true post-commit side effects (hooks) only after the durable commit.
        if (request is ICommand || auditContext.HasEntries)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.RunAfterCommitHooksAsync(cancellationToken);
        }

        return response;
    }
}
