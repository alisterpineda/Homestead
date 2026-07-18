namespace Homestead.WebApi.Modules.Finances.Features.Settings;

public record FinanceSettingsResponse
{
    public Guid? DisplayCurrencyId { get; init; }

    /// <summary>Effective auto-categorise-from-payee flag (absent in storage ⇒ on).</summary>
    public bool AutoCategoriseFromPayee { get; init; }
}
