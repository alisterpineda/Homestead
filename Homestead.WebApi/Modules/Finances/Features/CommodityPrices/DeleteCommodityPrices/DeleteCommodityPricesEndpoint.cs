using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;

public static class DeleteCommodityPricesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(
                new DeleteCommodityPricesCommand([id]), ct);
            return response.DeletedCount == 1 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCommodityPrice")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/bulk-delete", async (
            DeleteCommodityPricesCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult(
                    "At least one commodity price ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteCommodityPrices")
        .Produces<DeleteCommodityPricesResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
