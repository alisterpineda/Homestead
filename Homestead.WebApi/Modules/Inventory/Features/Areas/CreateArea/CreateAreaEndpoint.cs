using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;

public static class CreateAreaEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (CreateAreaCommand request, CreateAreaHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(request, ct);
            return Results.Created($"/api/v1/inventory/areas/{response.Id}", response);
        })
        .WithName("CreateArea")
        .Produces<AreaResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
