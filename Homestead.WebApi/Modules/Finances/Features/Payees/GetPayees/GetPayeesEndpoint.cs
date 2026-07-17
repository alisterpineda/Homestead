using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.GetPayees;

public static class GetPayeesEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetPayeesQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetPayees")
        .Produces<PaginatedResponse<PayeeResponse>>();
    }
}
