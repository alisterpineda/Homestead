using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Services;

public class PayeeResolutionServiceTests
{
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private ChatOptions? _sent;

    // Golden copy of the whole response schema: the contract both provider families were validated against.
    // The descriptions are part of the prompt. Whatever produces the schema (hand-built or generated from the
    // DTO), a change here is a change to what every provider is sent — update this copy deliberately, never to
    // make a refactor pass. Compared structurally, so key order doesn't matter but array order does.
    [Fact]
    public async Task ResponseSchema_MatchesGoldenCopy()
    {
        _client.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"results":[]}"""))
            {
                FinishReason = ChatFinishReason.Stop,
            });
        var sut = new PayeeResolutionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<PayeeResolutionService>.Instance);

        await sut.ResolveAsync(
            [new PayeeResolutionInput(0, "COFFEE SHOP #12")], [new PayeeCatalogEntry(Guid.NewGuid(), "Coffee Shop")],
            TestContext.Current.CancellationToken);

        var schema = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat).Schema!.Value;
        var actual = JsonNode.Parse(schema.GetRawText());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GoldenSchema), actual),
            $"Response schema drifted from the golden copy. Actual:\n{actual!.ToJsonString(Indented)}");
    }

    // payeeIndex is a non-null integer in the schema, so a null can only arrive after the ladder fell back to
    // unenforced JSON. That reply breaks the contract and fails loudly rather than being half-used.
    [Fact]
    public async Task NullPayeeIndex_Throws()
    {
        _client.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"results":[{"lineIndex":0,"payeeIndex":null,"newPayeeName":"Coffee Shop"}]}"""))
            {
                FinishReason = ChatFinishReason.Stop,
            });
        var sut = new PayeeResolutionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<PayeeResolutionService>.Instance);

        await Assert.ThrowsAsync<JsonException>(() => sut.ResolveAsync(
            [new PayeeResolutionInput(0, "COFFEE SHOP #12")], [],
            TestContext.Current.CancellationToken));
    }

    // The same contract break with the key left out: an int would silently default to 0 and match the first
    // catalog payee, so a missing payeeIndex has to fail as loudly as a null one.
    [Fact]
    public async Task MissingPayeeIndex_Throws()
    {
        _client.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"results":[{"lineIndex":0,"newPayeeName":"Blue Bottle Coffee"}]}"""))
            {
                FinishReason = ChatFinishReason.Stop,
            });
        var sut = new PayeeResolutionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<PayeeResolutionService>.Instance);

        await Assert.ThrowsAsync<JsonException>(() => sut.ResolveAsync(
            [new PayeeResolutionInput(0, "BLUE BOTTLE #4")], [new PayeeCatalogEntry(Guid.NewGuid(), "Coffee Shop")],
            TestContext.Current.CancellationToken));
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private const string GoldenSchema = """
        {
          "type": "object",
          "properties": {
            "results": {
              "type": "array",
              "description": "One entry per input line.",
              "items": {
                "type": "object",
                "properties": {
                  "lineIndex": {
                    "type": "integer",
                    "description": "The line's index, echoed from the input."
                  },
                  "payeeIndex": {
                    "type": "integer",
                    "description": "Zero-based index into the known-payees list, or -1 for no existing match."
                  },
                  "newPayeeName": {
                    "type": "string",
                    "description": "A concise new payee name when proposing one (payeeIndex = -1); otherwise empty."
                  }
                },
                "required": ["lineIndex", "payeeIndex", "newPayeeName"],
                "additionalProperties": false
              }
            }
          },
          "required": ["results"],
          "additionalProperties": false
        }
        """;
}
