using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.EmptyTrash;

public static class EmptyTrashEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/empty-trash", async (ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new EmptyTrashCommand(), ct);
            return Results.Ok(response);
        })
        .WithName("EmptyTrash")
        .Produces<EmptyTrashResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
