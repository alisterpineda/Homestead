using FluentValidation;
using MediatR;

namespace Homestead.WebApi.Common;

public class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
            await validator.ValidateAndThrowAsync(request, cancellationToken);

        return await next();
    }
}
