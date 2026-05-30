namespace Homestead.WebApi.Common;

/// <summary>
/// Opt-in marker that lets a request carry the user the resulting audit entries should be
/// attributed to, rather than relying on the ambient <see cref="Authentication.ICurrentUserAccessor"/>.
/// HTTP commands omit it (they fall back to the signed-in user); internal job commands set it from
/// <c>job.CreatedByUserId</c> so background work is attributed to whoever initiated the job. Resolved by
/// <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/> as <c>(request as IActorAttributed)?.ActorUserId
/// ?? currentUser.UserId</c> — no <c>HttpContext</c> required, so it is unit-testable.
/// </summary>
public interface IActorAttributed
{
    Guid? ActorUserId { get; }
}
