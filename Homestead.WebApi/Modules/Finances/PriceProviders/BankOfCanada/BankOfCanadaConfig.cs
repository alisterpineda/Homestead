namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

public record BankOfCanadaConfig
{
    public const string DefaultBaseUrl = "https://www.bankofcanada.ca/valet/";

    public string BaseUrl { get; init; } = DefaultBaseUrl;
}
