using Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.DeleteUser;

public static class DeleteUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var request = new BulkDeleteUsersCommand([id]);
                var response = await sender.Send(request, ct);
                return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .WithName("DeleteUser")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
