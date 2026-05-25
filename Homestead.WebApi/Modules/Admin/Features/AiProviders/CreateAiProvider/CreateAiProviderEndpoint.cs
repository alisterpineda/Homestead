using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.CreateAiProvider;

public static class CreateAiProviderEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateAiProviderCommand request,
            ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/admin/ai-providers/{response.Id}", response);
        })
        .WithName("CreateAiProvider")
        .Produces<AiProviderResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict);
    }
}
