using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Services;

public class PayeeResolutionServiceTests
{
    private static PayeeResolutionService Sut(IAiProvider provider, AiInputSettings? settings = null) =>
        new(provider, settings ?? TestAiProviders.Settings(), NullLogger<PayeeResolutionService>.Instance);

    // A short reply (one small entry per line, so no streaming); the effort comes from the input settings, never a
    // service default.
    [Fact]
    public async Task SendsShortPayeeRequest_WithSettingsEffort()
    {
        var provider = TestAiProviders.Provider("""{"results":[]}""").Capture(out var sent);

        await Sut(provider, TestAiProviders.Settings(effort: AiEffort.High)).ResolveAsync(
            [new PayeeResolutionInput(0, "COFFEE SHOP #12")], [new PayeeCatalogEntry(Guid.NewGuid(), "Coffee Shop")],
            TestContext.Current.CancellationToken);

        var request = sent()!;
        Assert.Equal(AiFeature.PayeeResolution, request.Feature);
        Assert.Equal(AiEffort.High, request.Effort);
        Assert.False(request.LongOutput);
        Assert.Contains("COFFEE SHOP #12", request.UserText());
    }

    // Golden copy of the whole response schema: the contract the provider was validated against.
    // The descriptions are part of the prompt. Whatever produces the schema (hand-built or generated from the
    // DTO), a change here is a change to what the model is sent — update this copy deliberately, never to
    // make a refactor pass. Compared structurally, so key order doesn't matter but array order does.
    [Fact]
    public async Task ResponseSchema_MatchesGoldenCopy()
    {
        var provider = TestAiProviders.Provider("""{"results":[]}""").Capture(out var sent);

        await Sut(provider).ResolveAsync(
            [new PayeeResolutionInput(0, "COFFEE SHOP #12")], [new PayeeCatalogEntry(Guid.NewGuid(), "Coffee Shop")],
            TestContext.Current.CancellationToken);

        var actual = JsonNode.Parse(sent()!.Schema.GetRawText());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(GoldenSchema), actual),
            $"Response schema drifted from the golden copy. Actual:\n{actual!.ToJsonString(Indented)}");
    }

    // payeeIndex is a non-null integer in the enforced schema, so a null can only arrive in a reply that escapes it.
    // That reply breaks the contract and fails loudly rather than being half-used.
    [Fact]
    public async Task NullPayeeIndex_Throws()
    {
        var provider = TestAiProviders.Provider(
            """{"results":[{"lineIndex":0,"payeeIndex":null,"newPayeeName":"Coffee Shop"}]}""");

        await Assert.ThrowsAsync<JsonException>(() => Sut(provider).ResolveAsync(
            [new PayeeResolutionInput(0, "COFFEE SHOP #12")], [],
            TestContext.Current.CancellationToken));
    }

    // The same contract break with the key left out: an int would silently default to 0 and match the first
    // catalog payee, so a missing payeeIndex has to fail as loudly as a null one.
    [Fact]
    public async Task MissingPayeeIndex_Throws()
    {
        var provider = TestAiProviders.Provider(
            """{"results":[{"lineIndex":0,"newPayeeName":"Blue Bottle Coffee"}]}""");

        await Assert.ThrowsAsync<JsonException>(() => Sut(provider).ResolveAsync(
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
