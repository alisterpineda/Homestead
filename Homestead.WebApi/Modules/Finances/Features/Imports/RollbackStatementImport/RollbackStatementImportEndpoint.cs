using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.RollbackStatementImport;

public static class RollbackStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapDelete("/{documentId:guid}", async (
            Guid documentId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(
                new RollbackStatementImportCommand { DocumentId = documentId }, ct);
            return Results.Ok(response);
        })
        .WithName("RollbackStatementImport")
        .Produces<RollbackStatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
