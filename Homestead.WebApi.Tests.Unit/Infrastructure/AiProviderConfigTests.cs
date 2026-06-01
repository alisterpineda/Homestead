using Homestead.WebApi.Infrastructure.Ai.Configurations;

namespace Homestead.WebApi.Tests.Unit.Infrastructure;

public class AiProviderConfigTests
{
    [Fact]
    public void TryParse_ValidJson_ReturnsTrue()
    {
        var json = """{"endpoint":"https://test.openai.azure.com","apiKey":"key123","model":"gpt-4o"}""";

        var result = AiProviderConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.openai.azure.com", config!.Endpoint);
        Assert.Equal("key123", config.ApiKey);
        Assert.Equal("gpt-4o", config.Model);
    }

    [Fact]
    public void TryParse_NullJson_ReturnsFalse()
    {
        var result = AiProviderConfig.TryParse(null, out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_EmptyJson_ReturnsFalse()
    {
        var result = AiProviderConfig.TryParse("", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_InvalidJson_ReturnsFalse()
    {
        var result = AiProviderConfig.TryParse("not json", out var config);

        Assert.False(result);
        Assert.Null(config);
    }

    [Fact]
    public void TryParse_PartialJson_ReturnsTrue_WithNulls()
    {
        var json = """{"endpoint":"https://test.openai.azure.com"}""";

        var result = AiProviderConfig.TryParse(json, out var config);

        Assert.True(result);
        Assert.NotNull(config);
        Assert.Equal("https://test.openai.azure.com", config!.Endpoint);
        Assert.Null(config.ApiKey);
        Assert.Null(config.Model);
    }

    [Fact]
    public void TryParse_MaxInputChars_IsParsed()
    {
        AiProviderConfig.TryParse("""{"apiKey":"k","model":"m","maxInputChars":40000}""", out var config);

        Assert.NotNull(config);
        Assert.Equal(40_000, config!.MaxInputChars);
    }

    [Fact]
    public void TryParse_Omitted_MaxInputChars_IsNull()
    {
        AiProviderConfig.TryParse("""{"apiKey":"k","model":"m"}""", out var config);

        Assert.NotNull(config);
        Assert.Null(config!.MaxInputChars);
    }

    [Fact]
    public void ResolveMaxInputChars_AboveFloor_UsesConfiguredValue()
    {
        var config = new AiProviderConfig { MaxInputChars = 40_000 };
        Assert.Equal(40_000, config.ResolveMaxInputChars());
    }

    [Fact]
    public void ResolveMaxInputChars_Null_UsesDefault()
    {
        var config = new AiProviderConfig { MaxInputChars = null };
        Assert.Equal(AiProviderConfig.DefaultMaxInputChars, config.ResolveMaxInputChars());
    }

    [Fact]
    public void ResolveMaxInputChars_BelowFloor_FallsBackToDefault()
    {
        // Defends the bypass paths (seeded rows, background jobs) the admin validator never sees: a degenerate
        // sub-floor value must not starve the prompt — it falls back to the default rather than being used.
        var config = new AiProviderConfig { MaxInputChars = 10 };
        Assert.Equal(AiProviderConfig.DefaultMaxInputChars, config.ResolveMaxInputChars());
    }
}
