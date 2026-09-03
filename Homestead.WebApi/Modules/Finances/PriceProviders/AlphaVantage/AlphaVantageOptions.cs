namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

// The "PriceSources:AlphaVantage" options bag.
public class AlphaVantageOptions() : PriceSourceOptions(DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://www.alphavantage.co/query";

    // Required when Enabled. Never surfaced to the UI — only whether it is set.
    public string? ApiKey { get; set; }
}
