using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Settings;

public class FinanceSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DisplayCurrencyId { get; set; }

    public static FinanceSettings From(AppSetting? entity) =>
        entity is null
            ? new FinanceSettings()
            : JsonSerializer.Deserialize<FinanceSettings>(entity.Value, FieldChange.JsonOptions)
              ?? new FinanceSettings();

    public static async Task<FinanceSettings> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var entity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.FinancesModule, ct);
        return From(entity);
    }
}
