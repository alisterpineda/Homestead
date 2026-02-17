namespace HomeSuiteHome.WebApi.Common;

public interface IQueryHandler<in TRequest, TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}