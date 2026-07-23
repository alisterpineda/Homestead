using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetBackupTargets;

public static class GetBackupTargetsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/targets", async (ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetBackupTargetsQuery(), ct);
            return Results.Ok(response);
        })
        .WithName("GetBackupTargets")
        .Produces<GetBackupTargetsResponse>();
    }
}
