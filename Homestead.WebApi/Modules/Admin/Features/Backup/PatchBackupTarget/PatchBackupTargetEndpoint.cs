namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupTarget;

public static class PatchBackupTargetEndpoint
{
    public record PatchBackupTargetBody
    {
        public bool? IsEnabled { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/targets/{key}", async (
            string key, PatchBackupTargetBody body, PatchBackupTargetHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new PatchBackupTargetCommand
            {
                TargetKey = key,
                IsEnabled = body.IsEnabled,
            }, ct);

            return Results.Ok(response);
        })
        .WithName("PatchBackupTarget")
        .Produces<PatchBackupTargetResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
