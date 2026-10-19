using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Services;

public class StatementExtractionServiceTests
{
    private static StatementExtractionService Sut(IAiProvider provider, AiInputSettings? settings = null) =>
        new(provider, settings ?? TestAiProviders.Settings(), NullLogger<StatementExtractionService>.Instance);

    // The nullable line-item direction is an anyOf rather than ["string","null"] + an enum holding null: the
    // Claude API rejects the latter outright ("Enum value 'out' does not match declared type"), which would
    // knock every statement parse off strict structured output. Pin the shape so dropping the
    // nullable-enum hook from the schema generator can't silently reintroduce it.
    [Fact]
    public async Task LineItemDirection_IsANullableEnumWrittenAsAnyOf()
    {
        var provider = TestAiProviders.Provider("""{"kind":"Bank","lineItems":[]}""").Capture(out var sent);

        await Sut(provider).ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var direction = sent()!.Schema
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

    // Line-item output grows with the transaction count, so the request asks for a long reply (the provider streams
    // and raises its output cap); the effort comes from the input settings, never a service default.
    [Fact]
    public async Task SendsLongOutputStatementRequest_WithSettingsEffort()
    {
        var provider = TestAiProviders.Provider("""{"kind":"Bank","lineItems":[]}""").Capture(out var sent);

        await Sut(provider, TestAiProviders.Settings(effort: AiEffort.Low))
            .ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var request = sent()!;
        Assert.Equal(AiFeature.StatementExtraction, request.Feature);
        Assert.Equal(AiEffort.Low, request.Effort);
        Assert.True(request.LongOutput);
        Assert.Contains("Opening balance 1.00", request.UserText());
    }

    // Golden copy of the whole response schema: the contract the provider was validated against.
    // Whatever produces the schema (hand-built or generated from the DTO), a change here is a change to what
    // the model is sent — update this copy deliberately, never to make a refactor pass. Compared
    // structurally, so key order doesn't matter but array order (e.g. "required") does.
    [Fact]
    public async Task ResponseSchema_MatchesGoldenCopy()
    {
        var provider = TestAiProviders.Provider("""{"kind":"Bank","lineItems":[]}""").Capture(out var sent);

        await Sut(provider).ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        var actual = JsonNode.Parse(sent()!.Schema.GetRawText());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GoldenSchema), actual),
            $"Response schema drifted from the golden copy. Actual:\n{actual!.ToJsonString(Indented)}");
    }

    // kind and direction are constrained in the schema but parsed tolerantly: a value outside the enum (the schema
    // is enforced, so this is defence in depth against a reply that escapes it) maps to Unknown / null rather than
    // failing the parse.
    [Fact]
    public async Task UnrecognisedKind_MapsToUnknown()
    {
        var provider = TestAiProviders.Provider("""{"kind":"Savings","institution":"Example Bank","lineItems":[]}""");

        var ir = await Sut(provider).ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

        Assert.Equal(StatementKind.Unknown, ir.Kind);
        Assert.Equal("Example Bank", ir.Header.Institution);
    }

    [Fact]
    public async Task UnrecognisedDirection_MapsToNull()
    {
        var provider = TestAiProviders.Provider("""
            {"kind":"Bank","lineItems":[
              {"date":"2026-06-03","description":"GROCERY MART","amount":42.10,"direction":"sideways","runningBalance":null}
            ]}
            """);

        var ir = await Sut(provider).ExtractAsync("Opening balance 1.00", null, TestContext.Current.CancellationToken);

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
}
