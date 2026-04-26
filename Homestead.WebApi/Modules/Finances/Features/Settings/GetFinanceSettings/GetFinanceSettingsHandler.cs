using Homestead.WebApi.Persistence;
using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public class GetFinanceSettingsHandler(AppDbContext db)
    : IRequestHandler<GetFinanceSettingsQuery, FinanceSettingsResponse>
{
    public async Task<FinanceSettingsResponse> Handle(GetFinanceSettingsQuery request, CancellationToken ct)
    {
        var settings = await FinanceSettings.LoadAsync(db, ct);

        return new FinanceSettingsResponse
        {
            DisplayCurrencyId = settings.DisplayCurrencyId
        };
    }
}
