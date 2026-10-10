using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

public static class GetStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", async (Guid jobId, GetStatementImportHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetStatementImportQuery(jobId), ct);
            return Results.Ok(response);
        })
        .WithName("GetStatementImport")
        .Produces<StatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
