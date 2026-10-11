using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.EmptyTrash;

public static class EmptyTrashEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/empty-trash", async (EmptyTrashHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("EmptyTrash")
        .Produces<EmptyTrashResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
