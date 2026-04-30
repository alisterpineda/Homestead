using System.Net;
using System.Text;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.PriceProviders.AlphaVantage;

public class AlphaVantagePriceProviderTests
{
    private static (AlphaVantagePriceProvider provider, FakeHttpHandler handler, List<TimeSpan> delays) CreateSut()
    {
        var handler = new FakeHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = null };
        var client = new AlphaVantageClient(http);
        var delays = new List<TimeSpan>();
        var provider = new AlphaVantagePriceProvider(client, (ts, _) =>
        {
            delays.Add(ts);
            return Task.CompletedTask;
        });
        return (provider, handler, delays);
    }

    private static string ConfigJson(string apiKey = "TEST-KEY", string? baseUrl = null) =>
        baseUrl is null
            ? $$"""{ "ApiKey": "{{apiKey}}" }"""
            : $$"""{ "ApiKey": "{{apiKey}}", "BaseUrl": "{{baseUrl}}" }""";

    private static Commodity Currency(string symbol) =>
        new() { Id = Guid.NewGuid(), Symbol = symbol, Name = symbol, Kind = CommodityKind.Currency };

    private static Commodity Security(string symbol, string? exchange) =>
        new() { Id = Guid.NewGuid(), Symbol = symbol, Name = symbol, Kind = CommodityKind.Security, Exchange = exchange };

    private static string DailyJson(params (string Date, string Close)[] rows)
    {
        var sb = new StringBuilder();
        sb.Append("{ \"Time Series (Daily)\": {");
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(rows[i].Date).Append("\": {");
            sb.Append("\"1. open\": \"0.0\", \"2. high\": \"0.0\", \"3. low\": \"0.0\", ");
            sb.Append("\"4. close\": \"").Append(rows[i].Close).Append("\", ");
            sb.Append("\"5. volume\": \"0\"");
            sb.Append('}');
        }
        sb.Append("} }");
        return sb.ToString();
    }

    [Fact]
    public void Source_IsAlphaVantage()
    {
        var (provider, _, _) = CreateSut();
        Assert.Equal(CommodityPriceSource.AlphaVantage, provider.Source);
    }

    [Fact]
    public void ConfigSchema_DeclaresApiKeyAsRequiredSecret_AndBaseUrlAsOptionalString()
    {
        var (provider, _, _) = CreateSut();
        var fields = provider.ConfigSchema.Fields;
        Assert.Equal(2, fields.Count);

        var apiKey = Assert.Single(fields, f => f.Key == "ApiKey");
        Assert.Equal(PriceProviderConfigFieldType.Secret, apiKey.Type);
        Assert.True(apiKey.IsRequired);
        Assert.Null(apiKey.DefaultValue);

        var baseUrl = Assert.Single(fields, f => f.Key == "BaseUrl");
        Assert.Equal(PriceProviderConfigFieldType.String, baseUrl.Type);
        Assert.False(baseUrl.IsRequired);
        Assert.Equal(AlphaVantageConfig.DefaultBaseUrl, baseUrl.DefaultValue);
    }

    [Fact]
    public void CoveredSecurityExchanges_IncludeUsExchangesAndTsx()
    {
        var (provider, _, _) = CreateSut();
        Assert.Equal(new[] { "NASDAQ", "NYSE", "AMEX", "TSX" }, provider.CoveredSecurityExchanges);
        Assert.Empty(provider.SupportedPairs);
    }

    [Fact]
    public void CoveredQuoteCurrencies_IncludeUsdAndCad()
    {
        var (provider, _, _) = CreateSut();
        Assert.Equal(new[] { "USD", "CAD" }, provider.CoveredQuoteCurrencies);
    }

    [Fact]
    public async Task ImportAsync_ParsesDailyClose_AndMapsToObservations()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "172.34"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Null(result.Warning);
        var obs = Assert.Single(result.Observations);
        Assert.Equal(aapl.Id, obs.CommodityId);
        Assert.Equal(usd.Id, obs.QuoteCurrencyId);
        Assert.Equal(new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero), obs.PriceAt);
        Assert.Equal(172.34m, obs.Rate);
        Assert.Contains("symbol=AAPL", handler.LastRequestUrl);
        Assert.Contains("apikey=TEST-KEY", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_SkipsCommoditiesOnUncoveredExchange()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var usd = Currency("USD");
        var lse = Security("VOD", "LSE");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, lse },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Observations);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_SkipsCurrenciesAndNonSecurityKinds()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var usd = Currency("USD");
        var eur = Currency("EUR");
        var aapl = Security("AAPL", "NASDAQ");

        await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, eur, aapl },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Contains("symbol=AAPL", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_TsxSecurity_AppendsTrtSuffixAndQuotesInCad()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "32.10"));

        var cad = Currency("CAD");
        var veqt = Security("VEQT", "TSX");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { cad, veqt },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        var obs = Assert.Single(result.Observations);
        Assert.Equal(veqt.Id, obs.CommodityId);
        Assert.Equal(cad.Id, obs.QuoteCurrencyId);
        Assert.Equal(32.10m, obs.Rate);
        Assert.Contains("symbol=VEQT.TRT", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_MixedExchanges_RoutesPerSecurityQuoteCurrency()
    {
        var (provider, handler, _) = CreateSut();
        handler.QueueResponse(DailyJson(("2026-04-29", "172.34")));
        handler.QueueResponse(DailyJson(("2026-04-29", "32.10")));

        var usd = Currency("USD");
        var cad = Currency("CAD");
        var aapl = Security("AAPL", "NASDAQ");
        var veqt = Security("VEQT", "TSX");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, cad, aapl, veqt },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Observations.Count);
        Assert.Contains(result.Observations, o => o.CommodityId == aapl.Id && o.QuoteCurrencyId == usd.Id);
        Assert.Contains(result.Observations, o => o.CommodityId == veqt.Id && o.QuoteCurrencyId == cad.Id);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_TsxSecurityWithoutHeldCad_SkipsSilently()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "32.10"));

        var usd = Currency("USD");
        var veqt = Security("VEQT", "TSX");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, veqt },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Observations);
        Assert.Null(result.Warning);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_NoUsdInRequest_ReturnsEmptySuccess()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var cad = Currency("CAD");
        var aapl = Security("AAPL", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { cad, aapl },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Observations);
        Assert.Null(result.Warning);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_InformationKey_ReturnsPartialAndStops()
    {
        var (provider, handler, _) = CreateSut();
        handler.QueueResponse(DailyJson(("2026-04-29", "172.34")));
        handler.QueueResponse("""{ "Information": "Thank you for using Alpha Vantage! Our standard API rate limit is 25 requests per day." }""");
        handler.QueueResponse(DailyJson(("2026-04-29", "999.99")));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");
        var msft = Security("MSFT", "NASDAQ");
        var goog = Security("GOOG", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl, msft, goog },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Warning);
        Assert.Contains("Rate limit reached after 1 of 3", result.Warning);
        var obs = Assert.Single(result.Observations);
        Assert.Equal(aapl.Id, obs.CommodityId);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_NoteKey_ReturnsPartialAndStops()
    {
        var (provider, handler, _) = CreateSut();
        handler.QueueResponse(DailyJson(("2026-04-29", "172.34")));
        handler.QueueResponse("""{ "Note": "Standard API call frequency exceeded." }""");

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");
        var msft = Security("MSFT", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl, msft },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Warning);
        Assert.Contains("Rate limit reached after 1 of 2", result.Warning);
        Assert.Single(result.Observations);
    }

    [Fact]
    public async Task ImportAsync_ErrorMessageKey_SkipsSymbolAndContinues()
    {
        var (provider, handler, _) = CreateSut();
        handler.QueueResponse("""{ "Error Message": "Invalid API call. Please retry or visit the documentation." }""");
        handler.QueueResponse(DailyJson(("2026-04-29", "172.34")));

        var usd = Currency("USD");
        var bogus = Security("BOGUS", "NASDAQ");
        var aapl = Security("AAPL", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, bogus, aapl },
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result.Warning);
        Assert.Contains("Skipped", result.Warning);
        Assert.Contains("BOGUS", result.Warning);
        var obs = Assert.Single(result.Observations);
        Assert.Equal(aapl.Id, obs.CommodityId);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_RespectsFromToWindow()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(
            ("2026-04-29", "100.00"),
            ("2026-04-28", "99.00"),
            ("2026-04-27", "98.00"),
            ("2026-04-20", "90.00"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl },
                new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero),
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Observations.Count);
        Assert.Contains(result.Observations, o => o.PriceAt == new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero) && o.Rate == 98.00m);
        Assert.Contains(result.Observations, o => o.PriceAt == new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero) && o.Rate == 99.00m);
    }

    [Fact]
    public async Task ImportAsync_ChoosesCompactOutputForRecentWindows()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Contains("outputsize=compact", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_ChoosesFullOutputForHistoricBackfill()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2020-01-02", "100.00"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl },
                DateTimeOffset.UtcNow.AddDays(-365), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Contains("outputsize=full", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_PacesCallsBetweenSymbols()
    {
        var (provider, handler, delays) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");
        var msft = Security("MSFT", "NASDAQ");
        var goog = Security("GOOG", "NASDAQ");

        await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl, msft, goog },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson()),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, delays.Count);
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromSeconds(12), d));
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_MissingApiKey_Throws()
    {
        var (provider, _, _) = CreateSut();

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.ImportAsync(
                new PriceImportRequest(new[] { usd, aapl },
                    DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                    """{ "BaseUrl": "https://example.test/query" }"""),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_UsesConfigBaseUrl()
    {
        var (provider, handler, _) = CreateSut();
        handler.ResponseBody = DailyJson(("2026-04-29", "100.00"));

        var usd = Currency("USD");
        var aapl = Security("AAPL", "NASDAQ");

        await provider.ImportAsync(
            new PriceImportRequest(new[] { usd, aapl },
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow,
                ConfigJson(baseUrl: "https://example.test/query")),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("https://example.test/query?", handler.LastRequestUrl);
    }

    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Queue<string> _queue = new();

        public string ResponseBody { get; set; } = """{ "Time Series (Daily)": {} }""";
        public string LastRequestUrl { get; private set; } = string.Empty;
        public int CallCount { get; private set; }

        public void QueueResponse(string body) => _queue.Enqueue(body);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUrl = request.RequestUri?.ToString() ?? string.Empty;
            var body = _queue.Count > 0 ? _queue.Dequeue() : ResponseBody;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
