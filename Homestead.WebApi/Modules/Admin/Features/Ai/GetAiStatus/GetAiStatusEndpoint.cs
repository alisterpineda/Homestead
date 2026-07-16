using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

public static class GetAiStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/status", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetAiStatusQuery(), ct));
        })
        .WithName("GetAiStatus")
        .Produces<List<AiProviderStatusResponse>>();
    }
}
