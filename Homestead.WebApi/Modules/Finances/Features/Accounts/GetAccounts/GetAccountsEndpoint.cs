using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public static class GetAccountsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async ([AsParameters] GetAccountsQuery request,
            GetAccountsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(request, ct));
        })
        .WithName("GetAccounts")
        .Produces<PaginatedResponse<AccountResponse>>();
    }
}
