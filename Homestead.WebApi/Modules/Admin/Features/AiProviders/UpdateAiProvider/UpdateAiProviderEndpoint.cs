using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.UpdateAiProvider;

public static class UpdateAiProviderEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateAiProviderRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateAiProviderCommand { Id = id, Request = request };
            return Results.Ok(await sender.Send(command, ct));
        })
        .WithName("UpdateAiProvider")
        .Produces<AiProviderResponse>()
        .Produces(StatusCodes.Status409Conflict);
    }
}
