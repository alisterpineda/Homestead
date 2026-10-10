namespace Homestead.WebApi.Modules.Documents.Features.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetDocumentSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new GetDocumentSettingsQuery(), ct));
        })
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
