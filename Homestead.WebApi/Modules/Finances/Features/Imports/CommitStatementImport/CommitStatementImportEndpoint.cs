using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.CommitStatementImport;

public static class CommitStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{jobId:guid}/commit", async (
            Guid jobId, CommitStatementImportCommand body, ISender sender, CancellationToken ct) =>
        {
            var request = body with { JobId = jobId };
            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("CommitStatementImport")
        .Produces<CommitStatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
