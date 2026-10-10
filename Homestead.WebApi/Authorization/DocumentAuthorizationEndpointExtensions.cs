using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace Homestead.WebApi.Authorization;

/// <summary>
/// Marks an endpoint as authorizing a single document taken from a route value. The filter does the
/// authorizing; this metadata exists so the coverage guard test can see which endpoints carry it.
/// </summary>
public sealed record DocumentAuthorizationMetadata(OperationAuthorizationRequirement Operation, string RouteParameter);

public static class DocumentAuthorizationEndpointExtensions
{
    /// <summary>
    /// Authorizes the signed-in user for <paramref name="operation"/> on the document whose id is the
    /// <paramref name="routeParameter"/> route value, before the endpoint runs. Fails closed with
    /// <see cref="AccessDeniedException"/> when the route value is missing or not a Guid, and when
    /// authorization is denied. Sits beside <c>.RequireAuthorization(policy)</c> so a reviewer sees both.
    /// </summary>
    public static TBuilder RequireDocumentAuthorization<TBuilder>(
        this TBuilder builder, OperationAuthorizationRequirement operation, string routeParameter = "documentId")
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new DocumentAuthorizationMetadata(operation, routeParameter));
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.GetRouteValue(routeParameter)?.ToString(), out var documentId))
                throw new AccessDeniedException();

            await http.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeDocumentAsync(http.User, documentId, operation);
            return await next(context);
        });
    }
}
