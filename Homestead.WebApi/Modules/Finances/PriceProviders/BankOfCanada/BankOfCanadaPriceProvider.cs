using System.Text.Json;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

public class BankOfCanadaPriceProvider(BankOfCanadaValetClient client) : IPriceProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly string[] _supportedSymbols =
    [
        "USD", "EUR", "GBP", "JPY", "AUD", "CHF", "CNY", "HKD", "INR", "MXN",
        "NZD", "SEK", "KRW", "NOK", "SGD", "BRL", "CLP", "COP", "IDR", "MYR",
        "PEN", "PHP", "RUB", "TWD", "THB", "TRY", "VND", "ZAR", "AED",
    ];

    public CommodityPriceSource Source => CommodityPriceSource.BankOfCanada;

    public PriceProviderConfigSchema ConfigSchema { get; } = new(new[]
    {
        new PriceProviderConfigField(
            Key: "BaseUrl",
            Label: "Valet API base URL",
            Type: PriceProviderConfigFieldType.String,
            IsRequired: true,
            Help: "Bank of Canada Valet API root URL.",
            DefaultValue: BankOfCanadaConfig.DefaultBaseUrl),
    });

    public IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; } =
        _supportedSymbols.Select(s => new PriceProviderSupportedPair(s, null, "CAD")).ToArray();

    public IReadOnlyList<string> CoveredSecurityExchanges { get; } = Array.Empty<string>();

    public async Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        if (request.Commodities.Count == 0)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var config = ParseConfig(request.Config);
        var baseUri = new Uri(config.BaseUrl, UriKind.Absolute);

        var supported = new HashSet<string>(_supportedSymbols, StringComparer.OrdinalIgnoreCase);

        // Build per-series mapping: series name → commodity Id
        var seriesToCommodity = new Dictionary<string, Commodity>(StringComparer.OrdinalIgnoreCase);
        Commodity? cad = null;
        foreach (var commodity in request.Commodities)
        {
            if (commodity.Kind != CommodityKind.Currency)
                continue;
            if (string.Equals(commodity.Symbol, "CAD", StringComparison.OrdinalIgnoreCase))
            {
                cad = commodity;
                continue;
            }
            if (!supported.Contains(commodity.Symbol))
                continue;
            var seriesName = $"FX{commodity.Symbol.ToUpperInvariant()}CAD";
            seriesToCommodity[seriesName] = commodity;
        }

        if (seriesToCommodity.Count == 0 || cad is null)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var fromDate = DateOnly.FromDateTime(request.FromAt.UtcDateTime);
        var toDate = DateOnly.FromDateTime(request.ToAt.UtcDateTime);

        var raw = await client.GetObservationsAsync(baseUri, seriesToCommodity.Keys.ToArray(), fromDate, toDate, ct);

        var observations = new List<PriceObservation>(raw.Count);
        foreach (var item in raw)
        {
            if (!seriesToCommodity.TryGetValue(item.SeriesName, out var commodity))
                continue;
            observations.Add(new PriceObservation(commodity.Id, cad.Id, item.PriceAt, item.Value));
        }

        return PriceImportResult.Success(observations);
    }

    private static BankOfCanadaConfig ParseConfig(string? rawConfig)
    {
        if (string.IsNullOrWhiteSpace(rawConfig))
            return new BankOfCanadaConfig();
        try
        {
            var parsed = JsonSerializer.Deserialize<BankOfCanadaConfig>(rawConfig, _jsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.BaseUrl))
                return new BankOfCanadaConfig();
            return parsed;
        }
        catch (JsonException)
        {
            return new BankOfCanadaConfig();
        }
    }
}
