using Homestead.WebApi.Common;
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
            // Document authorization is an HTTP-request concern: it reads the signed-in principal.
            // Internal job commands deliberately omit IRequireDocumentAuthorization (authorization
            // already happened at enqueue). Guard against a future job command carrying the marker by
            // mistake — fail closed with a 403 rather than NRE on the null HttpContext.
            var user = httpContextAccessor.HttpContext?.User
                ?? throw new AccessDeniedException();
            await authorizationService.AuthorizeDocumentAsync(
                user, authRequest.DocumentId, authRequest.RequiredOperation);
        }

        return await next();
    }
}
