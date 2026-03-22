using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.TextExtraction.Configurations;

namespace Homestead.WebApi.Tests.Unit.TextExtraction;

public class TesseractLanguageResolverTests
{
    [Fact]
    public void Resolve_NullConfiguration_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve(null);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_EmptyConfiguration_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve("");

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_WhitespaceConfiguration_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve("   ");

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_SingleLanguage_ReturnsLowercase()
    {
        var result = TesseractLanguageResolver.Resolve("""{"languages":["Fra"]}""");

        Assert.Equal("fra", result);
    }

    [Fact]
    public void Resolve_MultipleLanguages_JoinsWithPlus()
    {
        var result = TesseractLanguageResolver.Resolve("""{"languages":["Eng","Fra","Deu"]}""");

        Assert.Equal("eng+fra+deu", result);
    }

    [Fact]
    public void Resolve_InvalidJson_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve("not-valid-json");

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_EmptyLanguagesArray_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve("""{"languages":[]}""");

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_BuiltInDefaultConfiguration_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve(
            BuiltInTextExtractionEngines.TesseractDefaultConfiguration);

        Assert.Equal("eng", result);
    }

    [Fact]
    public void Resolve_AllSupportedLanguages_ReturnsLowercaseJoined()
    {
        var result = TesseractLanguageResolver.Resolve("""{"languages":["Jpn","Kor","Zho"]}""");

        Assert.Equal("jpn+kor+zho", result);
    }

    [Fact]
    public void Resolve_JsonWithMissingLanguagesField_ReturnsEng()
    {
        var result = TesseractLanguageResolver.Resolve("""{}""");

        // TesseractEngineConfig has default [TesseractLanguage.Eng] so deserialization
        // produces a config with Languages = [Eng]
        Assert.Equal("eng", result);
    }
}
