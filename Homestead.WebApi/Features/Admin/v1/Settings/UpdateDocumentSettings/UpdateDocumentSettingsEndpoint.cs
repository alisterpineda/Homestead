namespace Homestead.WebApi.Features.Admin.v1.Settings.UpdateDocumentSettings;

public static class UpdateDocumentSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/documents", async (UpdateDocumentSettingsRequest request,
            UpdateDocumentSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("UpdateDocumentSettings")
        .Produces<DocumentSettingsResponse>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
