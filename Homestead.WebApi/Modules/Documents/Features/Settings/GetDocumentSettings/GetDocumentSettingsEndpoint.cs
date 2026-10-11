namespace Homestead.WebApi.Modules.Documents.Features.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetDocumentSettingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
