using MediatR;

namespace Homestead.WebApi.Common;

/// <summary>
/// Persistence/audit/transaction chokepoint adapter for the MediatR pipeline. Threads the audit actor,
/// then wraps every <see cref="ICommand"/> in a single database transaction via
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>: the handler runs, the work is flushed and
/// committed exactly once, and post-commit hooks drain afterwards. Any failure rolls the whole command
/// back. The <see cref="ICommand"/> marker is the sole transaction gate: queries are not
/// <see cref="ICommand"/>, run with no transaction, and are therefore guaranteed read-only.
/// </summary>
/// <remarks>
/// Registered innermost so authorization and validation run <em>outside</em> the transaction — a
/// rejected request never opens a transaction. This behavior is DbContext-free: the transaction
/// boundary lives entirely on <see cref="IUnitOfWork"/>.
/// </remarks>
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

        // Queries are not ICommand: they run with no transaction and are guaranteed read-only. Commands
        // run inside a single transaction that commits once and then drains post-commit hooks.
        if (request is not ICommand)
            return await next();

        return await unitOfWork.ExecuteInTransactionAsync(() => next(), cancellationToken);
    }
}
