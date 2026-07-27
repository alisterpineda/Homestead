using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;

public static class ValidateBackupTargetConnectionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/targets/{key}/validate", async (
            string key, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new ValidateBackupTargetConnectionCommand(key), ct);
            return Results.Ok(response);
        })
        .WithName("ValidateBackupTargetConnection")
        .Produces<ValidateBackupTargetConnectionResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
