using System.Globalization;
using System.Text.Json;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

public class AlphaVantagePriceProvider : IPriceProvider
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly TimeSpan _interCallDelay = TimeSpan.FromSeconds(12);

    private static readonly IReadOnlyDictionary<string, string> _exchangeToQuote =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NASDAQ"] = "USD",
            ["NYSE"] = "USD",
            ["AMEX"] = "USD",
        };

    private readonly AlphaVantageClient _client;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AlphaVantagePriceProvider(
        AlphaVantageClient client,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = client;
        _delay = delay ?? Task.Delay;
    }

    public CommodityPriceSource Source => CommodityPriceSource.AlphaVantage;

    public PriceProviderConfigSchema ConfigSchema { get; } = new(new[]
    {
        new PriceProviderConfigField(
            Key: "ApiKey",
            Label: "API key",
            Type: PriceProviderConfigFieldType.Secret,
            IsRequired: true,
            Help: "Alpha Vantage API key. Get one free at https://www.alphavantage.co/support/#api-key."),
        new PriceProviderConfigField(
            Key: "BaseUrl",
            Label: "Base URL",
            Type: PriceProviderConfigFieldType.String,
            IsRequired: false,
            Help: "Alpha Vantage query endpoint URL.",
            DefaultValue: AlphaVantageConfig.DefaultBaseUrl),
    });

    public IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; } =
        Array.Empty<PriceProviderSupportedPair>();

    public IReadOnlyList<string> CoveredSecurityExchanges { get; } =
        _exchangeToQuote.Keys.ToArray();

    public async Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        if (request.Commodities.Count == 0)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var config = ParseConfig(request.Config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("AlphaVantage API key is not configured.");

        var baseUri = new Uri(config.BaseUrl, UriKind.Absolute);

        Commodity? usd = null;
        var securities = new List<Commodity>();
        foreach (var commodity in request.Commodities)
        {
            if (commodity.Kind == CommodityKind.Currency
                && string.Equals(commodity.Symbol, "USD", StringComparison.OrdinalIgnoreCase))
            {
                usd = commodity;
                continue;
            }
            if (commodity.Kind != CommodityKind.Security)
                continue;
            if (string.IsNullOrWhiteSpace(commodity.Exchange))
                continue;
            if (!_exchangeToQuote.ContainsKey(commodity.Exchange))
                continue;
            securities.Add(commodity);
        }

        if (usd is null || securities.Count == 0)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var now = DateTimeOffset.UtcNow;
        var outputsize = request.FromAt >= now.AddDays(-100) ? "compact" : "full";

        var observations = new List<PriceObservation>();
        var failed = new List<string>();
        var successCount = 0;

        for (var i = 0; i < securities.Count; i++)
        {
            if (i > 0)
                await _delay(_interCallDelay, ct);

            var security = securities[i];
            var response = await _client.GetDailyTimeSeriesAsync(
                baseUri, security.Symbol, config.ApiKey!, outputsize, ct);

            if (response is null)
            {
                failed.Add(security.Symbol);
                continue;
            }

            if (!string.IsNullOrEmpty(response.Information) || !string.IsNullOrEmpty(response.Note))
            {
                var warning = $"Rate limit reached after {successCount} of {securities.Count} securities; remaining will retry next run.";
                return PriceImportResult.Partial(observations, warning);
            }

            if (!string.IsNullOrEmpty(response.ErrorMessage))
            {
                failed.Add(security.Symbol);
                continue;
            }

            if (response.TimeSeriesDaily is not null)
            {
                foreach (var (dateStr, dayEl) in response.TimeSeriesDaily)
                {
                    var observation = TryParseObservation(security.Id, usd.Id, dateStr, dayEl, request.FromAt, request.ToAt);
                    if (observation is not null)
                        observations.Add(observation);
                }
            }

            successCount++;
        }

        if (failed.Count > 0)
            return PriceImportResult.Partial(observations, $"Skipped: {string.Join(", ", failed)}");

        return PriceImportResult.Success(observations);
    }

    private static PriceObservation? TryParseObservation(
        Guid securityId,
        Guid quoteCurrencyId,
        string dateStr,
        JsonElement dayEl,
        DateTimeOffset fromAt,
        DateTimeOffset toAt)
    {
        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return null;

        var priceAt = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (priceAt < fromAt || priceAt > toAt)
            return null;

        if (dayEl.ValueKind != JsonValueKind.Object)
            return null;
        if (!dayEl.TryGetProperty("4. close", out var closeEl) || closeEl.ValueKind != JsonValueKind.String)
            return null;
        var closeStr = closeEl.GetString();
        if (string.IsNullOrWhiteSpace(closeStr))
            return null;
        if (!decimal.TryParse(closeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
            return null;

        return new PriceObservation(securityId, quoteCurrencyId, priceAt, close);
    }

    private static AlphaVantageConfig ParseConfig(string? rawConfig)
    {
        if (string.IsNullOrWhiteSpace(rawConfig))
            return new AlphaVantageConfig();
        try
        {
            var parsed = JsonSerializer.Deserialize<AlphaVantageConfig>(rawConfig, _jsonOptions);
            if (parsed is null)
                return new AlphaVantageConfig();
            if (string.IsNullOrWhiteSpace(parsed.BaseUrl))
                parsed = parsed with { BaseUrl = AlphaVantageConfig.DefaultBaseUrl };
            return parsed;
        }
        catch (JsonException)
        {
            return new AlphaVantageConfig();
        }
    }
}
