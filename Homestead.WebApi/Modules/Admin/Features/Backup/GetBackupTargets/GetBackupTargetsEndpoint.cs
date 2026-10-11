namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

public static class GetBackupTargetsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/targets", async (GetBackupTargetsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetBackupTargets")
        .Produces<GetBackupTargetsResponse>();
    }
}
