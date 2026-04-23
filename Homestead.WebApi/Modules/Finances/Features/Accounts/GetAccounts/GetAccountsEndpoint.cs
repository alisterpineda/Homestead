using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public static class GetAccountsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetAccountsQuery request,
            ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(request, ct));
        })
        .WithName("GetAccounts")
        .Produces<PaginatedResponse<AccountResponse>>();
    }
}
