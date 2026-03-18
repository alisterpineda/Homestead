namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public static class GetDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/documents", (GetDocumentSettingsHandler handler) =>
        {
            return Results.Ok(handler.Handle());
        })
        .WithName("GetDocumentSettings")
        .Produces<DocumentSettingsResponse>();
    }
}
