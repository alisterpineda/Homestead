using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class DocumentClassifierTests : IDisposable
{
    private readonly TestAppDbContext _db;
    // Ready by default; tests flip its Availability to exercise the gate.
    private readonly IAiProvider _aiProvider = TestAiProviders.Provider();
    private readonly IAiClassificationService _classificationService;
    private readonly AiPageRenderService _aiPageRenderService;
    private readonly DocumentClassifier _sut;

    // A real directory: the PDF-mode tests write a PDF under it, which the classifier opens and reads.
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), $"doc-classifier-test-{Guid.NewGuid()}");

    private readonly Guid _documentId = Guid.NewGuid();

    public DocumentClassifierTests()
    {
        _db = TestAppDbContext.Create();
        _classificationService = Substitute.For<IAiClassificationService>();
        // GetOrCreateAsync is virtual; substitute it to assert the scope/config gate without real I/O.
        _aiPageRenderService = Substitute.For<AiPageRenderService>(
            Substitute.For<IDocumentPageRenderer>(),
            Options.Create(new FileStorageOptions { MediaPath = _mediaPath, CachePath = "/cache" }),
            Substitute.For<ILogger<AiPageRenderService>>());
        // PDF input off for the base suite, so the text/render-path tests don't hinge on the seeded (never written)
        // file failing to open; the PDF-mode tests build their sut with it on.
        _sut = CreateSut(TestAiProviders.Settings(pdfInput: false));
    }

    // The input settings are an immutable record, so a test needing other knobs (ImageInput) builds its own sut.
    private DocumentClassifier CreateSut(AiInputSettings settings) =>
        new(_db, _aiProvider, settings, _classificationService,
            _aiPageRenderService,
            Options.Create(new FileStorageOptions { MediaPath = _mediaPath, CachePath = "/cache" }),
            NullLogger<DocumentClassifier>.Instance);

    // Matches a text-path input (no PDF) by name and text, and — in the second form — the exact page image.
    private static ClassificationInput Input(string name, string? text) =>
        Arg.Is<ClassificationInput>(i => i.DocumentName == name && i.ExtractedText == text && i.Pdf == null);

    private static ClassificationInput Input(string name, string? text, byte[]? image) =>
        Arg.Is<ClassificationInput>(i =>
            i.DocumentName == name && i.ExtractedText == text && i.Pdf == null && i.PageImage == image);

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_mediaPath, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // ---- ClassifyAsync (pure — no document writes) -------------------------------------------------

    [Fact]
    public async Task ClassifyAsync_Configured_LoadsInputs_CallsAiService()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("extracted text here");
        SeedTag(tagId, "Finance", "Finance");

        _classificationService.ClassifyAsync(
            Input("Test Doc", "extracted text here"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([tagId], [], "A summary", "Better Title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.NotNull(result);
        Assert.Equal("Better Title", result!.Classification.SuggestedTitle);
        Assert.Equal("A summary", result.Classification.Summary);
        Assert.Single(result.Classification.MatchedTagIds);
        Assert.Equal(tagId, result.Classification.MatchedTagIds[0]);
        Assert.Single(result.AvailableTags);
    }

    [Fact]
    public async Task ClassifyAsync_AiAbsent_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        _aiProvider.Availability.Returns(FeatureAvailability.Absent);

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.Null(result);
        await _classificationService.DidNotReceive().ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());
    }

    // Present-but-misconfigured AI config gates exactly like the absent state.
    [Fact]
    public async Task ClassifyAsync_AiUnavailable_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        _aiProvider.Availability.Returns(FeatureAvailability.Unavailable("Ai:ApiKey is required."));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.Null(result);
        await _classificationService.DidNotReceive().ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AiServiceReturnsNull_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");

        _classificationService.ClassifyAsync(
            Input("Test Doc", "some text"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns((ClassificationResult?)null);

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ClassifyAsync_UsesLatestVersion()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 1,
            ExtractedText = "old text",
            ContentType = "application/pdf",
            FileSize = 100,
            FilePath = "test/old.pdf",
            FileHash = "oldhash",
            FileExtension = ".pdf"
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 2,
            ExtractedText = "latest text",
            ContentType = "application/pdf",
            FileSize = 200,
            FilePath = "test/new.pdf",
            FileHash = "newhash",
            FileExtension = ".pdf"
        });
        await _db.SaveChangesAsync(ct);

        _classificationService.ClassifyAsync(
            Input("Test Doc", "latest text"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "summary", "title"));

        var result = await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.NotNull(result);
        await _classificationService.Received(1).ClassifyAsync(
            Input("Test Doc", "latest text"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct);
    }

    [Fact]
    public async Task ClassifyAsync_InMemoryOverload_UsesProvidedText_NoDocumentLoad()
    {
        // The in-memory overload classifies pre-creation (no persisted document/version).
        var ct = TestContext.Current.CancellationToken;

        _classificationService.ClassifyAsync(
            Input("Upload Name", "in-memory text"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "summary", "title"));

        var result = await _sut.ClassifyAsync("Upload Name", "in-memory text", null, ClassificationScope.All, ct);

        Assert.NotNull(result);
        await _classificationService.Received(1).ClassifyAsync(
            Input("Upload Name", "in-memory text"),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct);
    }

    [Fact]
    public async Task ClassifyAsync_AiAbsent_InMemoryOverload_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        _aiProvider.Availability.Returns(FeatureAvailability.Absent);
        var file = new ClassificationFile("hash", Path.Combine(_mediaPath, "upload.png"), "image/png", 100);

        var result = await _sut.ClassifyAsync("Upload Name", "in-memory text", file, ClassificationScope.All, ct);

        Assert.Null(result);
        // AI unconfigured -> no render (and no cache write) for a classify that would no-op anyway.
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classificationService.DidNotReceive().ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>());
    }

    // ---- Image-augmentation scope gate (ClassifyAsync overload a) ---------------------------------

    [Fact]
    public async Task ClassifyAsync_ById_ImageInputOn_ScopeAttachesImage_RendersAndForwardsBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("text");
        var image = new byte[] { 1, 2, 3 };
        _aiPageRenderService.GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct).Returns(image);
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "s", "t"));

        await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        // Render keyed by the latest version's FileHash + physical path + content type, at the resolved
        // settings' ImageRenderMaxEdge (default -> 1024).
        await _aiPageRenderService.Received(1).GetOrCreateAsync(
            "abc123", Path.Combine(_mediaPath, "test/file.pdf"), "application/pdf",
            AiOptions.DefaultImageRenderMaxEdge, ct);
        // The rendered bytes reach the AI service.
        await _classificationService.Received(1).ClassifyAsync(
            Input("Test Doc", "text", image: image),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), ClassificationScope.All, ct);
    }

    [Fact]
    public async Task ClassifyAsync_ById_TagsOnlyScope_DoesNotRenderImage()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("text");
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "", "Test Doc"));

        await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.Tags, ct);

        // Tags-only does not satisfy ShouldAttachImage -> no render, null page image.
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classificationService.Received(1).ClassifyAsync(
            Input("Test Doc", "text", image: null),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), ClassificationScope.Tags, ct);
    }

    [Fact]
    public async Task ClassifyAsync_ById_ImageInputOff_DoesNotRenderImage()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("text");
        var sut = CreateSut(TestAiProviders.Settings(imageInput: false));
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "s", "t"));

        await sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        // Kill-switch off -> no render even though the scope (All) would otherwise attach an image.
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classificationService.Received(1).ClassifyAsync(
            Input("Test Doc", "text", image: null),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), ClassificationScope.All, ct);
    }

    [Fact]
    public async Task ClassifyAsync_ById_AttachImageFalse_DoesNotRenderImage_EvenWhenScopeWould()
    {
        // The synchronous preview/summarize endpoints pass attachImage:false so image-carrying classification
        // never runs on a synchronous request, even though the scope (All) would otherwise attach one.
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("text");
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "s", "t"));

        await _sut.ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct, attachImage: false);

        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _classificationService.Received(1).ClassifyAsync(
            Input("Test Doc", "text", image: null),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), ClassificationScope.All, ct);
    }

    [Fact]
    public async Task ClassifyAsync_InMemoryOverload_RendersTheProvidedFile_AndForwardsBytes()
    {
        // The upload path: the file isn't a persisted version yet, so the caller names it (hash, staged path, type).
        var ct = TestContext.Current.CancellationToken;
        var image = new byte[] { 9, 8, 7 };
        var path = Path.Combine(_mediaPath, "upload.png");
        _aiPageRenderService.GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct).Returns(image);
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(new ClassificationResult([], [], "s", "t"));

        await _sut.ClassifyAsync(
            "Upload Name", "in-memory text", new ClassificationFile("uphash", path, "image/png", 100),
            ClassificationScope.All, ct);

        await _aiPageRenderService.Received(1).GetOrCreateAsync(
            "uphash", path, "image/png", AiOptions.DefaultImageRenderMaxEdge, ct);
        await _classificationService.Received(1).ClassifyAsync(
            Input("Upload Name", "in-memory text", image: image),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), ClassificationScope.All, ct);
    }

    // ---- PDF input (ClassificationInputSelector decides; the classifier reads the file) -----------

    private const string RealPdfPath = "test/real.pdf";

    // Writes a real PDF under the media path and seeds a version pointing at it (its true size, so the byte cap
    // sees the real number).
    private byte[] SeedDocumentWithPdf(string? extractedText, int pages = 1)
    {
        var bytes = TestPdfs.Write(Path.Combine(_mediaPath, RealPdfPath), pages);
        SeedDocumentWithVersion(extractedText, RealPdfPath, bytes.Length);
        return bytes;
    }

    // Records every input the service is sent, answering each with an empty result.
    private List<ClassificationInput> CaptureInputs()
    {
        var inputs = new List<ClassificationInput>();
        _classificationService.ClassifyAsync(
            Arg.Do<ClassificationInput>(inputs.Add),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), Arg.Any<CancellationToken>())
            .Returns(new ClassificationResult([], [], "s", "t"));
        return inputs;
    }

    [Fact]
    public async Task ClassifyAsync_ById_QualifyingPdf_SendsThePdf_WithoutTextOrRender()
    {
        var ct = TestContext.Current.CancellationToken;
        var pdf = SeedDocumentWithPdf("ocr text");
        var inputs = CaptureInputs();

        await CreateSut(TestAiProviders.Settings()).ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        // The PDF replaces the OCR text (Claude reads it itself) and the render — no render is even made.
        var input = Assert.Single(inputs);
        Assert.Equal(pdf, input.Pdf);
        Assert.Null(input.ExtractedText);
        Assert.Null(input.PageImage);
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_ById_AttachImageFalse_QualifyingPdf_SendsTextOnly()
    {
        // The synchronous summarize endpoint opts out of document-carrying classification: no file is offered, so
        // neither the PDF nor a render goes out, whatever the selector would have picked.
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithPdf("ocr text");
        var inputs = CaptureInputs();

        await CreateSut(TestAiProviders.Settings())
            .ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct, attachImage: false);

        var input = Assert.Single(inputs);
        Assert.Null(input.Pdf);
        Assert.Null(input.PageImage);
        Assert.Equal("ocr text", input.ExtractedText);
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_ById_PdfOverThePageCap_FallsToTextAndRender()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithPdf("ocr text", pages: 3);
        var image = new byte[] { 1, 2, 3 };
        _aiPageRenderService.GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct).Returns(image);
        var inputs = CaptureInputs();

        await CreateSut(TestAiProviders.Settings(pdfInputMaxPages: 2))
            .ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        var input = Assert.Single(inputs);
        Assert.Null(input.Pdf);
        Assert.Equal("ocr text", input.ExtractedText);
        Assert.Same(image, input.PageImage);
        await _aiPageRenderService.Received(1).GetOrCreateAsync(
            "abc123", Path.Combine(_mediaPath, RealPdfPath), "application/pdf", AiOptions.DefaultImageRenderMaxEdge, ct);
    }

    // A PDF the API refuses (a 4xx a retry can't fix) doesn't fail the classify: one retry on the text + render
    // path, with the OCR text back. Likewise an endpoint with no upload route (the provider's
    // AiPdfRejectedException, from a 404 that would otherwise not qualify).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassifyAsync_PdfRejected_RetriesOnceWithTextAndRender(bool noUploadRoute)
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithPdf("ocr text");
        var image = new byte[] { 4, 5, 6 };
        _aiPageRenderService.GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct).Returns(image);
        var inputs = new List<ClassificationInput>();
        _classificationService.ClassifyAsync(
            Arg.Do<ClassificationInput>(inputs.Add),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(
                _ => throw (noUploadRoute
                    ? new AiPdfRejectedException("no upload route", 404,
                        new AnthropicNotFoundException { StatusCode = HttpStatusCode.NotFound, ResponseBody = "" })
                    : new AnthropicBadRequestException
                    {
                        StatusCode = HttpStatusCode.BadRequest, ResponseBody = "The PDF could not be processed.",
                    }),
                _ => new ClassificationResult([], [], "s", "t"));

        var result = await CreateSut(TestAiProviders.Settings())
            .ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.NotNull(result);
        Assert.Collection(inputs,
            first => Assert.NotNull(first.Pdf),
            retry =>
            {
                Assert.Null(retry.Pdf);
                Assert.Equal("ocr text", retry.ExtractedText);
                Assert.Same(image, retry.PageImage);
            });
        await _aiPageRenderService.Received(1).GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), ct);
    }

    // The retry honours the image kill-switch like the normal text path: with ImageInput off it goes back on text
    // alone, rendering nothing.
    [Fact]
    public async Task ClassifyAsync_PdfRejectedWithImageInputOff_RetriesOnTextAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithPdf("ocr text");
        var inputs = new List<ClassificationInput>();
        _classificationService.ClassifyAsync(
            Arg.Do<ClassificationInput>(inputs.Add),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .Returns(
                _ => throw new AnthropicBadRequestException
                {
                    StatusCode = HttpStatusCode.BadRequest, ResponseBody = "The PDF could not be processed.",
                },
                _ => new ClassificationResult([], [], "s", "t"));

        var result = await CreateSut(TestAiProviders.Settings(imageInput: false))
            .ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct);

        Assert.NotNull(result);
        Assert.Collection(inputs,
            first => Assert.NotNull(first.Pdf),
            retry =>
            {
                Assert.Null(retry.Pdf);
                Assert.Null(retry.PageImage);
                Assert.Equal("ocr text", retry.ExtractedText);
            });
        await _aiPageRenderService.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default!, default!, default!, default, default);
    }

    // Auth failures would fail the text path the same way, and a rate limit is the job retry policy's business — so
    // in PDF mode neither is retried here; the error propagates after one attempt, with no render.
    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    public async Task ClassifyAsync_PdfModeAuthOrRateLimitError_IsNotRetried_AndPropagates(int status)
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithPdf("ocr text");
        AnthropicApiException error = status == 401
            ? new AnthropicUnauthorizedException { StatusCode = HttpStatusCode.Unauthorized, ResponseBody = "" }
            : new AnthropicRateLimitException { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" };
        _classificationService.ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct)
            .ThrowsAsync(error);

        var thrown = await Assert.ThrowsAsync(error.GetType(), () =>
            CreateSut(TestAiProviders.Settings()).ClassifyAsync(_documentId, "Test Doc", ClassificationScope.All, ct));

        Assert.Same(error, thrown);
        await _classificationService.Received(1).ClassifyAsync(
            Arg.Any<ClassificationInput>(),
            Arg.Any<List<TagInfo>>(), Arg.Any<List<PartyInfo>>(), Arg.Any<ClassificationScope>(), ct);
        await _aiPageRenderService.DidNotReceive().GetOrCreateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // The rejection rule on its own: a non-transient, non-auth 4xx from the AI SDK only.
    [Theory]
    [InlineData(400, true)]
    [InlineData(404, false)]
    [InlineData(413, true)]
    [InlineData(422, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(408, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    public void IsPdfRejection_OnlyForANonTransientNonAuth4xx(int status, bool expected)
    {
        var error = new AnthropicUnexpectedStatusCodeException { StatusCode = (HttpStatusCode)status, ResponseBody = "" };

        Assert.Equal(expected, DocumentClassifier.IsPdfRejection(error, out var reported));
        Assert.Equal(status, reported);
    }

    [Fact]
    public void IsPdfRejection_AlwaysForTheProvidersPdfRejection()
    {
        var error = new AiPdfRejectedException("no upload route", 405, new InvalidOperationException());

        Assert.True(DocumentClassifier.IsPdfRejection(error, out var status));
        Assert.Equal(405, status);
    }

    [Fact]
    public void IsPdfRejection_IgnoresExceptionsFromOutsideTheAiSdk()
    {
        Assert.False(DocumentClassifier.IsPdfRejection(new InvalidOperationException("x"), out _));
    }

    // ---- ApplyAsync (net-diff onto a tracked/new document; no save, no audit) ----------------------

    [Fact]
    public async Task ApplyAsync_AppliesClassification_TagsNameSummary()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "AI summary", "AI Title"),
            ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("AI Title", doc!.Name);
        Assert.Equal("AI summary", doc.Summary);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Name);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Tags);
    }

    [Fact]
    public async Task ApplyAsync_SetsTagNavigation_SoSnapshotResolvesTagPath()
    {
        // The applied DocumentTag carries its Tag navigation, so an in-memory DocumentSnapshot diff
        // resolves Tag.Path without a re-read (this is what feeds the Updated audit at the chokepoint).
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Root/Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "", "Test Doc"),
            ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);

        var added = document.DocumentTags.Single(dt => dt.TagId == tagId);
        Assert.NotNull(added.Tag);
        Assert.Equal("Root/Finance", added.Tag.Path);
    }

    [Fact]
    public async Task ApplyAsync_AppliesParties_AndCreatesPendingSuggestions()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(partyId, "ACME Insurance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [partyId], "AI summary", "Test Doc"),
            ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentParties.Where(dp => dp.DocumentId == _documentId).ToList();
        Assert.Single(links);
        Assert.Equal(partyId, links[0].PartyId);

        var suggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Parties)
            .ToList();
        Assert.Single(suggestions);
        Assert.Equal(partyId.ToString(), suggestions[0].SuggestedValue);
        Assert.Equal(DocumentSuggestionStatus.Pending, suggestions[0].Status);
    }

    [Fact]
    public async Task ApplyAsync_SkipsAlreadyLinkedUserParty_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(partyId, "ACME Insurance");
        _db.DocumentParties.Add(new DocumentParty { DocumentId = _documentId, PartyId = partyId });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [partyId], "AI summary", "Test Doc"),
            ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentParties.Where(dp => dp.DocumentId == _documentId).ToList();
        Assert.Single(links);
        var partySuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Parties)
            .ToList();
        Assert.Empty(partySuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ClearsPendingPartySuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldPartyId = Guid.NewGuid();
        var newPartyId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedParty(oldPartyId, "Old Party");
        SeedParty(newPartyId, "New Party");

        _db.DocumentParties.Add(new DocumentParty { DocumentId = _documentId, PartyId = oldPartyId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Parties,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldPartyId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [newPartyId], "new summary", "Test Doc"),
            ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        // Old AI-applied party link is removed; new one is linked.
        var linkedPartyIds = _db.DocumentParties
            .Where(dp => dp.DocumentId == _documentId)
            .Select(dp => dp.PartyId)
            .ToList();
        Assert.DoesNotContain(oldPartyId, linkedPartyIds);
        Assert.Contains(newPartyId, linkedPartyIds);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .Select(s => s.SuggestedValue)
            .ToList();
        Assert.DoesNotContain(oldPartyId.ToString(), pendingSuggestions);
        Assert.Contains(newPartyId.ToString(), pendingSuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ClearsPendingSuggestions_BeforeApplyingNew()
    {
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(oldTagId, "OldTag", "OldTag");
        SeedTag(newTagId, "NewTag", "NewTag");

        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = oldTagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldTagId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "Old AI Name",
            OriginalValue = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([newTagId], [], "new summary", "New Title"),
            ClassificationScope.All, actorUserId: null, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var pendingSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();

        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == oldTagId.ToString());
        Assert.DoesNotContain(pendingSuggestions, s => s.SuggestedValue == "Old AI Name");
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == newTagId.ToString());
        Assert.Contains(pendingSuggestions, s => s.SuggestedValue == "New Title");
    }

    [Fact]
    public async Task ApplyAsync_RematchedTag_KeepsLink_NoCompositeKeyChurn()
    {
        // Canary for the net-diff: re-matching an already-AI-applied tag must NOT remove-then-re-add the
        // DocumentTag (which would collide on the composite key). The link stays; the suggestion refreshes.
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = tagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = tagId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "", "Test Doc"),
            ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);
        // No InvalidOperationException for a duplicate-tracked DocumentTag.
        await _db.SaveChangesAsync(ct);

        var links = _db.DocumentTags.Where(dt => dt.DocumentId == _documentId).ToList();
        Assert.Single(links);
        Assert.Equal(tagId, links[0].TagId);

        var pendingTagSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Tags
                && s.Status == DocumentSuggestionStatus.Pending)
            .ToList();
        Assert.Single(pendingTagSuggestions);
    }

    [Fact]
    public async Task ApplyAsync_ScopedToTags_LeavesPendingNameSuggestionUntouched()
    {
        // Headline product rule: refreshing one field must not clear another field's pending suggestions.
        var ct = TestContext.Current.CancellationToken;
        var oldTagId = Guid.NewGuid();
        var newTagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(oldTagId, "OldTag", "OldTag");
        SeedTag(newTagId, "NewTag", "NewTag");

        _db.DocumentTags.Add(new DocumentTag { DocumentId = _documentId, TagId = oldTagId });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Tags,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = oldTagId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = DocumentSuggestionField.Name,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = "Old AI Name",
            OriginalValue = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([newTagId], [], "new summary", "New Title"),
            ClassificationScope.Tags, actorUserId: null, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        // Tags: old suggestion cleared, new one applied.
        var tagSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Tags
                && s.Status == DocumentSuggestionStatus.Pending)
            .Select(s => s.SuggestedValue)
            .ToList();
        Assert.DoesNotContain(oldTagId.ToString(), tagSuggestions);
        Assert.Contains(newTagId.ToString(), tagSuggestions);

        // Name: out of scope, so its pending suggestion survives untouched.
        var nameSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Name)
            .ToList();
        Assert.Single(nameSuggestions);
        Assert.Equal(DocumentSuggestionStatus.Pending, nameSuggestions[0].Status);
        Assert.Equal("Old AI Name", nameSuggestions[0].SuggestedValue);
    }

    [Fact]
    public async Task ApplyAsync_ScopedToSummary_StampsMetadata_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");
        var document = LoadDocument();
        var userId = Guid.NewGuid();

        await _sut.ApplyAsync(document, new ClassificationResult([], [], "The generated summary", "Test Doc"),
            ClassificationScope.Summary, actorUserId: userId, stampSummaryMetadata: true, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("The generated summary", doc!.Summary);
        // Writing a summary on the existing-doc path stamps modified-metadata.
        Assert.Equal(userId, doc.UpdatedByUserId);

        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == _documentId).ToList();
        Assert.Empty(suggestions);
    }

    [Fact]
    public async Task ApplyAsync_SummaryWithoutStamp_WritesSummary_LeavesMetadataClean()
    {
        // Creation path: the summary is part of the initial state, so no modified-metadata stamp —
        // the document reads as created-only.
        var ct = TestContext.Current.CancellationToken;

        SeedDocumentWithVersion("some text");
        var document = LoadDocument();
        var userId = Guid.NewGuid();

        await _sut.ApplyAsync(document, new ClassificationResult([], [], "The generated summary", "Test Doc"),
            ClassificationScope.Summary, actorUserId: userId, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("The generated summary", doc!.Summary);
        Assert.Null(doc.UpdatedByUserId);
    }

    [Fact]
    public async Task ApplyAsync_ScopeWithoutName_LeavesDocumentNameUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();

        SeedDocumentWithVersion("some text");
        SeedTag(tagId, "Finance", "Finance");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([tagId], [], "summary", "Should Not Apply"),
            ClassificationScope.Tags | ClassificationScope.Parties, actorUserId: null,
            stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        var doc = await _db.Documents.FindAsync([_documentId], ct);
        Assert.Equal("Test Doc", doc!.Name);

        var nameSuggestions = _db.DocumentSuggestions
            .Where(s => s.DocumentId == _documentId && s.Field == DocumentSuggestionField.Name)
            .ToList();
        Assert.Empty(nameSuggestions);
    }

    // ---- ApplyAsync: dates (overwrite like Name; the original rides on the pending suggestion) -------

    private static readonly DateOnly Jan1 = new(2025, 1, 1);
    private static readonly DateOnly Mar15 = new(2025, 3, 15);
    private static readonly DateOnly Jun30 = new(2025, 6, 30);

    private Task ApplyDocumentDateAsync(Document document, DateOnly? documentDate, CancellationToken ct) =>
        _sut.ApplyAsync(document, new ClassificationResult([], [], "", "Test Doc", DocumentDate: documentDate),
            ClassificationScope.DocumentDate, actorUserId: null, stampSummaryMetadata: false, ct);

    private void SeedDocumentDate(DateOnly? date)
    {
        var doc = _db.Documents.First(d => d.Id == _documentId);
        doc.DocumentDate = date;
        _db.SaveChanges();
    }

    private void SeedPendingSuggestion(DocumentSuggestionField field, string suggested, string? original)
    {
        _db.DocumentSuggestions.Add(new DocumentSuggestion
        {
            DocumentId = _documentId,
            Field = field,
            Status = DocumentSuggestionStatus.Pending,
            SuggestedValue = suggested,
            OriginalValue = original,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }

    private List<DocumentSuggestion> PendingSuggestions(DocumentSuggestionField field) => _db.DocumentSuggestions
        .Where(s => s.DocumentId == _documentId && s.Field == field && s.Status == DocumentSuggestionStatus.Pending)
        .ToList();

    [Fact]
    public async Task ApplyAsync_DocumentDate_EmptyDate_IsApplied_SuggestionHasNullOriginal()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, Mar15, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Mar15, document.DocumentDate);
        var suggestion = Assert.Single(PendingSuggestions(DocumentSuggestionField.DocumentDate));
        Assert.Equal("2025-03-15", suggestion.SuggestedValue);
        Assert.Null(suggestion.OriginalValue);
    }

    [Fact]
    public async Task ApplyAsync_DocumentDate_ExistingDate_IsOverwritten_SuggestionHoldsOriginal()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Jan1);
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, Mar15, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Mar15, document.DocumentDate);
        var suggestion = Assert.Single(PendingSuggestions(DocumentSuggestionField.DocumentDate));
        Assert.Equal("2025-03-15", suggestion.SuggestedValue);
        Assert.Equal("2025-01-01", suggestion.OriginalValue);
    }

    [Fact]
    public async Task ApplyAsync_ExpiryDate_IsApplied_ToItsOwnField()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        var document = LoadDocument();

        await _sut.ApplyAsync(document, new ClassificationResult([], [], "", "Test Doc", ExpiryDate: Jun30),
            ClassificationScope.ExpiryDate, actorUserId: null, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Jun30, document.ExpiryDate);
        Assert.Null(document.DocumentDate);
        var suggestion = Assert.Single(PendingSuggestions(DocumentSuggestionField.ExpiryDate));
        Assert.Equal("2025-06-30", suggestion.SuggestedValue);
        Assert.Empty(PendingSuggestions(DocumentSuggestionField.DocumentDate));
    }

    [Fact]
    public async Task ApplyAsync_DocumentDate_NullAnswer_KeepsExistingDate_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Jan1);
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, null, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Jan1, document.DocumentDate);
        Assert.Empty(PendingSuggestions(DocumentSuggestionField.DocumentDate));
    }

    [Fact]
    public async Task ApplyAsync_DocumentDate_EqualDate_NoSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Jan1);
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, Jan1, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Jan1, document.DocumentDate);
        Assert.Empty(PendingSuggestions(DocumentSuggestionField.DocumentDate));
    }

    [Fact]
    public async Task ApplyAsync_DocumentDate_Reclassify_RecoversOriginal_FromPendingSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Mar15); // the earlier AI value
        SeedPendingSuggestion(DocumentSuggestionField.DocumentDate, "2025-03-15", "2025-01-01");
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, Jun30, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Jun30, document.DocumentDate);
        var suggestion = Assert.Single(PendingSuggestions(DocumentSuggestionField.DocumentDate));
        Assert.Equal("2025-06-30", suggestion.SuggestedValue);
        Assert.Equal("2025-01-01", suggestion.OriginalValue); // the true original, not the earlier AI value
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-03-15")] // equal to the earlier AI value — but the original is empty, so not "unchanged"
    public async Task ApplyAsync_DocumentDate_Reclassify_EmptyOriginal_IsRecovered(string? answer)
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Mar15);
        SeedPendingSuggestion(DocumentSuggestionField.DocumentDate, "2025-03-15", original: null);
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, SuggestionDate.Parse(answer), ct);
        await _db.SaveChangesAsync(ct);

        if (answer is null)
        {
            // A null answer restores the empty original and leaves nothing to review.
            Assert.Null(document.DocumentDate);
            Assert.Empty(PendingSuggestions(DocumentSuggestionField.DocumentDate));
        }
        else
        {
            // The same AI date against an empty original is still a change: re-applied, with a fresh suggestion.
            Assert.Equal(Mar15, document.DocumentDate);
            var suggestion = Assert.Single(PendingSuggestions(DocumentSuggestionField.DocumentDate));
            Assert.Null(suggestion.OriginalValue);
        }
    }

    [Fact]
    public async Task ApplyAsync_DocumentDate_Reclassify_EqualToOriginal_RevertsAndClearsSuggestion()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedDocumentDate(Mar15);
        SeedPendingSuggestion(DocumentSuggestionField.DocumentDate, "2025-03-15", "2025-01-01");
        var document = LoadDocument();

        await ApplyDocumentDateAsync(document, Jan1, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(Jan1, document.DocumentDate);
        Assert.Empty(PendingSuggestions(DocumentSuggestionField.DocumentDate));
    }

    [Fact]
    public async Task ApplyAsync_ScopedToDocumentDate_LeavesPendingNameAndExpirySuggestionsUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        SeedDocumentWithVersion("some text");
        SeedPendingSuggestion(DocumentSuggestionField.Name, "Old AI Name", "Test Doc");
        SeedPendingSuggestion(DocumentSuggestionField.ExpiryDate, "2030-01-01", null);
        var document = LoadDocument();

        await _sut.ApplyAsync(document,
            new ClassificationResult([], [], "", "New Title", DocumentDate: Mar15, ExpiryDate: Jun30),
            ClassificationScope.DocumentDate, actorUserId: null, stampSummaryMetadata: false, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("Test Doc", document.Name);
        Assert.Null(document.ExpiryDate);
        Assert.Equal("Old AI Name", Assert.Single(PendingSuggestions(DocumentSuggestionField.Name)).SuggestedValue);
        Assert.Equal("2030-01-01", Assert.Single(PendingSuggestions(DocumentSuggestionField.ExpiryDate)).SuggestedValue);
        Assert.Single(PendingSuggestions(DocumentSuggestionField.DocumentDate));
    }

    // ---- IsAutoClassifyEnabledAsync ---------------------------------------------------------------

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsTrue_WhenEnabled()
    {
        var ct = TestContext.Current.CancellationToken;

        var settings = new DocumentSettings { AutoClassifyOnUpload = true };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsFalse_WhenDisabled()
    {
        var ct = TestContext.Current.CancellationToken;

        var settings = new DocumentSettings { AutoClassifyOnUpload = false };
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = JsonSerializer.Serialize(settings, FieldChange.JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.False(result);
    }

    [Fact]
    public async Task IsAutoClassifyEnabledAsync_ReturnsFalse_WhenNoSettings()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.IsAutoClassifyEnabledAsync(ct);

        Assert.False(result);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private Document LoadDocument() => _db.Documents
        .Include(d => d.DocumentTags).ThenInclude(dt => dt.Tag)
        .Include(d => d.DocumentParties)
        .Include(d => d.Suggestions)
        .First(d => d.Id == _documentId);

    private void SeedDocumentWithVersion(string? extractedText, string filePath = "test/file.pdf", long fileSize = 100)
    {
        _db.Documents.Add(new Document
        {
            Id = _documentId,
            Name = "Test Doc",
            CreatedAt = DateTimeOffset.UtcNow
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = Guid.NewGuid(),
            DocumentId = _documentId,
            VersionNumber = 1,
            ExtractedText = extractedText,
            ContentType = "application/pdf",
            FileSize = fileSize,
            FilePath = filePath,
            FileHash = "abc123",
            FileExtension = ".pdf"
        });
        _db.SaveChanges();
    }

    private void SeedTag(Guid id, string name, string path)
    {
        _db.Tags.Add(new Tag
        {
            Id = id,
            Name = name,
            Path = path,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }

    private void SeedParty(Guid id, string name)
    {
        _db.Parties.Add(new Party
        {
            Id = id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        _db.SaveChanges();
    }
}
