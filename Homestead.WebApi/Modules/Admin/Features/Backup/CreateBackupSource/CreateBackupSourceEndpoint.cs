namespace Homestead.WebApi.Modules.Admin.Features.Backup.CreateBackupSource;

public static class CreateBackupSourceEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/sources", async (
            CreateBackupSourceBody body, CreateBackupSourceHandler handler, CancellationToken ct) =>
        {
            var response = await handler.HandleAsync(body, ct);

            // 201 with no Location: there is no GET for a single source. The admin page reads pairings
            // nested under GET /targets, so advertising /sources/{id} would point at a 405.
            return Results.Created((string?)null, response);
        })
        .WithName("CreateBackupSource")
        .Produces<CreateBackupSourceResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
