using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;

public static class BulkDeleteUsersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteUsersCommand request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one user ID must be provided.");

            try
            {
                var response = await sender.Send(request, ct);
                return Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("BulkDeleteUsers")
        .Produces<BulkDeleteUsersResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
