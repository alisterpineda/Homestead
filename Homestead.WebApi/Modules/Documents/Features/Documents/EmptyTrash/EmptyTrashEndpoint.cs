using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;

public static class EmptyTrashEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/empty-trash", async (EmptyTrashHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new EmptyTrashCommand(), ct);
            return Results.Ok(response);
        })
        .WithName("EmptyTrash")
        .Produces<EmptyTrashResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
