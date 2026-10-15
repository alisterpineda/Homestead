using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiProviderConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Unlike ReadEngines (which always seeds the built-in tesseract), there is no zero-config AI provider:
    // no config means no providers, which is the graceful "AI is not configured" no-op.
    [Fact]
    public void ReadProviders_NoConfig_ReturnsEmpty_NoBuiltInSeeding()
    {
        Assert.Empty(AiProviderConfiguration.ReadProviders(Config([])));
    }

    [Fact]
    public void ReadProviders_NamedInstances_AreAllRead()
    {
        var providers = AiProviderConfiguration.ReadProviders(Config(new()
        {
            ["Ai:Providers:local:Type"] = "OpenAiCompatible",
            ["Ai:Providers:local:Endpoint"] = "http://homeserver:11434/v1",
            ["Ai:Providers:local:Model"] = "gemma-4-e4b",
            ["Ai:Providers:foundry-gpt5:Type"] = "AzureFoundry",
            ["Ai:Providers:foundry-gpt5:Endpoint"] = "https://res.openai.azure.com",
            ["Ai:Providers:foundry-gpt5:ApiKey"] = "k",
            ["Ai:Providers:foundry-gpt5:Model"] = "gpt-5-mini",
        }));

        var byKey = providers.ToDictionary(p => p.Key, p => p.Options);

        Assert.Equal(["foundry-gpt5", "local"], byKey.Keys.Order());
        Assert.Equal(AiProviderType.OpenAiCompatible, byKey["local"].Type);
        Assert.Equal("gemma-4-e4b", byKey["local"].Model);
        // One Azure resource, one deployment name — Model carries the deployment for AzureFoundry.
        Assert.Equal(AiProviderType.AzureFoundry, byKey["foundry-gpt5"].Type);
        Assert.Equal("gpt-5-mini", byKey["foundry-gpt5"].Model);
        Assert.Null(AiProviderConfiguration.Validate("local", byKey["local"]));
        Assert.Null(AiProviderConfiguration.Validate("foundry-gpt5", byKey["foundry-gpt5"]));
    }

    // What Ai__Providers__local__Endpoint arrives as from a Docker deployment or the AppHost walk.
    [Fact]
    public void ReadProviders_EnvVarStyleKeys_BindOntoTheNamedInstance()
    {
        var providers = AiProviderConfiguration.ReadProviders(Config(new()
        {
            ["Ai:Providers:local:Type"] = "OpenAiCompatible",
            ["Ai:Providers:local:Endpoint"] = "http://homeserver:11434/v1",
            ["Ai:Providers:local:Model"] = "gemma-4-e4b",
            ["Ai:Providers:local:MaxInputChars"] = "20000",
        }));

        var (key, options) = Assert.Single(providers);
        Assert.Equal("local", key);
        Assert.Equal(20_000, options.MaxInputChars);
    }

    // No built-in key means no implied type anywhere: EVERY provider must state its Type, or it reads as
    // unavailable rather than silently binding to default(0) = OpenAiCompatible under the operator's name.
    [Fact]
    public void Validate_MissingType_ReportsTypeRequired_ForAnyKey()
    {
        var error = AiProviderConfiguration.Validate("local", new AiProviderOptions
        {
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
        });

        Assert.Equal("Type is required (one of: OpenAiCompatible, AzureFoundry, Anthropic).", error);
    }

    [Theory]
    [InlineData(null, "m", "Endpoint is required.")]
    [InlineData("   ", "m", "Endpoint is required.")]
    [InlineData("not-a-uri", "m", "Endpoint must be an absolute URI.")]
    [InlineData("http://homeserver:11434/v1", null, "Model is required.")]
    public void Validate_IncompleteOpenAiCompatible_ReportsReason(string? endpoint, string? model, string expected)
    {
        var error = AiProviderConfiguration.Validate("local", new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatible,
            Endpoint = endpoint,
            Model = model,
        });

        Assert.Equal(expected, error);
    }

    // ApiKey is deliberately optional for OpenAiCompatible — local runtimes ignore it.
    [Fact]
    public void Validate_OpenAiCompatibleWithoutApiKey_IsValid()
    {
        Assert.Null(AiProviderConfiguration.Validate("local", new AiProviderOptions
        {
            Type = AiProviderType.OpenAiCompatible,
            Endpoint = "http://homeserver:11434/v1",
            Model = "gemma-4-e4b",
        }));
    }

    [Theory]
    [InlineData(null, "k", "m", "Endpoint is required.")]
    [InlineData("not-a-uri", "k", "m", "Endpoint must be an absolute URI.")]
    [InlineData("https://res.openai.azure.com", null, "m", "ApiKey is required.")]
    [InlineData("https://res.openai.azure.com", "k", null, "Model (the deployment name) is required.")]
    public void Validate_IncompleteAzureFoundry_ReportsReason(
        string? endpoint, string? apiKey, string? model, string expected)
    {
        var error = AiProviderConfiguration.Validate("foundry", new AiProviderOptions
        {
            Type = AiProviderType.AzureFoundry,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model,
        });

        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("not-a-uri", "k", "m", "Endpoint must be an absolute URI.")]
    [InlineData(null, null, "m", "ApiKey is required.")]
    [InlineData(null, "   ", "m", "ApiKey is required.")]
    [InlineData(null, "k", null, "Model is required.")]
    public void Validate_IncompleteAnthropic_ReportsReason(
        string? endpoint, string? apiKey, string? model, string expected)
    {
        var error = AiProviderConfiguration.Validate("claude", new AiProviderOptions
        {
            Type = AiProviderType.Anthropic,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Model = model,
        });

        Assert.Equal(expected, error);
    }

    // Endpoint only overrides the SDK's default base URL, so Anthropic is usable without one.
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("https://gateway.example.com")]
    public void Validate_AnthropicEndpointIsOptional(string? endpoint)
    {
        Assert.Null(AiProviderConfiguration.Validate("claude", new AiProviderOptions
        {
            Type = AiProviderType.Anthropic,
            Endpoint = endpoint,
            ApiKey = "k",
            Model = "claude-opus-5-5",
        }));
    }

    [Fact]
    public void ReadDefaultProviderKey_Blank_IsNull_SetValue_IsTrimmed()
    {
        Assert.Null(AiProviderConfiguration.ReadDefaultProviderKey(Config([])));
        Assert.Null(AiProviderConfiguration.ReadDefaultProviderKey(Config(new()
        {
            ["Ai:DefaultProvider"] = "   ",
        })));
        Assert.Equal("local", AiProviderConfiguration.ReadDefaultProviderKey(Config(new()
        {
            ["Ai:DefaultProvider"] = " local ",
        })));
    }

    // The key side of the resolution chain: configured default -> sole defined key -> null.
    [Fact]
    public void ResolveDefaultKey_ConfiguredDefault_WinsOverSoleProvider()
    {
        Assert.Equal("foundry", AiProviderConfiguration.ResolveDefaultKey("foundry", ["local"]));
    }

    // A dangling default is returned VERBATIM — turning it into unavailable-with-reason is the resolver's
    // job; substituting the sole healthy provider here would be exactly the silent substitution this
    // design refuses.
    [Fact]
    public void ResolveDefaultKey_DanglingDefault_ReturnedVerbatim_NotSubstituted()
    {
        Assert.Equal("typo", AiProviderConfiguration.ResolveDefaultKey("typo", ["local", "foundry"]));
    }

    [Fact]
    public void ResolveDefaultKey_NoDefault_SoleKeyWins_MultipleKeysNull_EmptyNull()
    {
        Assert.Equal("local", AiProviderConfiguration.ResolveDefaultKey(null, ["local"]));
        Assert.Null(AiProviderConfiguration.ResolveDefaultKey(null, ["local", "foundry"]));
        Assert.Null(AiProviderConfiguration.ResolveDefaultKey(null, []));
    }

    // --- Knob resolution (moved from AiOptions; the old GetAiStatusHandler budget tests live on here) ---

    [Fact]
    public void ResolveMaxInputChars_UnsetDefault_BelowFloorDefault_AboveFloorConfigured()
    {
        Assert.Equal(AiProviderOptions.DefaultMaxInputChars,
            new AiProviderOptions { MaxInputChars = null }.ResolveMaxInputChars());
        Assert.Equal(AiProviderOptions.DefaultMaxInputChars,
            new AiProviderOptions { MaxInputChars = AiProviderOptions.MinInputChars - 1 }.ResolveMaxInputChars());
        Assert.Equal(20_000,
            new AiProviderOptions { MaxInputChars = 20_000 }.ResolveMaxInputChars());
    }

    [Fact]
    public void ResolveStatementMaxInputChars_UnsetDefault_BelowFloorDefault_AboveFloorConfigured()
    {
        Assert.Equal(AiProviderOptions.DefaultStatementMaxInputChars,
            new AiProviderOptions { StatementMaxInputChars = null }.ResolveStatementMaxInputChars());
        Assert.Equal(AiProviderOptions.DefaultStatementMaxInputChars,
            new AiProviderOptions { StatementMaxInputChars = AiProviderOptions.MinInputChars - 1 }.ResolveStatementMaxInputChars());
        Assert.Equal(200_000,
            new AiProviderOptions { StatementMaxInputChars = 200_000 }.ResolveStatementMaxInputChars());
    }

    [Fact]
    public void ResolveImageRenderMaxEdge_SubFloorDefault_InRangeKept_AboveCeilingClamped()
    {
        Assert.Equal(AiProviderOptions.DefaultImageRenderMaxEdge,
            new AiProviderOptions { ImageRenderMaxEdge = AiProviderOptions.MinImageRenderMaxEdge - 1 }.ResolveImageRenderMaxEdge());
        Assert.Equal(2048,
            new AiProviderOptions { ImageRenderMaxEdge = 2048 }.ResolveImageRenderMaxEdge());
        Assert.Equal(AiProviderOptions.MaxImageRenderMaxEdge,
            new AiProviderOptions { ImageRenderMaxEdge = 10_000 }.ResolveImageRenderMaxEdge());
    }
}
