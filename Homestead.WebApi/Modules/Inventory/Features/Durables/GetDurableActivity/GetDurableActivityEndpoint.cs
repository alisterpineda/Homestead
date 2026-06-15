using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurableActivity;

public static class GetDurableActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/activity", async (Guid id,
            [AsParameters] PaginatedRequest paging, ISender sender, CancellationToken ct) =>
        {
            var query = new GetDurableActivityQuery
            {
                Id = id,
                Page = paging.Page,
                PageSize = paging.PageSize,
            };
            var response = await sender.Send(query, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetDurableActivity")
        .Produces<PaginatedResponse<GetDurableActivityResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
