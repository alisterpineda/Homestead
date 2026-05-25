using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.BulkDeleteAiProviders;

public static class BulkDeleteAiProvidersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteAiProvidersCommand request,
            ISender sender, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one provider ID must be provided.");

            var response = await sender.Send(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteAiProviders")
        .Produces<BulkDeleteAiProvidersResponse>(StatusCodes.Status200OK);
    }
}
