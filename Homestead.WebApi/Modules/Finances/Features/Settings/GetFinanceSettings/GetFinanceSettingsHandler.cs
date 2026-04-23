using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Settings.GetFinanceSettings;

public class GetFinanceSettingsHandler(AppDbContext db)
    : IRequestHandler<GetFinanceSettingsQuery, FinanceSettingsResponse>
{
    public async Task<FinanceSettingsResponse> Handle(GetFinanceSettingsQuery request, CancellationToken ct)
    {
        var entity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.FinancesModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<FinanceSettings>(entity.Value, FieldChange.JsonOptions) ?? new FinanceSettings()
            : new FinanceSettings();

        return new FinanceSettingsResponse
        {
            DisplayCurrencyId = settings.DisplayCurrencyId
        };
    }
}
