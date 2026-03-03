using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Features.Documents.v1.UpdateParty;

public static class UpdatePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdatePartyRequest request,
            UpdatePartyHandler handler, CancellationToken ct) =>
        {
            var command = new UpdatePartyCommand
            {
                Id = id,
                Name = request.Name,
                Description = request.Description
            };
            var response = await handler.HandleAsync(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateParty")
        .Produces<UpdatePartyResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
