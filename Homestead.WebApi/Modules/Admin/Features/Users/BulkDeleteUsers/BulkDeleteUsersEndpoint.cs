using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;

public static class BulkDeleteUsersEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/bulk-delete", async (BulkDeleteUsersCommand request,
            BulkDeleteUsersHandler handler, CancellationToken ct) =>
        {
            if (request.Ids.Count == 0)
                return ProblemDetailsFactory.BusinessRuleResult("At least one user ID must be provided.");

            var response = await handler.Handle(request, ct);
            return Results.Ok(response);
        })
        .WithName("BulkDeleteUsers")
        .Produces<BulkDeleteUsersResponse>(StatusCodes.Status200OK);
    }
}
