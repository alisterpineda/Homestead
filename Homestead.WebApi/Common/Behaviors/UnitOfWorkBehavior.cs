using MediatR;

namespace Homestead.WebApi.Common;

/// <summary>
/// Persistence/audit chokepoint for the MediatR pipeline. After the handler runs, commits the work
/// for any <see cref="ICommand"/> through <see cref="IUnitOfWork"/> (idempotent — a no-op when a
/// handler already saved), then drains post-commit side effects. The <see cref="ICommand"/> marker is
/// the sole commit gate: queries are not <see cref="ICommand"/> and skip the commit entirely, so they
/// are guaranteed read-only.
/// </summary>
public class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
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

        // The ICommand marker is the sole commit gate: commit every command through the single
        // chokepoint (idempotent — a no-op when the handler already saved), then drain true post-commit
        // side effects (hooks) only after the durable commit. Queries are not ICommand, so they never
        // commit and are therefore guaranteed read-only.
        if (request is ICommand)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.RunAfterCommitHooksAsync(cancellationToken);
        }

        return response;
    }
}
