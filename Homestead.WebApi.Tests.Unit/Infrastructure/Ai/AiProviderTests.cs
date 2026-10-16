using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using NSubstitute;

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

    // The Claude API needs max_tokens on every request, so the SDK's client is always wrapped — with the adapter
    // outside OpenTelemetry (GetService only walks inward, so finding OTel *from* the adapter proves the order),
    // so spans record the options actually sent.
    [Theory]
    [InlineData(null)]
    [InlineData("https://gateway.example.com")]
    public void GetChatClient_Anthropic_BuildsWrappedOutsideOpenTelemetry_AndMemoizes(string? endpoint)
    {
        var provider = new AiProvider("claude", ValidAnthropic(endpoint));

        var first = provider.GetChatClient();

        var adapter = first.GetService<AnthropicChatOptionsClient>();
        Assert.NotNull(adapter);
        Assert.NotNull(adapter!.GetService<OpenTelemetryChatClient>());
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

    // The Anthropic adapter forces a MaxOutputTokens default sized for Claude, so it must never reach another type.
    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_OnlyAnthropic_GetsTheOptionsAdapter(AiProviderOptions options)
    {
        var client = new AiProvider("any", options).GetChatClient();

        Assert.Equal(options.Type == AiProviderType.Anthropic,
            client.GetService<AnthropicChatOptionsClient>() is not null);
    }

    // The provider-options layer (Temperature, StructuredOutput) is outermost for every type: outside the
    // Anthropic adapter and outside OpenTelemetry, which the span tests below prove behaviourally.
    [Theory]
    [MemberData(nameof(AllProviderTypes))]
    public void GetChatClient_EveryType_PutsTheProviderOptionsLayerOutermost(AiProviderOptions options)
    {
        var client = new AiProvider("any", options).GetChatClient();

        Assert.IsType<ConfigureOptionsChatClient>(client);
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

    // --- The provider-options layer, driven through the real pipeline over a stand-in SDK client ---
    //
    // BuildPipeline is the pipeline every provider builds around its SDK client; the substitute stands in for
    // the SDK, so what it receives is what would be sent on the wire.

    private readonly IChatClient _sdk = Substitute.For<IChatClient>();
    private ChatOptions? _sent;

    public AiProviderTests()
    {
        _sdk.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        _sdk.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(_ => Empty());
    }

    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "hi")];

    private static AiProviderOptions OfType(AiProviderType type) => type switch
    {
        AiProviderType.OpenAiCompatible => ValidOpenAi(),
        AiProviderType.AzureFoundry => ValidFoundry(),
        _ => ValidAnthropic(),
    };

    private async Task Send(AiProviderOptions provider, ChatOptions? options, bool stream)
    {
        var client = AiProvider.BuildPipeline(_sdk, provider);
        var ct = TestContext.Current.CancellationToken;
        if (stream)
            await foreach (var _ in client.GetStreamingResponseAsync(Messages, options, ct)) { }
        else
            await client.GetResponseAsync(Messages, options, ct);
    }

    public static TheoryData<AiProviderType, bool> EveryTypeAndTransport()
    {
        var data = new TheoryData<AiProviderType, bool>();
        foreach (var type in Enum.GetValues<AiProviderType>())
        {
            data.Add(type, false);
            data.Add(type, true);
        }
        return data;
    }

    // What callers ask for: a strict schema, no temperature (StructuredChatRequest's shape).
    private static ChatOptions StrictSchemaRequest() => new()
    {
        ResponseFormat = ChatResponseFormat.ForJsonSchema(
            JsonSerializer.SerializeToElement(new { type = "object" }), "reply", "d"),
        AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true, ["other"] = "kept" },
    };

    // Anthropic included: a configured temperature is honoured, not silently dropped — config is the single
    // source of truth.
    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task ConfiguredTemperature_IsSent(AiProviderType type, bool stream)
    {
        var provider = OfType(type);
        provider.Temperature = 0.5f;

        await Send(provider, StrictSchemaRequest(), stream);

        Assert.Equal(0.5f, _sent!.Temperature);
    }

    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task UnsetTemperature_IsNotSent(AiProviderType type, bool stream)
    {
        await Send(OfType(type), StrictSchemaRequest(), stream);

        Assert.Null(_sent!.Temperature);
    }

    // Config fills in only what the caller left unset.
    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task CallerSetTemperature_WinsOverConfig(AiProviderType type, bool stream)
    {
        var provider = OfType(type);
        provider.Temperature = 0.5f;
        var request = StrictSchemaRequest();
        request.Temperature = 1f;

        await Send(provider, request, stream);

        Assert.Equal(1f, _sent!.Temperature);
    }

    // An endpoint without json_schema support gets plain JSON mode; "strict" means nothing without a schema,
    // so it goes too — any other provider-specific property is left alone.
    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task StructuredOutputOff_SendsPlainJson_WithoutStrict(AiProviderType type, bool stream)
    {
        var provider = OfType(type);
        provider.StructuredOutput = false;

        await Send(provider, StrictSchemaRequest(), stream);

        var format = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat);
        Assert.Null(format.Schema);
        Assert.False(_sent.AdditionalProperties!.ContainsKey("strict"));
        Assert.Equal("kept", _sent.AdditionalProperties["other"]);
    }

    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task StructuredOutputOn_SendsTheSchemaAndStrictUnchanged(AiProviderType type, bool stream)
    {
        await Send(OfType(type), StrictSchemaRequest(), stream);

        var format = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat);
        Assert.Equal("reply", format.SchemaName);
        Assert.Equal("""{"type":"object"}""", format.Schema!.Value.GetRawText());
        Assert.Equal(true, _sent.AdditionalProperties!["strict"]);
    }

    // Only a schema format is downgraded; a caller that asked for text keeps text.
    [Fact]
    public async Task StructuredOutputOff_LeavesANonSchemaFormatAlone()
    {
        var provider = ValidOpenAi();
        provider.StructuredOutput = false;

        await Send(provider, new ChatOptions { ResponseFormat = ChatResponseFormat.Text }, stream: false);

        Assert.IsType<ChatResponseFormatText>(_sent!.ResponseFormat);
    }

    // The caller's options are its own (a caller may reuse them): the adjusted copy goes to the SDK.
    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task NeverMutatesTheCallersOptions(AiProviderType type, bool stream)
    {
        var provider = OfType(type);
        provider.Temperature = 0.5f;
        provider.StructuredOutput = false;
        var request = StrictSchemaRequest();
        var schemaFormat = request.ResponseFormat;

        await Send(provider, request, stream);

        Assert.NotSame(request, _sent);
        Assert.Null(request.Temperature);
        Assert.Same(schemaFormat, request.ResponseFormat);
        Assert.Equal(true, request.AdditionalProperties!["strict"]);
    }

    // The layer sits outside OpenTelemetry, so the span records the temperature actually sent, not the
    // caller's (unset) one.
    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task Span_RecordsTheConfiguredTemperature(AiProviderType type, bool stream)
    {
        var provider = OfType(type);
        provider.Temperature = 0.5f;

        var span = await SendCapturingSpan(provider, stream);

        Assert.Equal(0.5, Convert.ToDouble(span.GetTagItem("gen_ai.request.temperature")));
    }

    [Theory]
    [MemberData(nameof(EveryTypeAndTransport))]
    public async Task Span_RecordsNoTemperature_WhenUnset(AiProviderType type, bool stream)
    {
        var span = await SendCapturingSpan(OfType(type), stream);

        Assert.Null(span.GetTagItem("gen_ai.request.temperature"));
    }

    // Listeners are process-global and other tests emit on the same source in parallel, so the span is picked
    // out by a model ID unique to this call.
    private async Task<Activity> SendCapturingSpan(AiProviderOptions provider, bool stream)
    {
        var modelId = Guid.NewGuid().ToString();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Microsoft.Extensions.Hosting.Extensions.MicrosoftExtensionsAiTelemetryName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.GetTagItem("gen_ai.request.model") as string == modelId)
                    spans.Add(a);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var request = StrictSchemaRequest();
        request.ModelId = modelId;
        await Send(provider, request, stream);

        return Assert.Single(spans);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Empty()
    {
        await Task.Yield();
        yield break;
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

    // The request-shaping knobs are surfaced as configured (for the admin page): no resolution, so an unset
    // Temperature stays null ("not sent") and StructuredOutput defaults on.
    [Fact]
    public void RequestKnobs_ExposeTheConfiguredValues()
    {
        var options = ValidOpenAi();
        options.Temperature = 0.7f;
        options.StructuredOutput = false;

        var provider = new AiProvider("local", options);

        Assert.Equal(0.7f, provider.Temperature);
        Assert.False(provider.StructuredOutput);
    }

    [Fact]
    public void RequestKnobs_Unset_AreNullAndTrue()
    {
        var provider = new AiProvider("local", ValidOpenAi());

        Assert.Null(provider.Temperature);
        Assert.True(provider.StructuredOutput);
    }
}
