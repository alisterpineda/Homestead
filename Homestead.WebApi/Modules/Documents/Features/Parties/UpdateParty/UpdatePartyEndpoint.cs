using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.UpdateParty;

public static class UpdatePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdatePartyRequest request,
            UpdatePartyHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(id, request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateParty")
        .Produces<UpdatePartyResponse>()
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
