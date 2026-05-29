using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.ListAiProviderModels;

public static class ListAiProviderModelsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // Ad-hoc fetch (create dialog): caller supplies the provider type + config.
        group.MapPost("/list-models", async (ListAiProviderModelsRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ListAiProviderModelsCommand
            {
                ProviderType = request.ProviderType,
                Configuration = request.Configuration,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("ListAiProviderModels")
        .Produces<ListModelsResponse>();

        // Existing provider (edit sheet): a blank/masked key reuses the stored secret.
        group.MapPost("/{id:guid}/list-models", async (Guid id, ListAiProviderModelsRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ListAiProviderModelsCommand
            {
                Id = id,
                Configuration = request.Configuration,
            }, ct);
            return Results.Ok(response);
        })
        .WithName("ListAiProviderModelsById")
        .Produces<ListModelsResponse>();
    }
}
