namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;

public static class PatchBackupSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/sources/{id:guid}", async (
            Guid id, PatchBackupSourceBody body, PatchBackupSourceHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(id, body, ct)))
        .WithName("PatchBackupSource")
        .Produces<PatchBackupSourceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
