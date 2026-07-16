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
            .Handle(new TestAiConnectionCommand(), ct);

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
            .Handle(new TestAiConnectionCommand(), ct);

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
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        await Sut().Handle(new TestAiConnectionCommand("foundry"), ct);

        _resolver.Received(1).Resolve("foundry");
    }

    [Fact]
    public async Task HappyPath_ReturnsSuccess_WithLatency()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        var result = await Sut().Handle(new TestAiConnectionCommand(), ct);

        Assert.True(result.Success);
        Assert.Equal("Connection successful.", result.Message);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task ClientThrows_ReturnsFailure_WithExceptionMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bad endpoint"));

        var result = await Sut().Handle(new TestAiConnectionCommand(), ct);

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
            .Handle(new TestAiConnectionCommand(), ct);

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

        var result = await Sut().Handle(new TestAiConnectionCommand(), ct);

        Assert.False(result.Success);
        Assert.Equal("Connection timed out.", result.Message);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task DoesNotDisposeClient_ProviderOwnsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        await Sut().Handle(new TestAiConnectionCommand(), ct);

        // The provider returns a singleton it owns for the app lifetime — the handler must never dispose it.
        _client.DidNotReceive().Dispose();
    }
}
