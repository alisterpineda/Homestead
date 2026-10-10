using Homestead.WebApi.Authentication;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public static class GetPositionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/positions",
            async (Guid accountId, GetPositionsHandler handler, CancellationToken ct) =>
        {
            var response = await handler.Handle(new GetPositionsQuery(accountId), ct);
            return Results.Ok(response);
        })
        .WithName("GetAccountPositions")
        .Produces<IReadOnlyList<PositionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
