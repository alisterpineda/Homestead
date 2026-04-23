using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public static class GetFinanceSettingsEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("", async (ISender sender, CancellationToken ct) =>
        {
            return Results.Ok(await sender.Send(new GetFinanceSettingsQuery(), ct));
        })
        .WithName("GetFinanceSettings")
        .Produces<FinanceSettingsResponse>();
    }
}
