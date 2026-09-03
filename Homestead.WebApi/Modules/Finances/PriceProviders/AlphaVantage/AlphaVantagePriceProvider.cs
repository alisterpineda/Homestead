using System.Globalization;
using System.Text.Json;
using Homestead.WebApi.Domain.Finances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

public class AlphaVantagePriceProvider : IPriceProvider
{
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
    private readonly AlphaVantageOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger<AlphaVantagePriceProvider> _logger;

    public AlphaVantagePriceProvider(
        AlphaVantageClient client,
        AlphaVantageOptions options,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        ILogger<AlphaVantagePriceProvider>? logger = null)
    {
        _client = client;
        _options = options;
        _delay = delay ?? Task.Delay;
        _logger = logger ?? NullLogger<AlphaVantagePriceProvider>.Instance;
        // Computed once at construction: config is static, so the verdict never changes.
        ConfigurationError = PriceSourceConfiguration.Validate(options);
    }

    public CommodityPriceSource Source => CommodityPriceSource.AlphaVantage;

    public bool IsEnabled => _options.Enabled;
    public string? ConfigurationError { get; }
    public bool IsConfigured => IsEnabled && ConfigurationError is null;
    public bool? HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public string BaseUrl => _options.ResolveBaseUrl();

    public IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; } =
        Array.Empty<PriceProviderSupportedPair>();

    public IReadOnlyList<string> CoveredSecurityExchanges { get; } =
        _exchangeToQuote.Keys.ToArray();

    public IReadOnlyList<string> CoveredQuoteCurrencies { get; } =
        _exchangeToQuote.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public TimeSpan RevisionLookback => TimeSpan.FromDays(1);

    public async Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "AlphaVantage import starting: {CommodityCount} commodities, window {FromAt:o}..{ToAt:o}",
            request.Commodities.Count, request.FromAt, request.ToAt);

        if (request.Commodities.Count == 0)
        {
            _logger.LogWarning("AlphaVantage import returning empty: no commodities in request");
            return PriceImportResult.Success(Array.Empty<PriceObservation>());
        }

        if (!IsConfigured)
            throw new InvalidOperationException(
                $"Price source '{Source}' is unavailable: {ConfigurationError ?? "disabled"}");

        // ApiKey is non-null here: Validate requires it, and the IsConfigured guard runs first.
        var baseUri = new Uri(BaseUrl, UriKind.Absolute);

        var quoteCurrenciesBySymbol = new Dictionary<string, Commodity>(StringComparer.OrdinalIgnoreCase);
        var candidateSecurities = new List<Commodity>();
        var droppedUnsupportedExchange = new List<string>();
        var droppedNoExchange = new List<string>();
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
            {
                droppedNoExchange.Add(commodity.Symbol);
                continue;
            }
            if (!_exchangeToQuote.ContainsKey(commodity.Exchange))
            {
                droppedUnsupportedExchange.Add($"{commodity.Symbol}@{commodity.Exchange}");
                continue;
            }
            candidateSecurities.Add(commodity);
        }

        if (droppedNoExchange.Count > 0)
            _logger.LogWarning(
                "AlphaVantage skipped {Count} securities with no exchange: {Symbols}",
                droppedNoExchange.Count, string.Join(", ", droppedNoExchange));

        if (droppedUnsupportedExchange.Count > 0)
            _logger.LogInformation(
                "AlphaVantage skipped {Count} securities on unsupported exchanges: {Symbols}",
                droppedUnsupportedExchange.Count, string.Join(", ", droppedUnsupportedExchange));

        var resolved = new List<(Commodity Security, Commodity Quote)>();
        var droppedNoQuote = new List<string>();
        foreach (var security in candidateSecurities)
        {
            var quoteSymbol = _exchangeToQuote[security.Exchange!];
            if (quoteCurrenciesBySymbol.TryGetValue(quoteSymbol, out var quote))
                resolved.Add((security, quote));
            else
                droppedNoQuote.Add($"{security.Symbol}@{security.Exchange} (needs {quoteSymbol})");
        }

        if (droppedNoQuote.Count > 0)
            _logger.LogWarning(
                "AlphaVantage skipped {Count} securities because the quote currency commodity was not in the request: {Symbols}",
                droppedNoQuote.Count, string.Join(", ", droppedNoQuote));

        if (resolved.Count == 0)
        {
            _logger.LogWarning(
                "AlphaVantage import returning empty: 0 of {CommodityCount} commodities resolved to a (security, quote) pair",
                request.Commodities.Count);
            return PriceImportResult.Success(Array.Empty<PriceObservation>());
        }

        _logger.LogInformation(
            "AlphaVantage resolved {ResolvedCount} (security, quote) pairs to fetch",
            resolved.Count);

        var observations = new List<PriceObservation>();
        var failed = new List<string>();
        var successCount = 0;

        for (var i = 0; i < resolved.Count; i++)
        {
            var (security, quote) = resolved[i];
            var apiSymbol = ToApiSymbol(security);
            var observationsBefore = observations.Count;

            if (i > 0)
                await _delay(_interCallDelay, ct);

            var dailyResponse = await _client.GetDailyTimeSeriesAsync(
                baseUri, apiSymbol, _options.ApiKey!, "compact", ct);

            if (dailyResponse is null)
            {
                _logger.LogWarning(
                    "AlphaVantage daily fetch for {Symbol} (api={ApiSymbol}) returned null body",
                    security.Symbol, apiSymbol);
                failed.Add(security.Symbol);
                continue;
            }

            if (TryGetApiBlockMessage(dailyResponse.Information, dailyResponse.Note) is { } dailyBlock)
            {
                _logger.LogWarning(
                    "AlphaVantage daily fetch blocked at {Symbol} after {SuccessCount}/{ResolvedCount}: {Block}",
                    security.Symbol, successCount, resolved.Count, dailyBlock);
                var warning = $"AlphaVantage stopped responding after {successCount} of {resolved.Count} securities: {dailyBlock}";
                return PriceImportResult.Partial(observations, warning);
            }

            if (!string.IsNullOrEmpty(dailyResponse.ErrorMessage))
            {
                _logger.LogWarning(
                    "AlphaVantage daily fetch for {Symbol} (api={ApiSymbol}) returned error: {Error}",
                    security.Symbol, apiSymbol, dailyResponse.ErrorMessage);
                failed.Add(security.Symbol);
                continue;
            }

            DateTimeOffset? dailyEarliest = null;
            var dailyParsed = 0;
            if (dailyResponse.TimeSeriesDaily is not null)
            {
                foreach (var (dateStr, dayEl) in dailyResponse.TimeSeriesDaily)
                {
                    if (TryParseDate(dateStr) is not { } priceAt)
                        continue;
                    dailyParsed++;
                    if (dailyEarliest is null || priceAt < dailyEarliest.Value)
                        dailyEarliest = priceAt;

                    var observation = TryParseObservation(security.Id, quote.Id, priceAt, dayEl, request.FromAt, request.ToAt);
                    if (observation is not null)
                        observations.Add(observation);
                }
            }

            var dailyInWindow = observations.Count - observationsBefore;
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "AlphaVantage daily for {Symbol}: parsed={Parsed}, inWindow={InWindow}, earliest={Earliest:o}",
                    security.Symbol, dailyParsed, dailyInWindow, dailyEarliest);

            if (dailyParsed == 0)
                _logger.LogWarning(
                    "AlphaVantage daily for {Symbol} (api={ApiSymbol}) returned 0 parsable rows (TimeSeriesDaily {Present})",
                    security.Symbol, apiSymbol, dailyResponse.TimeSeriesDaily is null ? "missing" : "present");
            else if (dailyInWindow == 0)
                _logger.LogWarning(
                    "AlphaVantage daily for {Symbol} parsed {Parsed} rows but none fell inside window {FromAt:o}..{ToAt:o} (earliest={Earliest:o})",
                    security.Symbol, dailyParsed, request.FromAt, request.ToAt, dailyEarliest);

            if (request.AnchorSource == PriceImportAnchorSource.ExplicitOverride
                && dailyEarliest is { } earliest
                && request.FromAt < earliest)
            {
                await _delay(_interCallDelay, ct);

                var weeklyObservationsBefore = observations.Count;
                var weeklyResponse = await _client.GetWeeklyTimeSeriesAsync(
                    baseUri, apiSymbol, _options.ApiKey!, ct);

                if (weeklyResponse is null)
                {
                    _logger.LogWarning(
                        "AlphaVantage weekly fetch for {Symbol} (api={ApiSymbol}) returned null body",
                        security.Symbol, apiSymbol);
                    successCount++;
                    continue;
                }

                if (TryGetApiBlockMessage(weeklyResponse.Information, weeklyResponse.Note) is { } weeklyBlock)
                {
                    _logger.LogWarning(
                        "AlphaVantage weekly fetch blocked at {Symbol} after {SuccessCount}/{ResolvedCount}: {Block}",
                        security.Symbol, successCount, resolved.Count, weeklyBlock);
                    var warning = $"AlphaVantage stopped responding after {successCount} of {resolved.Count} securities: {weeklyBlock}";
                    return PriceImportResult.Partial(observations, warning);
                }

                if (!string.IsNullOrEmpty(weeklyResponse.ErrorMessage))
                {
                    _logger.LogWarning(
                        "AlphaVantage weekly fetch for {Symbol} (api={ApiSymbol}) returned error: {Error}",
                        security.Symbol, apiSymbol, weeklyResponse.ErrorMessage);
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

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(
                        "AlphaVantage weekly back-fill for {Symbol}: added {Added}",
                        security.Symbol, observations.Count - weeklyObservationsBefore);
            }

            successCount++;
        }

        if (failed.Count > 0)
        {
            _logger.LogWarning(
                "AlphaVantage import finished partial: {ObservationCount} observations, {FailedCount} symbols skipped: {Symbols}",
                observations.Count, failed.Count, string.Join(", ", failed));
            return PriceImportResult.Partial(observations, $"Skipped: {string.Join(", ", failed)}");
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "AlphaVantage import finished: {ObservationCount} observations across {ResolvedCount} securities",
                observations.Count, resolved.Count);
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
}
