using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Tests.Unit.TextExtraction;

public class AzureDocumentIntelligenceEngineConfigTests
{
    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        var result = AzureDocumentIntelligenceEngineConfig.TryParse(null, out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_Empty_ReturnsFalse()
    {
        var result = AzureDocumentIntelligenceEngineConfig.TryParse("", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_Whitespace_ReturnsFalse()
    {
        var result = AzureDocumentIntelligenceEngineConfig.TryParse("   ", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var result = AzureDocumentIntelligenceEngineConfig.TryParse("not-json", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_ValidConfig_ReturnsTrue()
    {
        var json = """{"endpoint":"https://test.cognitiveservices.azure.com","apiKey":"key123"}""";

        var result = AzureDocumentIntelligenceEngineConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.cognitiveservices.azure.com", config!.Endpoint);
        Assert.Equal("key123", config.ApiKey);
    }

    [Fact]
    public void TryParse_MissingEndpoint_ParsesWithNull()
    {
        var json = """{"apiKey":"key123"}""";

        var result = AzureDocumentIntelligenceEngineConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Null(config!.Endpoint);
        Assert.Equal("key123", config.ApiKey);
    }

    [Fact]
    public void TryParse_MissingApiKey_ParsesWithNull()
    {
        var json = """{"endpoint":"https://test.cognitiveservices.azure.com"}""";

        var result = AzureDocumentIntelligenceEngineConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.cognitiveservices.azure.com", config!.Endpoint);
        Assert.Null(config.ApiKey);
    }

    [Fact]
    public void TryParse_EmptyObject_ParsesWithNulls()
    {
        var result = AzureDocumentIntelligenceEngineConfig.TryParse("{}", out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Null(config!.Endpoint);
        Assert.Null(config.ApiKey);
    }
}
