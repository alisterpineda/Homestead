namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

public static class GetBackupTargetsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/targets", async (GetBackupTargetsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetBackupTargetsQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetBackupTargets")
        .Produces<GetBackupTargetsResponse>();
    }
}
