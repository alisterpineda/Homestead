using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;

public static class GetPayeesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetPayeesQuery request,
            GetPayeesHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(request, ct));
        })
        .WithName("GetPayees")
        .Produces<PaginatedResponse<PayeeResponse>>();
    }
}
