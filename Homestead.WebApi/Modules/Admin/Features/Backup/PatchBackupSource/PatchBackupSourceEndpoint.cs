namespace Homestead.WebApi.Modules.Admin.Features.Backup.PatchBackupSource;

public static class PatchBackupSourceEndpoint
{
    public record PatchBackupSourceBody
    {
        public bool? IsEnabled { get; init; }
        public bool? AcknowledgePruneBlock { get; init; }
    }

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPatch("/sources/{id:guid}", async (
            Guid id, PatchBackupSourceBody body, PatchBackupSourceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new PatchBackupSourceCommand
            {
                Id = id,
                IsEnabled = body.IsEnabled,
                AcknowledgePruneBlock = body.AcknowledgePruneBlock,
            }, ct);

            return Results.Ok(response);
        })
        .WithName("PatchBackupSource")
        .Produces<PatchBackupSourceResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
