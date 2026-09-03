using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

namespace Homestead.WebApi.Modules.Finances.PriceProviders;

// Reads the "PriceSources:{Source}" section for each external price source and validates it per provider
// (mirrors AiProviderConfiguration). Validation returns an error string rather than throwing: there is
// deliberately no ValidateOnStart — an enabled-but-invalid source must not stop the app from booting. The
// cost is that config errors surface at first use; the mitigation is that ConfigurationError is exposed on
// the source's page, so a source that cannot import reads as "not configured, because <reason>".
public static class PriceSourceConfiguration
{
    public const string SectionName = "PriceSources";

    public static string SectionFor(CommodityPriceSource source) => $"{SectionName}:{source}";

    // A missing section binds to a fresh bag: Enabled=false, so an unconfigured source is simply off.
    public static T Read<T>(IConfiguration configuration, CommodityPriceSource source) where T : class, new() =>
        configuration.GetSection(SectionFor(source)).Get<T>() ?? new T();

    // Validation deliberately ignores Enabled: the readout distinguishes "disabled" from "enabled but
    // broken", and both are reported separately on the source's page.
    public static string? Validate(AlphaVantageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return "ApiKey is required.";
        return ValidateBaseUrl(options.ResolveBaseUrl());
    }

    public static string? Validate(BankOfCanadaOptions options) =>
        ValidateBaseUrl(options.ResolveBaseUrl());

    private static string? ValidateBaseUrl(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out _) ? null : "BaseUrl must be an absolute URI.";

    // Why a source cannot import right now, as (error code, message) — or null when it can. The ONE
    // definition shared by the Run-now endpoint (400) and the import job (PriceImportException), so the
    // two can never drift: same order, same wording, same codes.
    public static (string Code, string Message)? ImportBlocker(IPriceProvider? provider, CommodityPriceSource source)
    {
        if (provider is null)
            return (ProviderNotRegistered, $"No provider is registered for price source '{source}'.");
        if (!provider.IsEnabled)
            return (SourceDisabled, $"Price source '{source}' is disabled.");
        if (!provider.IsConfigured)
            return (SourceNotConfigured, $"Price source '{source}' is not configured: {provider.ConfigurationError}");
        return null;
    }

    public const string ProviderNotRegistered = "provider_not_registered";
    public const string SourceDisabled = "source_disabled";
    public const string SourceNotConfigured = "source_not_configured";
}
