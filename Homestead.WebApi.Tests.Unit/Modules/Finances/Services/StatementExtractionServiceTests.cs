using System.Text.Json;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Services;

public class StatementExtractionServiceTests
{
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private ChatOptions? _sent;

    // The nullable line-item direction is an anyOf rather than ["string","null"] + an enum holding null: the
    // Claude API rejects the latter outright ("Enum value 'out' does not match declared type"), which would
    // knock every Anthropic statement parse off strict structured output. Pin the shape so a tidy-up back to
    // the sibling Nullable(...) style can't silently reintroduce it.
    [Fact]
    public async Task LineItemDirection_IsANullableEnumWrittenAsAnyOf()
    {
        _client.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(Reply("""{"kind":"Bank","lineItems":[]}"""));
        var sut = new StatementExtractionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<StatementExtractionService>.Instance);

        await sut.ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var schema = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat).Schema!.Value;
        var direction = schema
            .GetProperty("properties").GetProperty("lineItems")
            .GetProperty("items").GetProperty("properties").GetProperty("direction");

        Assert.False(direction.TryGetProperty("type", out _));
        Assert.False(direction.TryGetProperty("enum", out _));
        var anyOf = direction.GetProperty("anyOf").EnumerateArray().ToList();
        Assert.Equal(2, anyOf.Count);
        Assert.Equal("string", anyOf[0].GetProperty("type").GetString());
        Assert.Equal(["out", "in"], anyOf[0].GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("null", anyOf[1].GetProperty("type").GetString());
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Reply(string json)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, json) { FinishReason = ChatFinishReason.Stop };
    }
}
