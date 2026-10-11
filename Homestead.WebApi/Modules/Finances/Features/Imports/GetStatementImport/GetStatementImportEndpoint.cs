using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

public static class GetStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", async (Guid jobId, GetStatementImportHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(jobId, ct)))
        .WithName("GetStatementImport")
        .Produces<StatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
