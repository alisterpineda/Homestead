using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AiProviderTests
{
    private static AiProviderOptions ValidOpenAi() => new()
    {
        Type = AiProviderType.OpenAiCompatible,
        Endpoint = "http://homeserver:11434/v1",
        Model = "gemma-4-e4b",
    };

    private static AiProviderOptions ValidFoundry() => new()
    {
        Type = AiProviderType.AzureFoundry,
        Endpoint = "https://res.openai.azure.com",
        ApiKey = "k",
        Model = "gpt-5-mini",
    };

    [Fact]
    public void ConfigurationError_ComputedAtConstruction_DrivesIsConfigured()
    {
        var valid = new AiProvider("local", ValidOpenAi());
        Assert.True(valid.IsConfigured);
        Assert.Null(valid.ConfigurationError);

        var invalid = new AiProvider("broken", new AiProviderOptions { Type = AiProviderType.OpenAiCompatible });
        Assert.False(invalid.IsConfigured);
        Assert.Equal("Endpoint is required.", invalid.ConfigurationError);
    }

    [Fact]
    public void GetChatClient_Unavailable_ThrowsWithKeyAndReason()
    {
        var provider = new AiProvider("broken", new AiProviderOptions());

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetChatClient());

        Assert.Contains("broken", ex.Message);
        Assert.Contains("Type is required", ex.Message);
    }

    // Construction makes no network call, so building the client is offline-safe; the second call must
    // return the same instance (app-lifetime reuse — the whole point of the lazy field).
    [Fact]
    public void GetChatClient_OpenAiCompatible_BuildsOnce_AndMemoizes()
    {
        var provider = new AiProvider("local", ValidOpenAi());

        var first = provider.GetChatClient();
        var second = provider.GetChatClient();

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetChatClient_AzureFoundry_Builds()
    {
        var provider = new AiProvider("foundry", ValidFoundry());

        Assert.NotNull(provider.GetChatClient());
    }

    [Fact]
    public void HasApiKey_PresenceOnly()
    {
        Assert.False(new AiProvider("local", ValidOpenAi()).HasApiKey);
        Assert.True(new AiProvider("foundry", ValidFoundry()).HasApiKey);
        var blankKey = ValidOpenAi();
        blankKey.ApiKey = "   ";
        Assert.False(new AiProvider("local", blankKey).HasApiKey);
    }

    [Fact]
    public void Knobs_ExposeResolvedValues()
    {
        var options = ValidOpenAi();
        options.MaxInputChars = AiProviderOptions.MinInputChars - 1;   // below floor -> default
        options.StatementMaxInputChars = 200_000;
        options.ImageInput = false;
        options.ImageRenderMaxEdge = 10_000;                           // above ceiling -> clamped

        var provider = new AiProvider("local", options);

        Assert.Equal(AiProviderOptions.DefaultMaxInputChars, provider.MaxInputChars);
        Assert.Equal(200_000, provider.StatementMaxInputChars);
        Assert.False(provider.ImageInput);
        Assert.Equal(AiProviderOptions.MaxImageRenderMaxEdge, provider.ImageRenderMaxEdge);
    }
}
