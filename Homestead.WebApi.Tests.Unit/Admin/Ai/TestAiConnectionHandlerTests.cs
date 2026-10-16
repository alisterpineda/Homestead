using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

public class TestAiConnectionHandlerTests
{
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private readonly IAiProvider _provider;
    private readonly IAiProviderResolver _resolver;

    public TestAiConnectionHandlerTests()
    {
        _provider = TestAiProviders.Provider(_client);
        _resolver = TestAiProviders.Resolver(_provider);
    }

    private TestAiConnectionHandler Sut() => new(_resolver);

    [Fact]
    public async Task Unresolved_ReturnsFailure_WithoutBuildingClient()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await new TestAiConnectionHandler(TestAiProviders.Unresolved())
            .HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("AI is not configured.", result.Message);
        Assert.Null(result.LatencyMs);
    }

    // Covers both a misconfigured provider and (via the resolver's synthetic instance) an undefined key —
    // the probe reports the reason instead of building a client that would throw.
    [Fact]
    public async Task UnavailableProvider_ReturnsFailureWithReason_WithoutBuildingClient()
    {
        var ct = TestContext.Current.CancellationToken;
        var broken = TestAiProviders.UnavailableProvider("foundry", "ApiKey is required.");

        var result = await new TestAiConnectionHandler(TestAiProviders.Resolver(broken))
            .HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("AI provider 'foundry' is unavailable: ApiKey is required.", result.Message);
        Assert.Null(result.LatencyMs);
        broken.DidNotReceive().GetChatClient();
    }

    [Fact]
    public async Task ExplicitProviderKey_IsForwardedToTheResolver()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"ok":true}"""))));

        await Sut().HandleAsync("foundry", ct);

        _resolver.Received(1).Resolve("foundry");
    }

    [Fact]
    public async Task HappyPath_ReturnsSuccess_WithLatency()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"ok":true}"""))));

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.True(result.Success);
        Assert.Equal("Connection successful.", result.Message);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);
    }

    // The probe is a real structured-output request, not a bare "ping": the same strict json_schema + strict flag
    // every AI service sends, so a provider whose Temperature/StructuredOutput config doesn't match the endpoint
    // fails here, at setup time. No temperature and no output cap from the caller — the pipeline adds a configured
    // temperature, and a fixed cap can misreport an Azure reasoning deployment as broken. The message must say
    // "JSON": the pipeline turns this into json_object mode for StructuredOutput=false, and OpenAI/Azure reject
    // that mode with a 400 unless a message contains the word.
    [Fact]
    public async Task Probe_SendsAStrictSchema_BufferedWithNoTemperatureOrOutputCap()
    {
        var ct = TestContext.Current.CancellationToken;
        ChatOptions? sent = null;
        IEnumerable<ChatMessage>? messages = null;
        _client.GetResponseAsync(Arg.Do<IEnumerable<ChatMessage>>(m => messages = m),
                Arg.Do<ChatOptions>(o => sent = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"ok":true}"""))));

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.True(result.Success);
        Assert.NotNull(messages);
        var message = Assert.Single(messages);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Contains("JSON", message.Text);
        Assert.NotNull(sent);
        var format = Assert.IsType<ChatResponseFormatJson>(sent.ResponseFormat);
        var schema = JsonNode.Parse(format.Schema!.Value.GetRawText())!;
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal("boolean", schema["properties"]!["ok"]!["type"]!.GetValue<string>());
        Assert.Equal(["ok"], schema["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(true, sent.AdditionalProperties?["strict"]);
        Assert.Null(sent.Temperature);
        Assert.Null(sent.MaxOutputTokens);
        _client.DidNotReceive().GetStreamingResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    // A provider 400 (a model that rejects the configured temperature, or an endpoint without json_schema while
    // StructuredOutput is on) is the point of the probe: it comes back as a failed result carrying the provider's
    // own account of what it rejected, for the operator to read on /admin/ai.
    [Fact]
    public async Task Anthropic400_ReturnsFailure_WithTheProvidersMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var error = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest,
            ResponseBody = """{"error":{"message":"temperature: unsupported for this model"}}""",
        };
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Contains("temperature: unsupported for this model", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task OpenAi400_ReturnsFailure_WithTheProvidersMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var pipelineResponse = Substitute.For<PipelineResponse>();
        pipelineResponse.Status.Returns(400);
        // Built before the stub: its constructor reads Status, which NSubstitute would mistake for the call
        // being configured.
        var error = new ClientResultException("json_schema response format not supported", pipelineResponse);
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("json_schema response format not supported", result.Message);
        Assert.Null(result.LatencyMs);
    }

    // A 200 that isn't an answer — declined, or cut off at the output cap — is not a working connection either.
    [Theory]
    [InlineData("content_filter", "AI connection test: the model declined the request.")]
    [InlineData("length", "AI connection test: the response hit the output-token limit and was cut off.")]
    public async Task IncompleteReply_ReturnsFailure(string finishReason, string expectedMessage)
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{"))
            {
                FinishReason = new ChatFinishReason(finishReason),
            }));

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal(expectedMessage, result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task ClientThrows_ReturnsFailure_WithExceptionMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bad endpoint"));

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("bad endpoint", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task GetChatClientThrows_ReturnsFailure_NoExceptionEscapes()
    {
        // Validation pre-checks the endpoint URI now, but a client-build failure must still surface as
        // Success=false rather than escaping as an unhandled 500 — the build runs inside the try.
        var ct = TestContext.Current.CancellationToken;
        var provider = TestAiProviders.Provider(_client);
        provider.GetChatClient().Throws(new UriFormatException("Invalid URI."));

        var result = await new TestAiConnectionHandler(TestAiProviders.Resolver(provider))
            .HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("Invalid URI.", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task ProbeTimeout_ReturnsFriendlyFailure()
    {
        // A cancellation NOT originating from the caller (i.e. our own probe timeout) is reported as a
        // friendly connection result, not propagated. The outer token is never cancelled here.
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await Sut().HandleAsync(providerKey: null, ct);

        Assert.False(result.Success);
        Assert.Equal("Connection timed out.", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task DoesNotDisposeClient_ProviderOwnsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"ok":true}"""))));

        await Sut().HandleAsync(providerKey: null, ct);

        // The provider returns a singleton it owns for the app lifetime — the handler must never dispose it.
        _client.DidNotReceive().Dispose();
    }
}
