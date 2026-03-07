using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public static class EmptyTrashEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/empty-trash", async (EmptyTrashHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(new EmptyTrashRequest(), ct);
            return Results.Ok(response);
        })
        .WithName("EmptyTrash")
        .Produces<EmptyTrashResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
