using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Tags.CreateTag;

public static class CreateTagEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateInventoryTagCommand request, CreateTagHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/inventory/tags/{response.Id}", response);
        })
        .WithName("CreateInventoryTag")
        .Produces<CreateInventoryTagResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
