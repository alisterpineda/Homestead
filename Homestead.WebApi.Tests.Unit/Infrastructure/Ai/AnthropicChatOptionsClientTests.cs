using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

public class AnthropicChatOptionsClientTests
{
    private readonly IChatClient _inner = Substitute.For<IChatClient>();
    private readonly AnthropicChatOptionsClient _sut;
    private ChatOptions? _sent;

    public AnthropicChatOptionsClientTests()
    {
        _sut = new AnthropicChatOptionsClient(_inner);
        _inner.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        _inner.GetStreamingResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(Empty());
    }

    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "hi")];

    private async Task Call(bool stream, ChatOptions? options)
    {
        var ct = TestContext.Current.CancellationToken;
        if (stream)
            await foreach (var _ in _sut.GetStreamingResponseAsync(Messages, options, ct)) { }
        else
            await _sut.GetResponseAsync(Messages, options, ct);
    }

    [Theory]
    [InlineData(false, AnthropicChatOptionsClient.NonStreamingMaxOutputTokens)]
    [InlineData(true, AnthropicChatOptionsClient.StreamingMaxOutputTokens)]
    public async Task DropsTemperature_AndCapsByTransport(bool stream, int expectedCap)
    {
        await Call(stream, new ChatOptions { Temperature = 0f });

        Assert.NotNull(_sent);
        Assert.Null(_sent!.Temperature);
        Assert.Equal(expectedCap, _sent.MaxOutputTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepsACallerSetCap(bool stream)
    {
        await Call(stream, new ChatOptions { MaxOutputTokens = 500 });

        Assert.Equal(500, _sent!.MaxOutputTokens);
    }

    // The caller's options are its own: the adapted copy goes to the SDK, the original is left untouched.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeverMutatesTheCallersOptions(bool stream)
    {
        var options = new ChatOptions { Temperature = 0f };

        await Call(stream, options);

        Assert.Equal(0f, options.Temperature);
        Assert.Null(options.MaxOutputTokens);
        Assert.NotSame(options, _sent);
    }

    // Only Temperature and MaxOutputTokens are adapted; everything else — the structured-output schema above
    // all — must reach the SDK unchanged.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassesTheResponseFormatAndAdditionalPropertiesThrough(bool stream)
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "s", "d"),
            AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true },
        };

        await Call(stream, options);

        var format = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat);
        Assert.Equal("s", format.SchemaName);
        Assert.Equal(schema.GetRawText(), format.Schema!.Value.GetRawText());
        Assert.Equal(true, _sent.AdditionalProperties?["strict"]);
    }

    // The connection probe passes no options at all; the cap must still be sent.
    [Theory]
    [InlineData(false, AnthropicChatOptionsClient.NonStreamingMaxOutputTokens)]
    [InlineData(true, AnthropicChatOptionsClient.StreamingMaxOutputTokens)]
    public async Task NullOptions_GetTheCap(bool stream, int expectedCap)
    {
        await Call(stream, null);

        Assert.Equal(expectedCap, _sent!.MaxOutputTokens);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Empty()
    {
        await Task.Yield();
        yield break;
    }
}
