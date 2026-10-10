using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public static class GetItemActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/activity", async (Guid id,
            [AsParameters] PaginatedRequest paging, GetItemActivityHandler handler, CancellationToken ct) =>
        {
            var query = new GetItemActivityQuery
            {
                Id = id,
                Page = paging.Page,
                PageSize = paging.PageSize,
            };
            var response = await handler.Handle(query, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItemActivity")
        .Produces<PaginatedResponse<GetItemActivityResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
