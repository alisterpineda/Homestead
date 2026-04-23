namespace Homestead.WebApi.Modules.Finances.Features.Settings;

public record FinanceSettingsResponse
{
    public Guid? DisplayCurrencyId { get; init; }
}
