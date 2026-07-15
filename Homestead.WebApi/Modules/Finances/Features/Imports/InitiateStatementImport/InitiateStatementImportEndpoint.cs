using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Imports.InitiateStatementImport;

public static class InitiateStatementImportEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("", async (
            InitiateStatementImportCommand request, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(request, ct);
            return Results.Created($"/api/v1/finances/imports/{response.JobId}", response);
        })
        .WithName("InitiateStatementImport")
        .Produces<InitiateStatementImportResponse>(StatusCodes.Status201Created)
        .RequireAuthorization(AuthPolicies.RequireContributor);
    }
}
