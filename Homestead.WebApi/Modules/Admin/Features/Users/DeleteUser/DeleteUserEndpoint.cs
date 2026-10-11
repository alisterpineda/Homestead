using Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;

namespace Homestead.WebApi.Modules.Admin.Features.Users.DeleteUser;

public static class DeleteUserEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, BulkDeleteUsersHandler handler, CancellationToken ct) =>
        {
            var request = new BulkDeleteUsersCommand([id]);
            var response = await handler.HandleAsync(request, ct);
            return response.DeletedCount > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteUser")
        .Produces(StatusCodes.Status204NoContent);
    }
}
