namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public static class GetFinanceSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetFinanceSettingsHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(ct)))
        .WithName("GetFinanceSettings")
        .Produces<FinanceSettingsResponse>();
    }
}
