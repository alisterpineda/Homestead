using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public static class GetItemActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/activity", async (Guid id,
            [AsParameters] PaginatedRequest paging, GetItemActivityHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, paging, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItemActivity")
        .Produces<PaginatedResponse<GetItemActivityResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
