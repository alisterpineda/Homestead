namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/documents", async (GetDocumentSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(ct));
        })
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
