using System.Diagnostics;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

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

    private static AiProviderOptions ValidAnthropic(string? endpoint = null) => new()
    {
        Type = AiProviderType.Anthropic,
        Endpoint = endpoint,
        ApiKey = "k",
        Model = "claude-opus-5-5",
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

    // The Claude API takes temperature and max_tokens differently, so the SDK's client is always wrapped — with
    // the adapter outermost and OpenTelemetry inside it (GetService only walks inward), so spans record the
    // options actually sent.
    [Theory]
    [InlineData(null)]
    [InlineData("https://gateway.example.com")]
    public void GetChatClient_Anthropic_BuildsWrappedOutsideOpenTelemetry_AndMemoizes(string? endpoint)
    {
        var provider = new AiProvider("claude", ValidAnthropic(endpoint));

        var first = provider.GetChatClient();

        Assert.IsType<AnthropicChatOptionsClient>(first);
        Assert.NotNull(first.GetService<OpenTelemetryChatClient>());
        Assert.Same(first, provider.GetChatClient());
    }

    // Every provider type returns the M.E.AI pipeline with OpenTelemetry in it, so GenAI spans and metrics
    // can't be silently dropped by a future switch arm.
    public static TheoryData<AiProviderOptions> AllProviderTypes() => [ValidOpenAi(), ValidFoundry(), ValidAnthropic()];

    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_EveryType_IncludesOpenTelemetry_AndMemoizesThePipeline(AiProviderOptions options)
    {
        var provider = new AiProvider("any", options);

        var client = provider.GetChatClient();

        Assert.NotNull(client.GetService<OpenTelemetryChatClient>());
        Assert.Same(client, provider.GetChatClient());
    }

    // Document contents are personal: prompts and responses never reach telemetry, whatever
    // OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT says (Aspire sets it to true in Development).
    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_EveryType_KeepsSensitiveDataOutOfTelemetry(AiProviderOptions options)
    {
        var otel = new AiProvider("any", options).GetChatClient().GetService<OpenTelemetryChatClient>();

        Assert.NotNull(otel);
        Assert.False(otel!.EnableSensitiveData);
    }

    // ServiceDefaults only exports what it subscribes to; a name the pipeline doesn't emit under drops every
    // GenAI span silently.
    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_EveryType_EmitsUnderTheNameServiceDefaultsSubscribesTo(AiProviderOptions options)
    {
        var source = new AiProvider("any", options).GetChatClient().GetService<ActivitySource>();

        Assert.Equal("Experimental.Microsoft.Extensions.AI", source?.Name);
    }

    // The Anthropic adapter drops Temperature and forces MaxOutputTokens, so it must never reach another type.
    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_OnlyAnthropic_GetsTheOptionsAdapter(AiProviderOptions options)
    {
        var client = new AiProvider("any", options).GetChatClient();

        Assert.Equal(options.Type == AiProviderType.Anthropic,
            client.GetService<AnthropicChatOptionsClient>() is not null);
    }

    // The configured Model and Endpoint reach the SDK client (the adapter reports both as metadata, which the
    // pipeline forwards); a blank Endpoint means the public Claude API.
    [Theory]
    [InlineData(null, "https://api.anthropic.com/")]
    [InlineData("https://gateway.example.com", "https://gateway.example.com/")]
    public void GetChatClient_Anthropic_UsesTheConfiguredModelAndEndpoint(string? endpoint, string expectedUri)
    {
        var metadata = new AiProvider("claude", ValidAnthropic(endpoint)).GetChatClient()
            .GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal(new Uri(expectedUri), metadata!.ProviderUri);
        Assert.Equal("claude-opus-5-5", metadata.DefaultModelId);
    }

    // Config is the only source of where requests go: the SDK would otherwise adopt ANTHROPIC_BASE_URL from
    // the process environment. Env vars are process-global, so this is the only test (and the only class)
    // that sets one; it restores the previous value either way.
    [Fact]
    public void GetChatClient_Anthropic_IgnoresAnthropicBaseUrlFromTheEnvironment()
    {
        const string name = "ANTHROPIC_BASE_URL";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "https://elsewhere.example.com");

            var metadata = new AiProvider("claude", ValidAnthropic()).GetChatClient()
                .GetService<ChatClientMetadata>();

            Assert.Equal(new Uri("https://api.anthropic.com/"), metadata!.ProviderUri);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
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
