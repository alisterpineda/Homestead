namespace Homestead.WebApi.Modules.Admin.Features.Backup.GetDocumentBackupState;

public static class GetDocumentBackupStateEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        // No per-endpoint authorization: the backup group already carries RequireAdmin, and adding a
        // second policy here would make the module's one invariant look optional.
        group.MapGet("/documents/{documentId:guid}", async (
            Guid documentId, GetDocumentBackupStateHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetDocumentBackupStateQuery(documentId), ct);
            return response is not null ? Results.Ok(response) : Results.NotFound();
        })
        .WithName("GetDocumentBackupState")
        .Produces<GetDocumentBackupStateResponse>()
        .Produces(StatusCodes.Status404NotFound);
    }
}
