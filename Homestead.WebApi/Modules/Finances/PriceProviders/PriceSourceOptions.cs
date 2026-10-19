namespace Homestead.WebApi.Modules.Finances.PriceProviders;

// What every "PriceSources:{Source}" options bag shares. Enablement and credentials are deployment
// facts, so they live here (config) rather than on the settings row — same rule as Ai:* /
// Ocr:Engines. Per-provider bags add only what their API needs (e.g. an ApiKey).
public abstract class PriceSourceOptions(string defaultBaseUrl)
{
    public bool Enabled { get; set; }

    // Nullable so an empty env var still resolves to the provider's default.
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? defaultBaseUrl : BaseUrl.Trim();
}
