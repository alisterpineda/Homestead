using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;

public static class GetUsersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetUsersQuery request,
            GetUsersHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetUsers")
        .Produces<PaginatedResponse<UserResponse>>();
    }
}
