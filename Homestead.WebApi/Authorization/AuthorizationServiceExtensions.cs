using System.Security.Claims;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Authorization;

public static class AuthorizationServiceExtensions
{
    public static async Task AuthorizeDocumentAsync(
        this IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        Guid documentId,
        OperationAuthorizationRequirement operation)
    {
        var result = await authorizationService.AuthorizeAsync(user, documentId, operation);
        if (!result.Succeeded)
            throw new ForbiddenAccessException();
    }
}
