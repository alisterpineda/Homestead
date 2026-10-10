namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public static class GetFinanceSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (GetFinanceSettingsHandler handler, CancellationToken ct) =>
        {
            return Results.Ok(await handler.Handle(new GetFinanceSettingsQuery(), ct));
        })
        .WithName("GetFinanceSettings")
        .Produces<FinanceSettingsResponse>();
    }
}
