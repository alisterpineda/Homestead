using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;

public static class MergeBrandEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/merge", async (Guid id,
            MergeBrandRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new MergeBrandCommand { SourceId = id, TargetId = body.TargetId };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("MergeBrand")
        .Produces<BrandResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
