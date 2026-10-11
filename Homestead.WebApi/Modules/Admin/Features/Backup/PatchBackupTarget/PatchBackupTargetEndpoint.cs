namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

public static class PatchBackupTargetEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/targets/{key}", async (
            string key, PatchBackupTargetBody body, PatchBackupTargetHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(key, body, ct)))
        .WithName("PatchBackupTarget")
        .Produces<PatchBackupTargetResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
