using MediatR;

namespace Homestead.WebApi.Common;

/// <summary>
/// Marker for mutation requests. Formalizes the existing <c>*Command</c> vs <c>*Query</c>
/// convention and gates the persistence/audit commit in <see cref="UnitOfWorkBehavior{TRequest,TResponse}"/>:
/// only requests marked <see cref="ICommand"/> commit after the handler runs. Queries skip the
/// commit entirely and are therefore guaranteed read-only.
/// </summary>
public interface ICommand;

/// <inheritdoc cref="ICommand"/>
public interface ICommand<out TResponse> : ICommand, IRequest<TResponse>;
