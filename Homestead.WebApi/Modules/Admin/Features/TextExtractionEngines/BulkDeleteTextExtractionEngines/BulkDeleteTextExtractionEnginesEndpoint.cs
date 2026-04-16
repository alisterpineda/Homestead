using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public static class BulkDeleteTextExtractionEnginesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteTextExtractionEnginesCommand request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one engine ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteTextExtractionEngines")
        .Produces<BulkDeleteTextExtractionEnginesResponse>(StatusCodes.Status200OK);
    }
}
