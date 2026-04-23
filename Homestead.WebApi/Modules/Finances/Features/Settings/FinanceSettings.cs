using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Finances.Features.Settings;

public class FinanceSettings
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DisplayCurrencyId { get; set; }
}
