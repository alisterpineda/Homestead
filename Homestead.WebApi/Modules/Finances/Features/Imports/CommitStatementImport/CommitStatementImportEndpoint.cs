using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public static class CommitStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{jobId:guid}/commit", async (
            Guid jobId, CommitStatementImportCommand body, CommitStatementImportHandler handler, CancellationToken ct) =>
        {
            var request = body with { JobId = jobId };
            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("CommitStatementImport")
        .Produces<CommitStatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
