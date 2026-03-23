using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.GetUsers;

public static class GetUsersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/", async ([AsParameters] GetUsersQuery request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("GetUsers")
        .Produces<PaginatedResponse<UserResponse>>();
    }
}
