using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.PriceProviders.AlphaVantage;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;
using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.PriceProviders;

public class PriceSourceConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void SectionFor_UsesEnumName()
    {
        Assert.Equal("PriceSources:AlphaVantage", PriceSourceConfiguration.SectionFor(CommodityPriceSource.AlphaVantage));
        Assert.Equal("PriceSources:BankOfCanada", PriceSourceConfiguration.SectionFor(CommodityPriceSource.BankOfCanada));
    }

    [Fact]
    public void Read_NoSection_IsDisabledWithDefaults()
    {
        var options = PriceSourceConfiguration.Read<AlphaVantageOptions>(Config(), CommodityPriceSource.AlphaVantage);

        Assert.False(options.Enabled);
        Assert.Null(options.ApiKey);
        Assert.Equal(AlphaVantageOptions.DefaultBaseUrl, options.ResolveBaseUrl());
    }

    [Fact]
    public void Read_BindsValues()
    {
        var config = Config(
            ("PriceSources:AlphaVantage:Enabled", "true"),
            ("PriceSources:AlphaVantage:ApiKey", "abc"),
            ("PriceSources:AlphaVantage:BaseUrl", " https://example.test/query "),
            ("PriceSources:BankOfCanada:Enabled", "true"));

        var alphaVantage = PriceSourceConfiguration.Read<AlphaVantageOptions>(config, CommodityPriceSource.AlphaVantage);
        Assert.True(alphaVantage.Enabled);
        Assert.Equal("abc", alphaVantage.ApiKey);
        Assert.Equal("https://example.test/query", alphaVantage.ResolveBaseUrl());

        var bankOfCanada = PriceSourceConfiguration.Read<BankOfCanadaOptions>(config, CommodityPriceSource.BankOfCanada);
        Assert.True(bankOfCanada.Enabled);
        Assert.Equal(BankOfCanadaOptions.DefaultBaseUrl, bankOfCanada.ResolveBaseUrl());
    }

    [Fact]
    public void Read_EmptyBaseUrl_ResolvesToDefault()
    {
        var config = Config(("PriceSources:BankOfCanada:BaseUrl", ""));
        var options = PriceSourceConfiguration.Read<BankOfCanadaOptions>(config, CommodityPriceSource.BankOfCanada);
        Assert.Equal(BankOfCanadaOptions.DefaultBaseUrl, options.ResolveBaseUrl());
    }

    [Fact]
    public void Validate_AlphaVantage_RequiresApiKey_IgnoresEnabled()
    {
        Assert.Equal("ApiKey is required.",
            PriceSourceConfiguration.Validate(new AlphaVantageOptions { Enabled = true }));
        Assert.Equal("ApiKey is required.",
            PriceSourceConfiguration.Validate(new AlphaVantageOptions { Enabled = false, ApiKey = " " }));
        Assert.Null(PriceSourceConfiguration.Validate(new AlphaVantageOptions { Enabled = false, ApiKey = "abc" }));
    }

    [Fact]
    public void Validate_AlphaVantage_RejectsRelativeBaseUrl()
    {
        var error = PriceSourceConfiguration.Validate(new AlphaVantageOptions { ApiKey = "abc", BaseUrl = "not a url" });
        Assert.Equal("BaseUrl must be an absolute URI.", error);
    }

    [Fact]
    public void Validate_BankOfCanada_OnlyChecksBaseUrl()
    {
        Assert.Null(PriceSourceConfiguration.Validate(new BankOfCanadaOptions()));
        Assert.Null(PriceSourceConfiguration.Validate(new BankOfCanadaOptions { BaseUrl = "https://example.test/" }));
        Assert.Equal("BaseUrl must be an absolute URI.",
            PriceSourceConfiguration.Validate(new BankOfCanadaOptions { BaseUrl = "not a url" }));
    }
}
