using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
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
    // knock every Anthropic statement parse off strict structured output. Pin the shape so dropping the
    // nullable-enum hook from the schema generator can't silently reintroduce it.
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

    // Golden copy of the whole response schema: the contract both provider families were validated against.
    // Whatever produces the schema (hand-built or generated from the DTO), a change here is a change to what
    // every provider is sent — update this copy deliberately, never to make a refactor pass. Compared
    // structurally, so key order doesn't matter but array order (e.g. "required") does.
    [Fact]
    public async Task ResponseSchema_MatchesGoldenCopy()
    {
        _client.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Do<ChatOptions>(o => _sent = o), Arg.Any<CancellationToken>())
            .Returns(Reply("""{"kind":"Bank","lineItems":[]}"""));
        var sut = new StatementExtractionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<StatementExtractionService>.Instance);

        await sut.ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var schema = Assert.IsType<ChatResponseFormatJson>(_sent!.ResponseFormat).Schema!.Value;
        var actual = JsonNode.Parse(schema.GetRawText());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GoldenSchema), actual),
            $"Response schema drifted from the golden copy. Actual:\n{actual!.ToJsonString(Indented)}");
    }

    // kind and direction are constrained in the schema but parsed tolerantly: a value outside the enum (only
    // possible once the ladder fell back to unenforced JSON) maps to Unknown / null rather than failing the parse.
    [Fact]
    public async Task UnrecognisedKind_MapsToUnknown()
    {
        _client.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Reply("""{"kind":"Savings","institution":"Example Bank","lineItems":[]}"""));
        var sut = new StatementExtractionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<StatementExtractionService>.Instance);

        var ir = await sut.ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        Assert.Equal(StatementKind.Unknown, ir.Kind);
        Assert.Equal("Example Bank", ir.Header.Institution);
    }

    [Fact]
    public async Task UnrecognisedDirection_MapsToNull()
    {
        _client.GetStreamingResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Reply("""
                {"kind":"Bank","lineItems":[
                  {"date":"2026-06-03","description":"GROCERY MART","amount":42.10,"direction":"sideways","runningBalance":null}
                ]}
                """));
        var sut = new StatementExtractionService(
            TestAiProviders.Resolver(TestAiProviders.Provider(_client)),
            NullLogger<StatementExtractionService>.Instance);

        var ir = await sut.ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var line = Assert.Single(ir.LineItems!);
        Assert.Equal(42.10m, line.Amount);
        Assert.Null(line.Direction);
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private const string GoldenSchema = """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "enum": ["Bank", "CreditCard", "Brokerage", "Unknown"] },
            "institution": { "type": ["string", "null"] },
            "accountNumberMasked": { "type": ["string", "null"] },
            "currency": { "type": ["string", "null"] },
            "periodStart": { "type": ["string", "null"] },
            "periodEnd": { "type": ["string", "null"] },
            "openingBalance": {
              "type": ["object", "null"],
              "properties": {
                "amount": { "type": "number" },
                "sourceAnchor": { "type": ["string", "null"] }
              },
              "required": ["amount", "sourceAnchor"],
              "additionalProperties": false
            },
            "closingBalance": {
              "type": ["object", "null"],
              "properties": {
                "amount": { "type": "number" },
                "sourceAnchor": { "type": ["string", "null"] }
              },
              "required": ["amount", "sourceAnchor"],
              "additionalProperties": false
            },
            "lineItems": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "date": { "type": ["string", "null"] },
                  "description": { "type": ["string", "null"] },
                  "amount": { "type": ["number", "null"] },
                  "direction": { "anyOf": [{ "type": "string", "enum": ["out", "in"] }, { "type": "null" }] },
                  "runningBalance": { "type": ["number", "null"] }
                },
                "required": ["date", "description", "amount", "direction", "runningBalance"],
                "additionalProperties": false
              }
            }
          },
          "required": [
            "kind", "institution", "accountNumberMasked", "currency",
            "periodStart", "periodEnd", "openingBalance", "closingBalance", "lineItems"
          ],
          "additionalProperties": false
        }
        """;

    private static async IAsyncEnumerable<ChatResponseUpdate> Reply(string json)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, json) { FinishReason = ChatFinishReason.Stop };
    }
}
