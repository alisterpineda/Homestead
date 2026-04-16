using MediatR;

namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public static class GetStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetStatusQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetStatus")
        .Produces<GetStatusResponse>()
        .AllowAnonymous();
    }
}
