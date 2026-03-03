using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Identity.v1.GetUserInfo;

public static class GetUserInfoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/user-info", async (ICurrentUserAccessor currentUser, GetUserInfoHandler handler, CancellationToken ct) =>
        {
            var userId = currentUser.UserId;
            if (userId is null)
                return Results.Unauthorized();

            var response = await handler.HandleAsync(new GetUserInfoRequest(userId.Value), ct);
            return response is not null ? Results.Ok(response) : Results.Unauthorized();
        })
        .WithName("GetUserInfo")
        .Produces<GetUserInfoResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AuthPolicies.AuthenticatedOnly);
    }
}
