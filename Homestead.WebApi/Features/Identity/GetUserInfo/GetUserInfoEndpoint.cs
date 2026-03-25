using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Identity.GetUserInfo;

public static class GetUserInfoEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/user-info", async (ICurrentUserAccessor currentUser, ISender sender, CancellationToken ct) =>
        {
            var userId = currentUser.UserId;
            if (userId is null)
                return Results.Unauthorized();

            var response = await sender.Send(new GetUserInfoQuery(userId.Value), ct);
            return response is not null ? Results.Ok(response) : Results.Unauthorized();
        })
        .WithName("GetUserInfo")
        .Produces<GetUserInfoResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AuthPolicies.AuthenticatedOnly);
    }
}
