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
            ["TSX"] = "CAD",
        };

    private static readonly IReadOnlyDictionary<string, string> _exchangeToApiSuffix =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TSX"] = ".TRT",
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

    public IReadOnlyList<string> CoveredQuoteCurrencies { get; } =
        _exchangeToQuote.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        if (request.Commodities.Count == 0)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var config = ParseConfig(request.Config);
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("AlphaVantage API key is not configured.");

        var baseUri = new Uri(config.BaseUrl, UriKind.Absolute);

        var quoteCurrenciesBySymbol = new Dictionary<string, Commodity>(StringComparer.OrdinalIgnoreCase);
        var candidateSecurities = new List<Commodity>();
        foreach (var commodity in request.Commodities)
        {
            if (commodity.Kind == CommodityKind.Currency)
            {
                quoteCurrenciesBySymbol[commodity.Symbol] = commodity;
                continue;
            }
            if (commodity.Kind != CommodityKind.Security)
                continue;
            if (string.IsNullOrWhiteSpace(commodity.Exchange))
                continue;
            if (!_exchangeToQuote.ContainsKey(commodity.Exchange))
                continue;
            candidateSecurities.Add(commodity);
        }

        var resolved = new List<(Commodity Security, Commodity Quote)>();
        foreach (var security in candidateSecurities)
        {
            var quoteSymbol = _exchangeToQuote[security.Exchange!];
            if (quoteCurrenciesBySymbol.TryGetValue(quoteSymbol, out var quote))
                resolved.Add((security, quote));
        }

        if (resolved.Count == 0)
            return PriceImportResult.Success(Array.Empty<PriceObservation>());

        var observations = new List<PriceObservation>();
        var failed = new List<string>();
        var successCount = 0;

        for (var i = 0; i < resolved.Count; i++)
        {
            var (security, quote) = resolved[i];
            var apiSymbol = ToApiSymbol(security);

            if (i > 0)
                await _delay(_interCallDelay, ct);

            var dailyResponse = await _client.GetDailyTimeSeriesAsync(
                baseUri, apiSymbol, config.ApiKey!, "compact", ct);

            if (dailyResponse is null)
            {
                failed.Add(security.Symbol);
                continue;
            }

            if (TryGetApiBlockMessage(dailyResponse.Information, dailyResponse.Note) is { } dailyBlock)
            {
                var warning = $"AlphaVantage stopped responding after {successCount} of {resolved.Count} securities: {dailyBlock}";
                return PriceImportResult.Partial(observations, warning);
            }

            if (!string.IsNullOrEmpty(dailyResponse.ErrorMessage))
            {
                failed.Add(security.Symbol);
                continue;
            }

            DateTimeOffset? dailyEarliest = null;
            if (dailyResponse.TimeSeriesDaily is not null)
            {
                foreach (var (dateStr, dayEl) in dailyResponse.TimeSeriesDaily)
                {
                    if (TryParseDate(dateStr) is not { } priceAt)
                        continue;
                    if (dailyEarliest is null || priceAt < dailyEarliest.Value)
                        dailyEarliest = priceAt;

                    var observation = TryParseObservation(security.Id, quote.Id, priceAt, dayEl, request.FromAt, request.ToAt);
                    if (observation is not null)
                        observations.Add(observation);
                }
            }

            if (dailyEarliest is { } earliest && request.FromAt < earliest)
            {
                await _delay(_interCallDelay, ct);

                var weeklyResponse = await _client.GetWeeklyTimeSeriesAsync(
                    baseUri, apiSymbol, config.ApiKey!, ct);

                if (weeklyResponse is null)
                {
                    successCount++;
                    continue;
                }

                if (TryGetApiBlockMessage(weeklyResponse.Information, weeklyResponse.Note) is { } weeklyBlock)
                {
                    var warning = $"AlphaVantage stopped responding after {successCount} of {resolved.Count} securities: {weeklyBlock}";
                    return PriceImportResult.Partial(observations, warning);
                }

                if (!string.IsNullOrEmpty(weeklyResponse.ErrorMessage))
                {
                    successCount++;
                    continue;
                }

                if (weeklyResponse.TimeSeriesWeekly is not null)
                {
                    foreach (var (dateStr, dayEl) in weeklyResponse.TimeSeriesWeekly)
                    {
                        if (TryParseDate(dateStr) is not { } priceAt)
                            continue;
                        if (priceAt >= earliest)
                            continue;
                        var observation = TryParseObservation(security.Id, quote.Id, priceAt, dayEl, request.FromAt, request.ToAt);
                        if (observation is not null)
                            observations.Add(observation);
                    }
                }
            }

            successCount++;
        }

        if (failed.Count > 0)
            return PriceImportResult.Partial(observations, $"Skipped: {string.Join(", ", failed)}");

        return PriceImportResult.Success(observations);
    }

    private static string? TryGetApiBlockMessage(string? information, string? note)
    {
        if (!string.IsNullOrWhiteSpace(information))
            return information.Trim();
        if (!string.IsNullOrWhiteSpace(note))
            return note.Trim();
        return null;
    }

    private static string ToApiSymbol(Commodity security) =>
        security.Exchange is { } ex && _exchangeToApiSuffix.TryGetValue(ex, out var suffix)
            ? security.Symbol + suffix
            : security.Symbol;

    private static DateTimeOffset? TryParseDate(string dateStr)
    {
        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return null;
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static PriceObservation? TryParseObservation(
        Guid securityId,
        Guid quoteCurrencyId,
        DateTimeOffset priceAt,
        JsonElement dayEl,
        DateTimeOffset fromAt,
        DateTimeOffset toAt)
    {
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
