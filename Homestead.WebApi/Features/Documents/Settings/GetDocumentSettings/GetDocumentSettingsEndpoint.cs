using MediatR;

namespace Homestead.WebApi.Features.Documents.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetDocumentSettingsQuery(), ct));
        })
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
