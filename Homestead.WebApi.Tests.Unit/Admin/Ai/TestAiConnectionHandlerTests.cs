using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Admin.Ai;

public class TestAiConnectionHandlerTests
{
    private readonly IAiClientFactory _factory = Substitute.For<IAiClientFactory>();
    private readonly IChatClient _client = Substitute.For<IChatClient>();

    public TestAiConnectionHandlerTests()
    {
        _factory.IsConfigured.Returns(true);
        _factory.CreateClient().Returns(_client);
    }

    private TestAiConnectionHandler Sut() => new(_factory);

    [Fact]
    public async Task NotConfigured_ReturnsFailure_WithoutCreatingClient()
    {
        var ct = TestContext.Current.CancellationToken;
        _factory.IsConfigured.Returns(false);

        var result = await Sut().Handle(new TestAiConnectionCommand(), ct);

        Assert.False(result.Success);
        Assert.Equal("AI is not configured.", result.Message);
        Assert.Null(result.LatencyMs);
        _factory.DidNotReceive().CreateClient();
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
    public async Task CreateClientThrows_ReturnsFailure_NoExceptionEscapes()
    {
        // A malformed Ai:Endpoint still passes IsConfigured but throws when the factory lazily builds the
        // client (new Uri). That build runs inside the try, so it must surface as Success=false, not a 500.
        var ct = TestContext.Current.CancellationToken;
        _factory.CreateClient().Throws(new UriFormatException("Invalid URI."));

        var result = await Sut().Handle(new TestAiConnectionCommand(), ct);

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
    public async Task DoesNotDisposeClient_FactoryOwnsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        await Sut().Handle(new TestAiConnectionCommand(), ct);

        // The factory returns a singleton it owns for the app lifetime — the handler must never dispose it.
        _client.DidNotReceive().Dispose();
    }
}
