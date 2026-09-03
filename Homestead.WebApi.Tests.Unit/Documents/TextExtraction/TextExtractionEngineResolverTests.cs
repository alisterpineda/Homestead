using Homestead.WebApi.Common;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The resolution chain: explicit key -> Ocr:DefaultEngine -> built-in "tesseract". Config-only, so no DB.
public class TextExtractionEngineResolverTests
{
    private static TextExtractionEngineResolver Resolver(string? defaultKey = null, params ITextExtractionEngine[] engines) =>
        new(engines.Length > 0 ? engines : [TestTextExtraction.Engine()], defaultKey);

    [Fact]
    public void ResolveKey_ExplicitConfiguredKey_ReturnsIt()
    {
        var resolver = Resolver(null, TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-read"));

        Assert.Equal("azure-read", resolver.ResolveKey("azure-read"));
    }

    // Casing is normalized to the configured key, so the payload and provenance record what config calls
    // the engine rather than however the caller spelled it.
    [Fact]
    public void ResolveKey_ExplicitKeyDifferentCasing_ReturnsConfiguredCasing()
    {
        var resolver = Resolver(null, TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-layout"));

        Assert.Equal("azure-layout", resolver.ResolveKey("AZURE-Layout"));
    }

    [Fact]
    public void ResolveKey_ExplicitUnknownKey_Throws()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Resolver().ResolveKey("nope"));

        Assert.Contains("nope", ex.Message);
    }

    // The one place the fall-back rule deliberately does NOT apply: silently running Tesseract would
    // hand back exactly the output the caller rejected by picking something else.
    [Fact]
    public void ResolveKey_ExplicitUnavailableKey_ThrowsRatherThanFallingBack()
    {
        var resolver = Resolver(null, TestTextExtraction.Engine(), TestTextExtraction.UnavailableEngine());

        var ex = Assert.Throws<BusinessRuleException>(() => resolver.ResolveKey("azure-layout"));

        Assert.Contains("azure-layout", ex.Message);
        Assert.Contains("Endpoint is required.", ex.Message);
    }

    [Fact]
    public void ResolveKey_NoExplicitKey_UsesConfiguredDefault()
    {
        var resolver = Resolver("azure-layout", TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-layout"));

        Assert.Equal("azure-layout", resolver.ResolveKey(null));
        Assert.Equal("azure-layout", resolver.ResolveKey("  "));
    }

    [Fact]
    public void DefaultKey_NoConfig_ReturnsBuiltIn()
    {
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, Resolver().DefaultKey);
    }

    // A configured default that names no engine is returned verbatim, NOT substituted here. Falling back
    // is TextExtractionHelper's job, because only there can the substitution be recorded in provenance
    // metadata — swapping it silently at enqueue would lose that.
    [Fact]
    public void DefaultKey_DanglingDefault_ReturnedVerbatimForExtractionToFallBack()
    {
        var resolver = Resolver("deleted-engine");

        Assert.Equal("deleted-engine", resolver.DefaultKey);
        Assert.Equal("deleted-engine", resolver.ResolveKey(null));
    }
}
