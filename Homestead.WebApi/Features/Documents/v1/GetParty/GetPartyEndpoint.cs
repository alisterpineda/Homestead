using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetParty;

public static class GetPartyEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var request = new GetPartyRequest(id);
            var response = await sender.Send(request, ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetParty")
        .Produces<GetPartyResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
