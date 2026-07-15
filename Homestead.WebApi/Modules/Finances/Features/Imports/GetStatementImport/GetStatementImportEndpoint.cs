using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.GetStatementImport;

public static class GetStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{jobId:guid}", async (Guid jobId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetStatementImportQuery(jobId), ct);
            return Results.Ok(response);
        })
        .WithName("GetStatementImport")
        .Produces<StatementImportResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
