using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

public static class BulkDeletePartiesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeletePartiesCommand request, ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return Results.BadRequest("At least one party ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteParties")
        .Produces<BulkDeletePartiesResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
