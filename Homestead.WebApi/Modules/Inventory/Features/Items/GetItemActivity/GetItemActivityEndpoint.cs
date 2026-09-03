using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.GetItemActivity;

public static class GetItemActivityEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/activity", async (Guid id,
            [AsParameters] PaginatedRequest paging, bool? notesOnly, string? filter,
            ISender sender, CancellationToken ct) =>
        {
            var query = new GetItemActivityQuery
            {
                Id = id,
                Page = paging.Page,
                PageSize = paging.PageSize,
                NotesOnly = notesOnly ?? false,
                Filter = filter,
            };
            var response = await sender.Send(query, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        })
        .WithName("GetItemActivity")
        .Produces<PaginatedResponse<GetItemActivityResponse>>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
