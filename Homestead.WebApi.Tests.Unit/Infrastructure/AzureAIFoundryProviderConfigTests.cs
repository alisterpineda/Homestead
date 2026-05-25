using Homestead.WebApi.Infrastructure.AiClassification.Configurations;

namespace Homestead.WebApi.Tests.Unit.Infrastructure;

public class AzureAIFoundryProviderConfigTests
{
    [Fact]
    public void TryParse_ValidJson_ReturnsTrue()
    {
        var json = """{"endpoint":"https://test.openai.azure.com","apiKey":"key123","model":"gpt-4o"}""";

        var result = AzureAIFoundryProviderConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.openai.azure.com", config!.Endpoint);
        Assert.Equal("key123", config.ApiKey);
        Assert.Equal("gpt-4o", config.Model);
    }

    [Fact]
    public void TryParse_NullJson_ReturnsFalse()
    {
        var result = AzureAIFoundryProviderConfig.TryParse(null, out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_EmptyJson_ReturnsFalse()
    {
        var result = AzureAIFoundryProviderConfig.TryParse("", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var result = AzureAIFoundryProviderConfig.TryParse("not json", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_PartialJson_ReturnsTrue_WithNulls()
    {
        var json = """{"endpoint":"https://test.openai.azure.com"}""";

        var result = AzureAIFoundryProviderConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.openai.azure.com", config!.Endpoint);
        Assert.Null(config.ApiKey);
        Assert.Null(config.Model);
    }
}
