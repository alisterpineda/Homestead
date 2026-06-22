using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

public static class TestAiConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/test-connection", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new TestAiConnectionCommand(), ct));
        })
        .WithName("TestAiConnection")
        .Produces<TestAiConnectionResponse>();
    }
}
