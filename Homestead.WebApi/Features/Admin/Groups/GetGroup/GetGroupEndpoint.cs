using MediatR;

namespace Homestead.WebApi.Features.Admin.Groups.GetGroup;

public static class GetGroupEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetGroupQuery(id), ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetGroup")
        .Produces<GroupDetailResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
