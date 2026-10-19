using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ClassificationInputSelectorTests : IDisposable
{
    private const string Pdf = ClassificationInputSelector.PdfContentType;
    private const long Size = 100_000;

    private static readonly AiInputSettings Defaults = TestAiProviders.Settings();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"input-selector-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static ClassificationInputMode Select(
        AiInputSettings? settings = null, ClassificationScope scope = ClassificationScope.All,
        string contentType = Pdf, long fileSize = Size, int? pageCount = 1, bool isEncrypted = false) =>
        ClassificationInputSelector.Select(settings ?? Defaults, scope, contentType, fileSize, pageCount, isEncrypted);

    [Fact]
    public void AllGatesPass_SendsThePdf() =>
        Assert.Equal(ClassificationInputMode.Pdf, Select());

    [Fact]
    public void ExactlyAtBothCaps_StillSendsThePdf() =>
        Assert.Equal(ClassificationInputMode.Pdf, Select(
            fileSize: Defaults.PdfInputMaxBytes, pageCount: Defaults.PdfInputMaxPages));

    // Each failed gate drops to the text + render path (image input on, a scope that wants more than text).
    public static TheoryData<string, ClassificationInputMode> FailedPdfGates => new()
    {
        { "PdfInput off", Select(settings: TestAiProviders.Settings(pdfInput: false)) },
        { "over the page cap", Select(pageCount: Defaults.PdfInputMaxPages + 1) },
        { "over the byte cap", Select(fileSize: Defaults.PdfInputMaxBytes + 1) },
        { "empty file", Select(fileSize: 0) },
        { "encrypted", Select(isEncrypted: true) },
        { "unknown page count", Select(pageCount: null) },
        { "zero pages", Select(pageCount: 0) },
        { "not a PDF", Select(contentType: "image/png") },
    };

    [Theory]
    [MemberData(nameof(FailedPdfGates))]
    public void AFailedPdfGate_FallsToTextAndImage(string gate, ClassificationInputMode mode) =>
        Assert.True(mode == ClassificationInputMode.TextAndImage, $"{gate}: {mode}");

    // A tags-only refresh never sends the PDF nor a render — the same cost rule as the image.
    [Fact]
    public void TagsOnlyScope_IsTextOnly() =>
        Assert.Equal(ClassificationInputMode.Text, Select(scope: ClassificationScope.Tags));

    [Fact]
    public void ImageInputOff_WhenThePdfDoesNotQualify_IsTextOnly() =>
        Assert.Equal(ClassificationInputMode.Text, Select(
            settings: TestAiProviders.Settings(imageInput: false), contentType: "image/png"));

    // The image switch governs only the render: a qualifying PDF is still sent with image input off.
    [Fact]
    public void ImageInputOff_QualifyingPdf_StillSendsThePdf() =>
        Assert.Equal(ClassificationInputMode.Pdf, Select(settings: TestAiProviders.Settings(imageInput: false)));

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("APPLICATION/PDF")]
    [InlineData("Application/Pdf")]
    public void ContentTypeMatch_IsCaseInsensitive(string contentType)
    {
        Assert.True(ClassificationInputSelector.IsPdfCandidate(Defaults, ClassificationScope.All, contentType, Size));
        Assert.Equal(ClassificationInputMode.Pdf, Select(contentType: contentType));
    }

    [Fact]
    public void IsPdfCandidate_ChecksOnlyTheCheapGates()
    {
        Assert.True(ClassificationInputSelector.IsPdfCandidate(Defaults, ClassificationScope.Name, Pdf, Size));
        Assert.False(ClassificationInputSelector.IsPdfCandidate(Defaults, ClassificationScope.Tags, Pdf, Size));
        Assert.False(ClassificationInputSelector.IsPdfCandidate(
            Defaults, ClassificationScope.All, Pdf, Defaults.PdfInputMaxBytes + 1));
        Assert.False(ClassificationInputSelector.IsPdfCandidate(
            TestAiProviders.Settings(pdfInput: false), ClassificationScope.All, Pdf, Size));
    }

    [Fact]
    public void ReadPdfFacts_NotAPdf_IsNull()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "garbage.pdf");
        File.WriteAllBytes(path, "this is not a pdf at all"u8.ToArray());

        Assert.Null(ClassificationInputSelector.ReadPdfFacts(path));
    }

    [Fact]
    public void ReadPdfFacts_MissingFile_IsNull() =>
        Assert.Null(ClassificationInputSelector.ReadPdfFacts(Path.Combine(_dir, "missing.pdf")));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ReadPdfFacts_RealPdf_ReportsPageCount_NotEncrypted(int pages)
    {
        var path = Path.Combine(_dir, "real.pdf");
        TestPdfs.Write(path, pages);

        Assert.Equal((pages, false), ClassificationInputSelector.ReadPdfFacts(path));
    }
}
