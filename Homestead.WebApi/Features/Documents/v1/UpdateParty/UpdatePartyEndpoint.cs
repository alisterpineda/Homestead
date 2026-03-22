using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.UpdateParty;

public static class UpdatePartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdatePartyRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new UpdatePartyCommand
            {
                Id = id,
                Name = request.Name,
                Description = request.Description
            };
            var response = await sender.Send(command, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("UpdateParty")
        .Produces<UpdatePartyResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
