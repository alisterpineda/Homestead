using System.Net;
using System.Text;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.PriceProviders.BankOfCanada;

public class BankOfCanadaPriceProviderTests
{
    private static (BankOfCanadaPriceProvider provider, FakeHttpHandler handler) CreateSut()
    {
        var handler = new FakeHttpHandler();
        var http = new HttpClient(handler) { BaseAddress = null };
        var client = new BankOfCanadaValetClient(http);
        return (new BankOfCanadaPriceProvider(client), handler);
    }

    [Fact]
    public void Source_IsBankOfCanada()
    {
        var (provider, _) = CreateSut();
        Assert.Equal(CommodityPriceSource.BankOfCanada, provider.Source);
    }

    [Fact]
    public void ConfigSchema_DeclaresBaseUrl()
    {
        var (provider, _) = CreateSut();
        Assert.Single(provider.ConfigSchema.Fields);
        Assert.Equal("BaseUrl", provider.ConfigSchema.Fields[0].Key);
    }

    [Fact]
    public async Task ImportAsync_NoCommodities_ReturnsEmpty()
    {
        var (provider, handler) = CreateSut();
        var result = await provider.ImportAsync(
            new PriceImportRequest(Array.Empty<Commodity>(),
                DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null),
            TestContext.Current.CancellationToken);
        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ImportAsync_BuildsSeriesNameAndParsesObservation()
    {
        var (provider, handler) = CreateSut();
        handler.ResponseBody = """
            {
              "observations": [
                { "d": "2026-04-28", "FXUSDCAD": { "v": "1.3621" } }
              ]
            }
            """;

        var cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency };
        var usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "US Dollar", Kind = CommodityKind.Currency };

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { cad, usd },
                new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero),
                null),
            TestContext.Current.CancellationToken);

        var obs = Assert.Single(result);
        Assert.Equal(usd.Id, obs.CommodityId);
        Assert.Equal(cad.Id, obs.QuoteCurrencyId);
        Assert.Equal(new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero), obs.PriceAt);
        Assert.Equal(1.3621m, obs.Rate);

        Assert.Contains("FXUSDCAD", handler.LastRequestUrl);
    }

    [Fact]
    public async Task ImportAsync_UnsupportedSymbol_Skipped()
    {
        var (provider, handler) = CreateSut();
        handler.ResponseBody = """{"observations":[]}""";

        var cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "Canadian Dollar", Kind = CommodityKind.Currency };
        var xyz = new Commodity { Id = Guid.NewGuid(), Symbol = "XYZ", Name = "Made up", Kind = CommodityKind.Currency };

        var result = await provider.ImportAsync(
            new PriceImportRequest(new[] { cad, xyz },
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, null),
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Equal(0, handler.CallCount); // CAD alone yields no series → no call
    }

    [Fact]
    public async Task ImportAsync_UsesConfigBaseUrl()
    {
        var (provider, handler) = CreateSut();
        handler.ResponseBody = """{"observations":[]}""";

        var cad = new Commodity { Id = Guid.NewGuid(), Symbol = "CAD", Name = "CAD", Kind = CommodityKind.Currency };
        var usd = new Commodity { Id = Guid.NewGuid(), Symbol = "USD", Name = "USD", Kind = CommodityKind.Currency };

        var customBaseUrl = "https://example.test/valet/";
        var configJson = $$"""{ "BaseUrl": "{{customBaseUrl}}" }""";

        await provider.ImportAsync(
            new PriceImportRequest(new[] { cad, usd },
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, configJson),
            TestContext.Current.CancellationToken);

        Assert.StartsWith(customBaseUrl, handler.LastRequestUrl);
    }

    private class FakeHttpHandler : HttpMessageHandler
    {
        public string ResponseBody { get; set; } = """{"observations":[]}""";
        public string LastRequestUrl { get; private set; } = string.Empty;
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUrl = request.RequestUri?.ToString() ?? string.Empty;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
