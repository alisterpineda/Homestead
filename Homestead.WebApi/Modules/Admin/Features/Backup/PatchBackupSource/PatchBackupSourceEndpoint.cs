using MediatR;

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
            Guid id, PatchBackupSourceBody body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new PatchBackupSourceCommand
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
