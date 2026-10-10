using Homestead.WebApi.Persistence;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public class GetFinanceSettingsHandler(AppDbContext db)
{
    public async Task<FinanceSettingsResponse> Handle(GetFinanceSettingsQuery request, CancellationToken ct)
    {
        var settings = await FinanceSettings.LoadAsync(db, ct);

        return new FinanceSettingsResponse
        {
            DisplayCurrencyId = settings.DisplayCurrencyId,
            AutoCategoriseFromPayee = settings.AutoCategoriseFromPayeeEnabled,
        };
    }
}
