using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;

public static class PollBackupTargetConnectionEndpoint
{
    public record PollBackupTargetConnectionBody(string PollToken);

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/connect/poll", async (
            string key, PollBackupTargetConnectionBody body, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(
                new PollBackupTargetConnectionCommand(key, body.PollToken), ct);

            return Results.Ok(response);
        })
        .WithName("PollBackupTargetConnection")
        .Produces<PollBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }
}
