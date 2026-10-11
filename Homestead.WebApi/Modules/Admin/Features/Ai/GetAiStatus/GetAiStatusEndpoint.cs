namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

public static class GetAiStatusEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/status", async (GetAiStatusHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetAiStatus")
        .Produces<List<AiProviderStatusResponse>>();
    }
}
