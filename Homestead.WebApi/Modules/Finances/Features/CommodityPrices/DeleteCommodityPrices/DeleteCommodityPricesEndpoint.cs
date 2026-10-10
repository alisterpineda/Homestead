using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.DeleteCommodityPrices;

public static class DeleteCommodityPricesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, DeleteCommodityPricesHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(
                new DeleteCommodityPricesCommand([id]), ct);
            return response.DeletedCount == 1 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCommodityPrice")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);

        group.MapPost("/bulk-delete", async (
            DeleteCommodityPricesCommand request, DeleteCommodityPricesHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult(
                    "At least one commodity price ID must be provided.");

            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteCommodityPrices")
        .Produces<DeleteCommodityPricesResponse>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
