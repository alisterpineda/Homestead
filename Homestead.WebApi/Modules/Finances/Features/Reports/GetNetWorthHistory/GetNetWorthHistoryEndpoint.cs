namespace Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;

public static class GetNetWorthHistoryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/net-worth-history", async ([AsParameters] GetNetWorthHistoryQuery request,
            GetNetWorthHistoryHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.HandleAsync(request, ct));
        })
        .WithName("GetNetWorthHistory")
        .Produces<GetNetWorthHistoryResponse>();
    }
}
