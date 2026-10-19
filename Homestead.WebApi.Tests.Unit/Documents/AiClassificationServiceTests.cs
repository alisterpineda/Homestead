using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class AiClassificationServiceTests
{
    private readonly IAiProvider _provider = Substitute.For<IAiProvider>();
    private readonly AiClassificationService _sut;

    public AiClassificationServiceTests()
    {
        _provider.Availability.Returns(AiAvailability.Ready);
        _sut = SutWith(TestAiProviders.Settings());
    }

    // Builds a service over the shared (capturing) provider with given input settings — used by the
    // budget tests to set MaxInputChars.
    private AiClassificationService SutWith(AiInputSettings settings) =>
        new(_provider, settings, NullLogger<AiClassificationService>.Instance);

    private AiRequest? _captured;

    private void RespondWith(string json)
    {
        _provider.SendAsync(Arg.Do<AiRequest>(r => _captured = r), Arg.Any<CancellationToken>())
            .Returns(TestAiProviders.Reply(json));
    }

    private string SystemPrompt => _captured!.SystemPrompt;
    private string UserMessage => _captured!.UserText();

    [Fact]
    public async Task ScopeTags_PromptHasOnlyTagFragments_OmitsPartiesBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        RespondWith("""{"tagIndexes":[0]}""");

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(Guid.NewGuid(), "ACME", null) };

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), tags, parties, ClassificationScope.Tags, ct);

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

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), tags, parties, ClassificationScope.Summary, ct);

        Assert.DoesNotContain("Available tags:", UserMessage);
        Assert.DoesNotContain("Available parties:", UserMessage);
        Assert.Contains("Extracted text:", UserMessage);
        Assert.Contains("Write a concise", SystemPrompt);
        Assert.DoesNotContain("tagIndexes", SystemPrompt);
        Assert.DoesNotContain("partyIndexes", SystemPrompt);
    }

    [Fact]
    public async Task Guard_SummaryScope_NoText_NoCatalogs_ReturnsNull_WithoutCallingProvider()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", null, null, null), [], [], ClassificationScope.Summary, ct);

        Assert.Null(result);
        await _provider.DidNotReceive().SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Guard_ScopeNone_ReturnsNull_WithoutCallingProvider()
    {
        // A malformed Fields request can map to None; never build a degenerate empty-schema prompt.
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), tags, [], ClassificationScope.None, ct);

        Assert.Null(result);
        await _provider.DidNotReceive().SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfScopeModelOutput_IsNeutralized()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var partyId = Guid.NewGuid();

        // Model returns everything, but only Tags is in scope.
        RespondWith("""
            {"tagIndexes":[0],"partyIndexes":[0],"summary":"S","suggestedTitle":"New Title",
             "documentDate":"2025-01-15","expiryDate":"2026-01-15"}
            """);

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(partyId, "ACME", null) };

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Original Name", "some text", null, null), tags, parties,
            ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);
        Assert.Empty(result.MatchedPartyIds);                 // Parties out of scope -> []
        Assert.Equal(string.Empty, result.Summary);            // Summary out of scope -> ""
        Assert.Equal("Original Name", result.SuggestedTitle);  // Name out of scope -> unchanged name
        Assert.Null(result.DocumentDate);                      // Dates out of scope -> null
        Assert.Null(result.ExpiryDate);
    }

    [Fact]
    public async Task DateScopes_AddTheirPromptFragments_AndOnlyTheirSchemaKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"documentDate":null}""");

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [], ClassificationScope.DocumentDate, ct);

        Assert.Contains("documentDate: The date the document was issued", SystemPrompt);
        Assert.DoesNotContain("expiryDate", SystemPrompt);
        Assert.Contains("YYYY-MM-DD", SystemPrompt);
        Assert.Contains("never guess", SystemPrompt);

        var properties = _captured!.Schema.GetProperty("properties");
        Assert.Equal(["documentDate"], properties.EnumerateObject().Select(p => p.Name));
        var required = _captured.Schema.GetProperty("required").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(["documentDate"], required);
    }

    [Fact]
    public async Task DateSchemaProperties_AreNullableStrings_AndRequired()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"documentDate":null,"expiryDate":null}""");

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [],
            ClassificationScope.DocumentDate | ClassificationScope.ExpiryDate, ct);

        Assert.Contains("expiryDate: The date the document itself stops being valid", SystemPrompt);
        var schema = _captured!.Schema;
        foreach (var key in new[] { "documentDate", "expiryDate" })
        {
            var property = schema.GetProperty("properties").GetProperty(key);
            Assert.Equal(["string", "null"], property.GetProperty("type").EnumerateArray().Select(e => e.GetString()));
            Assert.False(property.TryGetProperty("format", out _));
        }
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["documentDate", "expiryDate"], required);
    }

    [Fact]
    public async Task ValidIsoDates_AreParsed()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"documentDate":"2025-03-04","expiryDate":"2030-12-31"}""");

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [],
            ClassificationScope.DocumentDate | ClassificationScope.ExpiryDate, ct);

        Assert.Equal(new DateOnly(2025, 3, 4), result!.DocumentDate);
        Assert.Equal(new DateOnly(2030, 12, 31), result.ExpiryDate);
    }

    [Theory]
    [InlineData("1900-01-01")]
    [InlineData("2199-12-31")]
    public async Task PlausibleYearBounds_AreAccepted(string value)
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith($$"""{"documentDate":"{{value}}"}""");

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [], ClassificationScope.DocumentDate, ct);

        Assert.Equal(DateOnly.Parse(value), result!.DocumentDate);
    }

    [Theory]
    [InlineData("\"2025-13-01\"")]
    [InlineData("\"03/04/2025\"")]
    [InlineData("\"1850-06-01\"")]
    [InlineData("\"1899-12-31\"")]
    [InlineData("\"2200-01-01\"")]
    [InlineData("null")]
    public async Task UnusableDates_AreDroppedToNull(string value)
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith($$"""{"documentDate":{{value}},"expiryDate":{{value}}}""");

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [],
            ClassificationScope.DocumentDate | ClassificationScope.ExpiryDate, ct);

        Assert.NotNull(result);
        Assert.Null(result!.DocumentDate);
        Assert.Null(result.ExpiryDate);
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

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "text", null, null), tags, [], ClassificationScope.Tags, ct);

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
            new ClassificationInput("Doc", "text", null, null), tags, parties,
            ClassificationScope.Tags | ClassificationScope.Parties, ct);

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
    public async Task SendsClassificationRequest_WithSettingsEffort_AndPerScopeStructuredOutputSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"tagIndexes":[]}""");
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        await SutWith(TestAiProviders.Settings(effort: AiEffort.High))
            .ClassifyAsync(new ClassificationInput("Doc", "text", null, null), tags, [], ClassificationScope.Tags, ct);

        // Tagged for metrics, effort taken from the input settings (never a service default), and a short reply —
        // what reaches the wire from these is the request mapper's business (its own tests cover it).
        Assert.Equal(AiFeature.Classification, _captured!.Feature);
        Assert.Equal(AiEffort.High, _captured.Effort);
        Assert.False(_captured.LongOutput);

        // 1.2 — structured outputs: a JSON schema constrained to exactly the in-scope field, strict-mode valid.
        var schema = _captured.Schema;

        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("tagIndexes", out _));
        Assert.False(properties.TryGetProperty("partyIndexes", out _));   // out of scope -> absent
        Assert.False(properties.TryGetProperty("summary", out _));
        Assert.False(properties.TryGetProperty("suggestedTitle", out _));

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["tagIndexes"], required);
    }

    // Every 400 propagates after one attempt — including one naming the schema format. Nothing reads error text to
    // retry with a degraded request, so a mismatch surfaces instead of degrading quietly.
    [Theory]
    [InlineData("prompt is too long: 250000 tokens > 200000 maximum")]
    [InlineData("output_config.format: schema not supported")]
    public async Task Rejected400_IsNotRetried_AndPropagates(string message)
    {
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var rejected = new AnthropicBadRequestException
        {
            StatusCode = HttpStatusCode.BadRequest, ResponseBody = message,
        };

        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(rejected);

        var thrown = await Assert.ThrowsAsync<AnthropicBadRequestException>(() =>
            _sut.ClassifyAsync(
                new ClassificationInput("Doc", "some text", null, null), tags, [], ClassificationScope.Tags, ct));

        Assert.Same(rejected, thrown);

        // Exactly one attempt — the error was surfaced, not masked behind a degraded retry.
        await _provider.Received(1).SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
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
        var sut = SutWith(TestAiProviders.Settings(maxInputChars: 40_000));

        await sut.ClassifyAsync(
            new ClassificationInput("Doc", MakeLongText(), null, null), [], [], ClassificationScope.Summary, ct);

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

        // Default settings — no override, so the default budget applies and the 20k body is sampled.
        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", MakeLongText(), null, null), [], [], ClassificationScope.Summary, ct);

        Assert.Contains("[...document text omitted...]", UserMessage);
    }

    [Fact]
    public async Task SubFloorMaxInputChars_FallsBackToDefaultBudget_AndSamples()
    {
        // A degenerate sub-floor value: resolving the settings ignores it and uses the default budget, so the long
        // body is still sampled rather than sent at a 10-char budget.
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"S"}""");
        var sut = SutWith(AiInputSettings.From(new AiOptions { MaxInputChars = 10 }));

        await sut.ClassifyAsync(
            new ClassificationInput("Doc", MakeLongText(), null, null), [], [], ClassificationScope.Summary, ct);

        Assert.Contains("[...document text omitted...]", UserMessage);
        Assert.Contains("DOC_START_SENTINEL", UserMessage);   // not truncated to ~10 chars
    }

    [Fact]
    public async Task Non400Error_IsNotRetried_AndPropagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        // Transient rate-limit — not a schema-support problem.
        var rateLimited = new AnthropicRateLimitException
        {
            StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "",
        };

        _provider.SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(rateLimited);

        await Assert.ThrowsAsync<AnthropicRateLimitException>(() =>
            _sut.ClassifyAsync(
                new ClassificationInput("Doc", "some text", null, null), tags, [], ClassificationScope.Tags, ct));

        // Retrying a transient error is the job queue's call, not the request's — one attempt.
        await _provider.Received(1).SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }

    // ---- Image input (multimodal user turn) -------------------------------------------------------

    [Fact]
    public async Task PageImagePresent_UserContentCarriesPngImageBeforeText()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"suggestedTitle":"T"}""");
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", image, null), [], [], ClassificationScope.Name, ct);

        // The user content is multimodal: an image/png part carrying the bytes, then the text part.
        Assert.Collection(_captured!.UserContent,
            part =>
            {
                var data = Assert.IsType<AiImagePart>(part);
                Assert.Equal("image/png", data.MediaType);
                Assert.Equal(image, data.Bytes);
            },
            part => Assert.IsType<AiTextPart>(part));

        // The text part is still the full prompt (name + extracted text), unchanged by the image.
        Assert.Contains("Extracted text:", UserMessage);
    }

    [Fact]
    public async Task NoPageImage_UserContentIsPlainText_NoImagePart()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"suggestedTitle":"T"}""");

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, null), [], [], ClassificationScope.Name, ct);

        Assert.IsType<AiTextPart>(Assert.Single(_captured!.UserContent));
    }

    // ---- PDF input (the PDF itself replaces the page render) --------------------------------------

    private static readonly byte[] PdfBytes = "%PDF-1.7 test"u8.ToArray();

    // The production PDF path: DocumentClassifier passes no OCR text, so the attached PDF is the only source — the
    // text part carries the name and catalogs, never an "Extracted text:" block.
    [Fact]
    public async Task PdfWithoutText_PdfPartFirst_NoExtractedTextBlock_CatalogsStillSent()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"tagIndexes":[],"suggestedTitle":"T"}""");
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", null, null, PdfBytes), tags, [],
            ClassificationScope.Tags | ClassificationScope.Name, ct);

        Assert.Collection(_captured!.UserContent,
            part => Assert.Equal(PdfBytes, Assert.IsType<AiPdfPart>(part).Bytes),
            part => Assert.IsType<AiTextPart>(part));
        Assert.Contains("Document name: Doc", UserMessage);
        Assert.DoesNotContain("Extracted text:", UserMessage);
        Assert.Contains("Available tags:", UserMessage);
        Assert.Contains("Given a document (attached) and its name", SystemPrompt);
    }

    // The service sends whatever the input carries: a PDF with text gets both, and the prompt says so.
    [Fact]
    public async Task PdfWithText_SendsPdfPart_AndExtractedTextBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"suggestedTitle":"T"}""");

        await _sut.ClassifyAsync(
            new ClassificationInput("Doc", "some text", null, PdfBytes), [], [], ClassificationScope.Name, ct);

        Assert.IsType<AiPdfPart>(_captured!.UserContent[0]);
        Assert.Contains("Extracted text:", UserMessage);
        Assert.Contains("some text", UserMessage);
        Assert.Contains("Given a document (attached), its name and its extracted text content", SystemPrompt);
    }

    // A PDF is a source signal on its own (a scan with no OCR text), so a Summary-only call with no text and no
    // catalogs still reaches the model — unlike the same call with nothing attached (the guard test above).
    [Fact]
    public async Task PdfAlone_NoTextNoCatalogs_StillCallsTheModel()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"S"}""");

        var result = await _sut.ClassifyAsync(
            new ClassificationInput("Doc", null, null, PdfBytes), [], [], ClassificationScope.Summary, ct);

        Assert.NotNull(result);
        Assert.Equal("S", result!.Summary);
        await _provider.Received(1).SendAsync(Arg.Any<AiRequest>(), Arg.Any<CancellationToken>());
    }
}
