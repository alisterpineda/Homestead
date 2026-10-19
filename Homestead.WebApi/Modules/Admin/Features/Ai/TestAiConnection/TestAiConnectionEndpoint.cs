namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

public static class TestAiConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/test-connection", async (TestAiConnectionHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("TestAiConnection")
        .Produces<TestAiConnectionResponse>();
    }
}
