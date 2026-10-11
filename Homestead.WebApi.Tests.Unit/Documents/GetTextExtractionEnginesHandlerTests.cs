using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Features.TextExtractionEngines.GetTextExtractionEngines;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class GetTextExtractionEnginesHandlerTests
{
    private static GetTextExtractionEnginesHandler Sut(string? defaultKey, params ITextExtractionEngine[] engines) =>
        new(engines, new TextExtractionEngineResolver(engines, defaultKey));

    [Fact]
    public async Task MarksConfiguredDefault()
    {
        var sut = Sut("azure-layout", TestTextExtraction.Engine(), TestTextExtraction.UnavailableEngine());

        var result = await sut.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        var azure = Assert.Single(result, r => r.Key == "azure-layout");
        Assert.True(azure.IsDefault);
        // Default-ness is independent of availability: a misconfigured default still reads as the default.
        Assert.False(azure.IsConfigured);
        Assert.False(Assert.Single(result, r => r.Key == "tesseract").IsDefault);
    }

    [Fact]
    public async Task NoConfig_MarksBuiltInTesseract()
    {
        var sut = Sut(null, TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-read"));

        var result = await sut.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        var tesseract = Assert.Single(result, r => r.IsDefault);
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, tesseract.Key);
    }

    // Mirrors GetAiStatusHandler: a dangling Ocr:DefaultEngine is appended as its own unavailable row so the
    // settings page shows the typo rather than a list with no default.
    [Fact]
    public async Task DanglingDefault_AppendsUnavailableDefaultRow()
    {
        var sut = Sut("azure-typo", TestTextExtraction.Engine());

        var result = await sut.HandleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.False(result[0].IsDefault);
        var dangling = result[1];
        Assert.Equal("azure-typo", dangling.Key);
        Assert.True(dangling.IsDefault);
        Assert.Null(dangling.Type);
        Assert.False(dangling.IsConfigured);
        Assert.Contains("not defined under Ocr:Engines", dangling.ConfigurationError);
    }
}
