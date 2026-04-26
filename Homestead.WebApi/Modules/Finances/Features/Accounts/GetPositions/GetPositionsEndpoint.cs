using Homestead.WebApi.Authentication;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetPositions;

public static class GetPositionsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/{accountId:guid}/positions",
            async (Guid accountId, ISender sender, CancellationToken ct) =>
        {
            var response = await sender.Send(new GetPositionsQuery(accountId), ct);
            return Results.Ok(response);
        })
        .WithName("GetAccountPositions")
        .Produces<IReadOnlyList<PositionResponse>>()
        .RequireAuthorization(AuthPolicies.ActiveUser);
    }
}
