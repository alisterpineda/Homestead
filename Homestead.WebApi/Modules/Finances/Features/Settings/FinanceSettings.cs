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

    /// <summary>
    /// Whether statement-import auto-categorises each committed line from its payee's default category.
    /// Nullable on purpose: this POCO is round-tripped through <c>app_settings</c> JSON where a plain
    /// <c>bool</c> would default to <c>false</c> for every pre-existing (or empty) settings blob — so an
    /// <b>absent</b> value must read as ON. Consumers read <see cref="AutoCategoriseFromPayeeEnabled"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoCategoriseFromPayee { get; set; }

    /// <summary>Effective flag: absent ⇒ on (default true).</summary>
    [JsonIgnore]
    public bool AutoCategoriseFromPayeeEnabled => AutoCategoriseFromPayee ?? true;

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
