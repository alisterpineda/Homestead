namespace Homestead.WebApi.Common;

public interface ICommandHandler<in TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}