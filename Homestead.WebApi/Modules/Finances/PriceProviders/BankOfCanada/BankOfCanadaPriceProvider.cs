using Homestead.WebApi.Domain.Finances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

public class BankOfCanadaPriceProvider : IPriceProvider
{
    private readonly BankOfCanadaValetClient _client;
    private readonly BankOfCanadaOptions _options;
    private readonly ILogger<BankOfCanadaPriceProvider> _logger;

    public BankOfCanadaPriceProvider(
        BankOfCanadaValetClient client,
        BankOfCanadaOptions options,
        ILogger<BankOfCanadaPriceProvider>? logger = null)
    {
        _client = client;
        _options = options;
        _logger = logger ?? NullLogger<BankOfCanadaPriceProvider>.Instance;
        // Computed once at construction: config is static, so the verdict never changes.
        ConfigurationError = PriceSourceConfiguration.Validate(options);
    }

    private static readonly string[] _supportedSymbols =
    [
        "USD", "EUR", "GBP", "JPY", "AUD", "CHF", "CNY", "HKD", "INR", "MXN",
        "NZD", "SEK", "KRW", "NOK", "SGD", "BRL", "CLP", "COP", "IDR", "MYR",
        "PEN", "PHP", "RUB", "TWD", "THB", "TRY", "VND", "ZAR", "AED",
    ];

    public CommodityPriceSource Source => CommodityPriceSource.BankOfCanada;

    public bool IsEnabled => _options.Enabled;
    public string? ConfigurationError { get; }
    public bool IsConfigured => IsEnabled && ConfigurationError is null;
    // The Valet API takes no key.
    public bool? HasApiKey => null;
    public string BaseUrl => _options.ResolveBaseUrl();

    public IReadOnlyList<PriceProviderSupportedPair> SupportedPairs { get; } =
        _supportedSymbols.Select(s => new PriceProviderSupportedPair(s, null, "CAD")).ToArray();

    public IReadOnlyList<string> CoveredSecurityExchanges { get; } = Array.Empty<string>();

    public IReadOnlyList<string> CoveredQuoteCurrencies { get; } = Array.Empty<string>();

    public TimeSpan RevisionLookback => TimeSpan.FromDays(5);

    public async Task<PriceImportResult> ImportAsync(PriceImportRequest request, CancellationToken ct)
    {
        _logger.LogInformation(
            "BankOfCanada import starting: {CommodityCount} commodities, window {FromAt:o}..{ToAt:o}",
            request.Commodities.Count, request.FromAt, request.ToAt);

        if (request.Commodities.Count == 0)
        {
            _logger.LogWarning("BankOfCanada import returning empty: no commodities in request");
            return PriceImportResult.Success(Array.Empty<PriceObservation>());
        }

        if (!IsConfigured)
            throw new InvalidOperationException(
                $"Price source '{Source}' is unavailable: {ConfigurationError ?? "disabled"}");

        var baseUri = new Uri(BaseUrl, UriKind.Absolute);

        var supported = new HashSet<string>(_supportedSymbols, StringComparer.OrdinalIgnoreCase);

        // Build per-series mapping: series name → commodity Id
        var seriesToCommodity = new Dictionary<string, Commodity>(StringComparer.OrdinalIgnoreCase);
        var droppedUnsupported = new List<string>();
        var skippedNonCurrency = 0;
        Commodity? cad = null;
        foreach (var commodity in request.Commodities)
        {
            if (commodity.Kind != CommodityKind.Currency)
            {
                skippedNonCurrency++;
                continue;
            }
            if (string.Equals(commodity.Symbol, "CAD", StringComparison.OrdinalIgnoreCase))
            {
                cad = commodity;
                continue;
            }
            if (!supported.Contains(commodity.Symbol))
            {
                droppedUnsupported.Add(commodity.Symbol);
                continue;
            }
            var seriesName = $"FX{commodity.Symbol.ToUpperInvariant()}CAD";
            seriesToCommodity[seriesName] = commodity;
        }

        if (skippedNonCurrency > 0)
            _logger.LogInformation(
                "BankOfCanada skipped {Count} non-currency commodities (provider only handles currencies)",
                skippedNonCurrency);

        if (droppedUnsupported.Count > 0)
            _logger.LogInformation(
                "BankOfCanada skipped {Count} currencies not in the supported Valet series list: {Symbols}",
                droppedUnsupported.Count, string.Join(", ", droppedUnsupported));

        if (cad is null)
        {
            _logger.LogWarning(
                "BankOfCanada import returning empty: CAD currency commodity was not in the request — all Valet series quote against CAD, so nothing can be imported");
            return PriceImportResult.Success(Array.Empty<PriceObservation>());
        }

        if (seriesToCommodity.Count == 0)
        {
            _logger.LogWarning(
                "BankOfCanada import returning empty: no supported foreign-currency commodities in the request (CAD was present, but no peers to quote against it)");
            return PriceImportResult.Success(Array.Empty<PriceObservation>());
        }

        _logger.LogInformation(
            "BankOfCanada resolved {SeriesCount} series to fetch: {Series}",
            seriesToCommodity.Count, string.Join(", ", seriesToCommodity.Keys));

        var fromDate = DateOnly.FromDateTime(request.FromAt.UtcDateTime);
        var toDate = DateOnly.FromDateTime(request.ToAt.UtcDateTime);

        var raw = await _client.GetObservationsAsync(baseUri, seriesToCommodity.Keys.ToArray(), fromDate, toDate, ct);

        var observations = new List<PriceObservation>(raw.Count);
        var unmatchedSeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in raw)
        {
            if (!seriesToCommodity.TryGetValue(item.SeriesName, out var commodity))
            {
                unmatchedSeries.Add(item.SeriesName);
                continue;
            }
            observations.Add(new PriceObservation(commodity.Id, cad.Id, item.PriceAt, item.Value));
        }

        if (unmatchedSeries.Count > 0)
            _logger.LogWarning(
                "BankOfCanada Valet returned {Count} observations for series we did not request: {Series}",
                unmatchedSeries.Count, string.Join(", ", unmatchedSeries));

        if (raw.Count == 0)
            _logger.LogWarning(
                "BankOfCanada Valet returned 0 observations for {SeriesCount} series in window {From:yyyy-MM-dd}..{To:yyyy-MM-dd}",
                seriesToCommodity.Count, fromDate, toDate);

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation(
                "BankOfCanada import finished: {ObservationCount} observations across {SeriesCount} series (raw rows={RawCount})",
                observations.Count, seriesToCommodity.Count, raw.Count);

        return PriceImportResult.Success(observations);
    }
}
