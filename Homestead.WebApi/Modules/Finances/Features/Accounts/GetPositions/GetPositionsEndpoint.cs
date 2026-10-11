using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public static class GetPositionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/positions",
            async (Guid accountId, GetPositionsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(accountId, ct)))
        .WithName("GetAccountPositions")
        .Produces<IReadOnlyList<PositionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
