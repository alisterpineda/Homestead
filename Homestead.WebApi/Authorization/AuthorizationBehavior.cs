using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Homestead.WebApi.Authorization;

public class AuthorizationBehavior<TRequest, TResponse>(
    IAuthorizationService authorizationService,
    IHttpContextAccessor httpContextAccessor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IRequireDocumentAuthorization authRequest)
        {
            var user = httpContextAccessor.HttpContext!.User;
            await authorizationService.AuthorizeDocumentAsync(
                user, authRequest.DocumentId, authRequest.RequiredOperation);
        }

        return await next();
    }
}
