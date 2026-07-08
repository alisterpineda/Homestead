using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.CreateTag;

public static class CreateTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateInventoryTagCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/inventory/tags/{response.Id}", response);
        })
        .WithName("CreateInventoryTag")
        .Produces<CreateInventoryTagResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
