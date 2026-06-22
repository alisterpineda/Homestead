using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class AiClassificationServiceTests
{
    private readonly IAiClientFactory _factory = Substitute.For<IAiClientFactory>();
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private readonly AiClassificationService _sut;

    public AiClassificationServiceTests()
    {
        _factory.IsConfigured.Returns(true);
        _factory.CreateClient().Returns(_client);
        _sut = SutWith(new AiOptions());
    }

    // Builds a service over the shared (capturing) client with a given options budget — used by the
    // budget tests to set AiOptions.MaxInputChars instead of the old per-provider Configuration JSON.
    private AiClassificationService SutWith(AiOptions options) =>
        new(_factory, Options.Create(options), NullLogger<AiClassificationService>.Instance);

    private List<ChatMessage>? _captured;
    private ChatOptions? _capturedOptions;

    private void RespondWith(string json)
    {
        _client.GetResponseAsync(
            Arg.Do<IEnumerable<ChatMessage>>(m => _captured = m.ToList()),
            Arg.Do<ChatOptions>(o => _capturedOptions = o), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))));
    }

    private string SystemPrompt => _captured![0].Text;
    private string UserMessage => _captured![1].Text;

    // Arg.Is takes an expression tree, which can't hold an is-pattern — so the format checks live in
    // plain methods the expression can call. A schema present => structured outputs; absent => JSON mode.
    private static bool IsStructuredOutput(ChatOptions o) =>
        o.ResponseFormat is ChatResponseFormatJson { Schema: not null };

    private static bool IsJsonMode(ChatOptions o) =>
        o.ResponseFormat is ChatResponseFormatJson { Schema: null };

    [Fact]
    public async Task ScopeTags_PromptHasOnlyTagFragments_OmitsPartiesBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        RespondWith("""{"tagIndexes":[0]}""");

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(Guid.NewGuid(), "ACME", null) };

        await _sut.ClassifyAsync("Doc", "some text", tags, parties, ClassificationScope.Tags, ct);

        // System prompt: tag instruction present; party/summary/title absent.
        Assert.Contains("Select applicable tags", SystemPrompt);
        Assert.Contains("tagIndexes", SystemPrompt);
        Assert.DoesNotContain("partyIndexes", SystemPrompt);
        Assert.DoesNotContain("summary", SystemPrompt);
        Assert.DoesNotContain("suggestedTitle", SystemPrompt);

        // User message: tags catalog present, parties catalog omitted (the real token saving).
        Assert.Contains("Available tags:", UserMessage);
        Assert.DoesNotContain("Available parties:", UserMessage);

        // Catalog is keyed by positional index, never the opaque GUID (1.3 — no GUID transcription).
        Assert.Contains("\"index\":0", UserMessage);
        Assert.DoesNotContain(tagId.ToString(), UserMessage);
    }

    [Fact]
    public async Task ScopeSummary_UserMessageOmitsBothCatalogs()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"A short summary."}""");

        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(Guid.NewGuid(), "ACME", null) };

        await _sut.ClassifyAsync("Doc", "some text", tags, parties, ClassificationScope.Summary, ct);

        Assert.DoesNotContain("Available tags:", UserMessage);
        Assert.DoesNotContain("Available parties:", UserMessage);
        Assert.Contains("Extracted text:", UserMessage);
        Assert.Contains("Write a concise", SystemPrompt);
        Assert.DoesNotContain("tagIndexes", SystemPrompt);
        Assert.DoesNotContain("partyIndexes", SystemPrompt);
    }

    [Fact]
    public async Task Guard_SummaryScope_NoText_NoCatalogs_ReturnsNull_WithoutCallingClient()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.ClassifyAsync(
            "Doc", extractedText: null, [], [], ClassificationScope.Summary, ct);

        Assert.Null(result);
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Guard_ScopeNone_ReturnsNull_WithoutCallingClient()
    {
        // A malformed Fields request can map to None; never build a degenerate empty-schema prompt.
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var result = await _sut.ClassifyAsync(
            "Doc", "some text", tags, [], ClassificationScope.None, ct);

        Assert.Null(result);
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfScopeModelOutput_IsNeutralized()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var partyId = Guid.NewGuid();

        // Model returns everything, but only Tags is in scope.
        RespondWith("""
            {"tagIndexes":[0],"partyIndexes":[0],"summary":"S","suggestedTitle":"New Title"}
            """);

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(partyId, "ACME", null) };

        var result = await _sut.ClassifyAsync(
            "Original Name", "some text", tags, parties, ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);
        Assert.Empty(result.MatchedPartyIds);                 // Parties out of scope -> []
        Assert.Equal(string.Empty, result.Summary);            // Summary out of scope -> ""
        Assert.Equal("Original Name", result.SuggestedTitle);  // Name out of scope -> unchanged name
    }

    [Fact]
    public async Task TagIndexes_MapToIds_DroppingOutOfRange_AndDeduping()
    {
        var ct = TestContext.Current.CancellationToken;
        var t0 = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var tags = new List<TagInfo> { new(t0, "A", "A"), new(t1, "B", "B") };

        // 1 repeated, 5 (out of range), -1 (out of range), then 0.
        RespondWith("""{"tagIndexes":[1,1,5,-1,0]}""");

        var result = await _sut.ClassifyAsync("Doc", "text", tags, [], ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([t1, t0], result!.MatchedTagIds);  // order preserved, duplicates + out-of-range dropped
    }

    [Fact]
    public async Task Catalogs_AreKeyedByPositionalIndex_AndIndexesMapBack_ForTagsAndParties()
    {
        // The riskiest part of 1.3: BuildUserMessage labels catalog entries by list position, and MapIndexes maps the
        // returned positions back via that same order. Exercise both catalogs with >1 entry so position truly matters.
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo>
        {
            new(Guid.NewGuid(), "Finance", "Finance"),
            new(Guid.NewGuid(), "Invoices", "Finance/Invoices"),
            new(Guid.NewGuid(), "Utilities", "Home/Utilities")
        };
        var parties = new List<PartyInfo>
        {
            new(Guid.NewGuid(), "Acme", null),
            new(Guid.NewGuid(), "Globex", "A supplier")
        };

        // Model picks tags 2 and 0 (out of order) and party 1.
        RespondWith("""{"tagIndexes":[2,0],"partyIndexes":[1]}""");

        var result = await _sut.ClassifyAsync(
            "Doc", "text", tags, parties, ClassificationScope.Tags | ClassificationScope.Parties, ct);

        // Each emitted catalog entry's "index" equals its array position, and no GUID is leaked into the prompt.
        AssertCatalogIsPositional(UserMessage, "Available tags:", [.. tags.Select(t => t.Name)]);
        AssertCatalogIsPositional(UserMessage, "Available parties:", [.. parties.Select(p => p.Name)]);
        foreach (var t in tags) Assert.DoesNotContain(t.Id.ToString(), UserMessage);
        foreach (var p in parties) Assert.DoesNotContain(p.Id.ToString(), UserMessage);

        // Returned indexes map back to the ids at those positions, preserving the model's order.
        Assert.NotNull(result);
        Assert.Equal([tags[2].Id, tags[0].Id], result!.MatchedTagIds);
        Assert.Equal([parties[1].Id], result.MatchedPartyIds);
    }

    // Parses the JSON array following a catalog label in the user message and asserts each element's `index` equals
    // its position and its `name` matches the expected name there — locking the prompt-side half of the 1.3 contract.
    private static void AssertCatalogIsPositional(string message, string label, string[] expectedNames)
    {
        var labelAt = message.IndexOf(label, StringComparison.Ordinal);
        Assert.True(labelAt >= 0, $"'{label}' not found in user message");
        var open = message.IndexOf('[', labelAt);
        var close = message.IndexOf(']', open);
        var json = message[open..(close + 1)];

        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.EnumerateArray().ToArray();
        Assert.Equal(expectedNames.Length, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            Assert.Equal(i, items[i].GetProperty("index").GetInt32());
            Assert.Equal(expectedNames[i], items[i].GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task SetsZeroTemperature_AndPerScopeStructuredOutputSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"tagIndexes":[]}""");
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        await _sut.ClassifyAsync("Doc", "text", tags, [], ClassificationScope.Tags, ct);

        // 1.1 — deterministic sampling.
        Assert.Equal(0f, _capturedOptions!.Temperature);

        // 1.2 — strict mode must be explicitly opted in, or Microsoft.Extensions.AI sends the schema as advisory.
        Assert.NotNull(_capturedOptions.AdditionalProperties);
        Assert.True(_capturedOptions.AdditionalProperties!.TryGetValue("strict", out var strict) && strict is true);

        // 1.2 — structured outputs: a JSON schema constrained to exactly the in-scope field, strict-mode valid.
        var format = Assert.IsType<ChatResponseFormatJson>(_capturedOptions.ResponseFormat);
        Assert.NotNull(format.Schema);
        var schema = format.Schema!.Value;

        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("tagIndexes", out _));
        Assert.False(properties.TryGetProperty("partyIndexes", out _));   // out of scope -> absent
        Assert.False(properties.TryGetProperty("summary", out _));
        Assert.False(properties.TryGetProperty("suggestedTitle", out _));

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["tagIndexes"], required);
    }

    [Fact]
    public async Task StructuredOutputRejectedWith400_FallsBackToJsonMode_AndParses()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(400);
        var schemaUnsupported = new ClientResultException("json_schema response format not supported", response);

        // Strict attempt (schema present) is rejected; the plain JSON-mode retry (no schema) succeeds.
        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o)),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(schemaUnsupported);

        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsJsonMode(o) && o.Temperature == 0f),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"tagIndexes":[0]}"""))));

        var result = await _sut.ClassifyAsync("Doc", "some text", tags, [], ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);

        // Both response formats were attempted: strict first, then the JSON-mode fallback — and determinism
        // (temperature 0) is preserved on the fallback, since only the schema was at fault here.
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o)),
            Arg.Any<CancellationToken>());
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsJsonMode(o) && o.Temperature == 0f),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TemperatureRejectedWith400_RetriesWithoutTemperature_KeepingStructuredOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(400);
        // o-series-style rejection that names the offending parameter.
        var temperatureUnsupported = new ClientResultException(
            "Unsupported value: 'temperature' does not support 0 with this model. Only the default (1) value is supported.",
            response);

        // Structured output WITH a custom temperature is rejected...
        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o) && o.Temperature != null),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(temperatureUnsupported);

        // ...but structured output WITHOUT a temperature succeeds.
        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o) && o.Temperature == null),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"tagIndexes":[0]}"""))));

        var result = await _sut.ClassifyAsync("Doc", "some text", tags, [], ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);

        // The reasoning-model case: only temperature was dropped — structured outputs were preserved,
        // and we never degraded all the way to plain JSON mode.
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o) && o.Temperature == null),
            Arg.Any<CancellationToken>());
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsJsonMode(o)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unrelated400_IsNotRetried_AndPropagates()
    {
        // A 400 that names neither temperature nor the response format (here: context-length-exceeded) must NOT be
        // treated as a capability problem — no silent retry of the same oversized request, and the real error surfaces.
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(400);
        var contextLength = new ClientResultException(
            "This model's maximum context length is 8192 tokens, however you requested 9000 tokens.", response);

        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(contextLength);

        await Assert.ThrowsAsync<ClientResultException>(() =>
            _sut.ClassifyAsync("Doc", "some text", tags, [], ClassificationScope.Tags, ct));

        // Exactly one attempt — the error was surfaced, not masked behind a degraded retry.
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BothTemperatureAndStructuredOutputRejected_DegradesToJsonModeWithoutTemperature()
    {
        // A 400 naming both parameters drops both at once: the single retry is plain JSON mode with the default
        // temperature. Also pins the retry budget — two calls total, never an unbounded loop.
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(400);
        var bothRejected = new ClientResultException(
            "Unsupported: 'temperature' and 'response_format' are not supported with this model.", response);

        // Anything that still sets a custom temperature OR structured output is rejected...
        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsStructuredOutput(o) || o.Temperature != null),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(bothRejected);
        // ...only plain JSON mode with the default temperature succeeds.
        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => IsJsonMode(o) && o.Temperature == null),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"tagIndexes":[0]}"""))));

        var result = await _sut.ClassifyAsync("Doc", "some text", tags, [], ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);
        await _client.Received(2).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    // A body long enough to exceed the default budget but well under the raised one, with whitespace-free
    // start/end sentinels so we can assert verbatim inclusion.
    private static string MakeLongText() =>
        "DOC_START_SENTINEL" + new string('a', 20_000) + "DOC_END_SENTINEL";

    [Fact]
    public async Task ConfiguredMaxInputChars_RaisesBudget_SendsTextVerbatim_NoElision()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"S"}""");
        var sut = SutWith(new AiOptions { MaxInputChars = 40_000 });

        await sut.ClassifyAsync("Doc", MakeLongText(), [], [], ClassificationScope.Summary, ct);

        // 20k-char body fits inside the 40k budget -> sent whole, no elision, both ends present.
        Assert.DoesNotContain("[...document text omitted...]", UserMessage);
        Assert.Contains("DOC_START_SENTINEL", UserMessage);
        Assert.Contains("DOC_END_SENTINEL", UserMessage);
    }

    [Fact]
    public async Task DefaultBudget_LongText_IsSampledWithElisionMarker()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"S"}""");

        // Default AiOptions — no override, so the default budget applies and the 20k body is sampled.
        await _sut.ClassifyAsync("Doc", MakeLongText(), [], [], ClassificationScope.Summary, ct);

        Assert.Contains("[...document text omitted...]", UserMessage);
    }

    [Fact]
    public async Task SubFloorMaxInputChars_FallsBackToDefaultBudget_AndSamples()
    {
        // A degenerate sub-floor value: the service floor ignores it and uses the default budget, so the long
        // body is still sampled rather than sent at a 10-char budget.
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"S"}""");
        var sut = SutWith(new AiOptions { MaxInputChars = 10 });

        await sut.ClassifyAsync("Doc", MakeLongText(), [], [], ClassificationScope.Summary, ct);

        Assert.Contains("[...document text omitted...]", UserMessage);
        Assert.Contains("DOC_START_SENTINEL", UserMessage);   // not truncated to ~10 chars
    }

    [Fact]
    public async Task NonSchemaError_IsNotRetried_AndPropagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(429);  // transient rate-limit — not a schema-support problem
        var rateLimited = new ClientResultException("rate limited", response);

        _client.GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(rateLimited);

        await Assert.ThrowsAsync<ClientResultException>(() =>
            _sut.ClassifyAsync("Doc", "some text", tags, [], ClassificationScope.Tags, ct));

        // No JSON-mode fallback for a non-400 error — only the single strict attempt was made.
        await _client.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }
}
