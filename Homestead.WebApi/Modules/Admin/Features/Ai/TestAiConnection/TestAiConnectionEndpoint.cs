namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

public static class TestAiConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // The provider key rides an optional query parameter rather than a body: an optional JSON body
        // needs EmptyBodyBehavior ceremony, and a bare POST should still probe the default provider.
        group.MapPost("/test-connection", async (string? providerKey, TestAiConnectionHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new TestAiConnectionCommand(providerKey), ct));
        })
        .WithName("TestAiConnection")
        .Produces<TestAiConnectionResponse>();
    }
}
