using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

public static class RollbackStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{documentId:guid}", async (
            Guid documentId, RollbackStatementImportHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(
                new RollbackStatementImportCommand { DocumentId = documentId }, ct);
            return Results.Ok(response);
        })
        .WithName("RollbackStatementImport")
        .Produces<RollbackStatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
