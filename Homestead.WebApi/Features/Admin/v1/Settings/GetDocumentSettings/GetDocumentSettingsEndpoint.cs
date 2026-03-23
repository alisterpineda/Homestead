using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/documents", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetDocumentSettingsQuery(), ct));
        })
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
