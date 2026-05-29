using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.TestAiProviderConnection;

public static class TestAiProviderConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Ad-hoc test (create dialog): caller supplies the provider type + full config.
        group.MapPost("/test-connection", async (TestAiProviderConnectionRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new TestAiProviderConnectionCommand
            {
                ProviderType = request.ProviderType,
                Configuration = request.Configuration,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("TestAiProviderConnection")
        .Produces<TestConnectionResponse>();

        // Existing provider (edit sheet): a blank/masked key reuses the stored secret.
        group.MapPost("/{id:guid}/test-connection", async (Guid id, TestAiProviderConnectionRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new TestAiProviderConnectionCommand
            {
                Id = id,
                Configuration = request.Configuration,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("TestAiProviderConnectionById")
        .Produces<TestConnectionResponse>();
    }
}
