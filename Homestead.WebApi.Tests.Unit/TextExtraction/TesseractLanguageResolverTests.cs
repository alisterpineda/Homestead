using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction;
using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Tests.Unit.TextExtraction;

public class TesseractLanguageResolverTests
{
    [Fact]
    public void Resolve_NullConfiguration_ReturnsEng()
    {
        var context = new TextExtractionContext(TextExtractionEngineType.Tesseract, null);

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_EmptyConfiguration_ReturnsEng()
    {
        var context = new TextExtractionContext(TextExtractionEngineType.Tesseract, "");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_WhitespaceConfiguration_ReturnsEng()
    {
        var context = new TextExtractionContext(TextExtractionEngineType.Tesseract, "   ");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_SingleLanguage_ReturnsLowercase()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            """{"languages":["Fra"]}""");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("fra", result);
    }

    [Fact]
    public void Resolve_MultipleLanguages_JoinsWithPlus()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            """{"languages":["Eng","Fra","Deu"]}""");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng+fra+deu", result);
    }

    [Fact]
    public void Resolve_InvalidJson_ReturnsEng()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            "not-valid-json");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_EmptyLanguagesArray_ReturnsEng()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            """{"languages":[]}""");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_BuiltInDefaultConfiguration_ReturnsEng()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            BuiltInTextExtractionEngines.TesseractDefaultConfiguration);

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_AllSupportedLanguages_ReturnsLowercaseJoined()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            """{"languages":["Jpn","Kor","Zho"]}""");

        var result = TesseractLanguageResolver.Resolve(context);

        Assert.Equal("jpn+kor+zho", result);
    }

    [Fact]
    public void Resolve_JsonWithMissingLanguagesField_ReturnsEng()
    {
        var context = new TextExtractionContext(
            TextExtractionEngineType.Tesseract,
            """{}""");

        var result = TesseractLanguageResolver.Resolve(context);

        // TesseractEngineConfig has default [TesseractLanguage.Eng] so deserialization
        // produces a config with Languages = [Eng]
        Assert.Equal("eng", result);
    }
}
