namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

public record AlphaVantageConfig
{
    public const string DefaultBaseUrl = "https://www.alphavantage.co/query";

    public string BaseUrl { get; init; } = DefaultBaseUrl;
    public string? ApiKey { get; init; }
}
