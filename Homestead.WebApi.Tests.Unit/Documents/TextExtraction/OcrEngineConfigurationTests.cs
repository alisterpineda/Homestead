using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

public class OcrEngineConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Existing deployments and tests define no Ocr:Engines at all and must keep working untouched.
    [Fact]
    public void ReadEngines_NoConfig_SeedsBuiltInTesseractWithDefaults()
    {
        var engines = OcrEngineConfiguration.ReadEngines(Config([]));

        var (key, options) = Assert.Single(engines);
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, key);
        Assert.Equal(TextExtractionEngineType.Tesseract, options.ResolveType(key));
        Assert.Equal("tessdata", options.TessDataPath);
        // Empty means English — see OcrEngineOptions.Languages on why it is not pre-seeded.
        Assert.Empty(options.Languages);
        Assert.Equal(300, options.Dpi);
        Assert.Null(OcrEngineConfiguration.Validate(key, options));
    }

    [Fact]
    public void ReadEngines_NamedInstances_AreAllRead()
    {
        var engines = OcrEngineConfiguration.ReadEngines(Config(new()
        {
            ["Ocr:Engines:tesseract:Type"] = "Tesseract",
            ["Ocr:Engines:tesseract:TessDataPath"] = "/usr/share/tessdata",
            ["Ocr:Engines:azure-layout:Type"] = "AzureDocumentIntelligence",
            ["Ocr:Engines:azure-layout:Endpoint"] = "https://x.cognitiveservices.azure.com",
            ["Ocr:Engines:azure-layout:ApiKey"] = "k",
            ["Ocr:Engines:azure-layout:ModelId"] = "prebuilt-layout",
            ["Ocr:Engines:azure-read:Type"] = "AzureDocumentIntelligence",
            ["Ocr:Engines:azure-read:Endpoint"] = "https://x.cognitiveservices.azure.com",
            ["Ocr:Engines:azure-read:ApiKey"] = "k",
            ["Ocr:Engines:azure-read:ModelId"] = "prebuilt-read",
        }));

        var byKey = engines.ToDictionary(e => e.Key, e => e.Options);

        Assert.Equal(["azure-layout", "azure-read", "tesseract"], byKey.Keys.Order());
        Assert.Equal("/usr/share/tessdata", byKey["tesseract"].TessDataPath);
        // One Azure resource, two meaningfully different engines — this is why instances are named.
        Assert.Equal("prebuilt-layout", byKey["azure-layout"].ModelId);
        Assert.Equal("prebuilt-read", byKey["azure-read"].ModelId);
    }

    // The built-in key is the terminus of the fallback chain, so it exists even when config names only
    // other engines.
    [Fact]
    public void ReadEngines_ConfigWithoutTesseract_StillSeedsBuiltIn()
    {
        var engines = OcrEngineConfiguration.ReadEngines(Config(new()
        {
            ["Ocr:Engines:azure-layout:Type"] = "AzureDocumentIntelligence",
        }));

        Assert.Contains(engines, e => e.Key == OcrEngineOptions.BuiltInTesseractKey);
    }

    [Fact]
    public void ReadEngines_EnvVarStyleOverride_BindsOntoTheNamedInstance()
    {
        // What Ocr__Engines__tesseract__TessDataPath arrives as.
        var engines = OcrEngineConfiguration.ReadEngines(Config(new()
        {
            ["Ocr:Engines:tesseract:Type"] = "Tesseract",
            ["Ocr:Engines:tesseract:TessDataPath"] = "/usr/share/tessdata",
        }));

        var (_, options) = Assert.Single(engines);
        Assert.Equal("/usr/share/tessdata", options.TessDataPath);
    }

    [Theory]
    [InlineData(null, "k", "Endpoint is required.")]
    [InlineData("not-a-uri", "k", "Endpoint must be an absolute URI.")]
    [InlineData("https://x.cognitiveservices.azure.com", null, "ApiKey is required.")]
    public void Validate_IncompleteAzure_ReportsReason(string? endpoint, string? apiKey, string expected)
    {
        var error = OcrEngineConfiguration.Validate("some-engine", new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            Endpoint = endpoint,
            ApiKey = apiKey
        });

        Assert.Equal(expected, error);
    }

    // Only prebuilt-layout supports Markdown. Caught in config so it shows in the picker as
    // unavailable-with-reason rather than as a 400 from a background job.
    [Fact]
    public void Validate_PrebuiltReadWithMarkdown_ReportsUnsupportedCombination()
    {
        var error = OcrEngineConfiguration.Validate("some-engine", new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            Endpoint = "https://x.cognitiveservices.azure.com",
            ApiKey = "k",
            ModelId = AzureModels.PrebuiltRead,
            OutputContentFormat = AzureOutputContentFormat.Markdown
        });

        Assert.NotNull(error);
        Assert.Contains("Markdown", error);
        Assert.Contains(AzureModels.PrebuiltRead, error);
    }

    [Fact]
    public void Validate_PrebuiltLayoutWithMarkdown_IsValid()
    {
        var error = OcrEngineConfiguration.Validate("some-engine", new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            Endpoint = "https://x.cognitiveservices.azure.com",
            ApiKey = "k",
            ModelId = AzureModels.PrebuiltLayout,
            OutputContentFormat = AzureOutputContentFormat.Markdown
        });

        Assert.Null(error);
    }

    // The accept-set is exactly one id, not everything starting with it — an id that merely begins with
    // prebuilt-layout is a typo, and Azure has no such model.
    [Fact]
    public void Validate_PrebuiltLayoutPrefixedTypoWithMarkdown_ReportsUnsupportedCombination()
    {
        var error = OcrEngineConfiguration.Validate("some-engine", new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            Endpoint = "https://x.cognitiveservices.azure.com",
            ApiKey = "k",
            ModelId = "prebuilt-layout-v2",
            OutputContentFormat = AzureOutputContentFormat.Markdown
        });

        Assert.NotNull(error);
        Assert.Contains("Markdown", error);
        Assert.Contains("prebuilt-layout-v2", error);
    }

    // Only an explicit Markdown pairing is caught. Nothing here knows Azure's model list, so a typo on
    // its own stays "configured" and fails at the Azure call — the limit of what config can validate.
    [Fact]
    public void Validate_PrebuiltLayoutPrefixedTypoWithoutMarkdown_IsNotCaughtHere()
    {
        var error = OcrEngineConfiguration.Validate("some-engine", new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            Endpoint = "https://x.cognitiveservices.azure.com",
            ApiKey = "k",
            ModelId = "prebuilt-layout-v2",
            OutputContentFormat = null
        });

        Assert.Null(error);
    }

    // The signature accepts null and the guard is implicit in string.Equals's static overload — the
    // instance form (modelId.Equals(...)) compiles identically and NREs.
    [Fact]
    public void SupportsMarkdown_NullModelId_IsFalse() => Assert.False(AzureModels.SupportsMarkdown(null));

    [Theory]
    [InlineData(AzureModels.PrebuiltLayout, AzureOutputContentFormat.Markdown)]
    [InlineData(AzureModels.PrebuiltRead, AzureOutputContentFormat.Text)]
    // An unrecognized id falls back to Text rather than being assumed layout-like.
    [InlineData("prebuilt-layout-v2", AzureOutputContentFormat.Text)]
    public void ResolveOutputContentFormat_Unset_DefaultsByModelId(
        string modelId, AzureOutputContentFormat expected)
    {
        var options = new OcrEngineOptions
        {
            Type = TextExtractionEngineType.AzureDocumentIntelligence,
            ModelId = modelId,
            OutputContentFormat = null
        };

        Assert.Equal(expected, options.ResolveOutputContentFormat());
    }

    // The config binder ADDS to an existing collection instead of replacing it. A pre-seeded [Eng]
    // default would therefore make configured languages append to it: ["Fra"] would bind to [Eng, Fra]
    // and run as "eng+fra", silently demoting the language the operator actually asked for.
    [Fact]
    public void ReadEngines_ConfiguredLanguages_ReplaceRatherThanAppendToTheDefault()
    {
        var engines = OcrEngineConfiguration.ReadEngines(Config(new()
        {
            ["Ocr:Engines:tesseract:Type"] = "Tesseract",
            ["Ocr:Engines:tesseract:Languages:0"] = "Fra",
        }));

        var (_, options) = Assert.Single(engines);
        Assert.Equal([TesseractLanguage.Fra], options.Languages);
    }

    // An empty language list is legal — it is the default, and it means English.
    [Fact]
    public void Validate_TesseractWithNoLanguages_IsValid()
    {
        var options = new OcrEngineOptions { Type = TextExtractionEngineType.Tesseract, Languages = [] };

        Assert.Null(OcrEngineConfiguration.Validate("some-engine", options));
    }

    // A non-nullable Type would bind an omitted Type to default(0) = Tesseract, so an operator who sets
    // an Azure endpoint + key but forgets Type would get a fully-available engine silently running local
    // Tesseract under the name "azure-layout" — the exact silent substitution this design refuses.
    [Fact]
    public void Validate_EngineWithNoType_RefusesRatherThanDefaultingToTesseract()
    {
        var options = new OcrEngineOptions
        {
            Endpoint = "https://x.cognitiveservices.azure.com",
            ApiKey = "k"
        };

        Assert.Null(options.ResolveType("azure-layout"));
        var error = OcrEngineConfiguration.Validate("azure-layout", options);

        Assert.NotNull(error);
        Assert.Contains("Type is required", error);
    }

    // The one exception: the built-in instance's type is implied by its name, so config that only
    // overrides its TessDataPath (as the Docker image does) need not restate it.
    [Fact]
    public void Validate_BuiltInKeyWithNoType_IsTesseractAndValid()
    {
        var options = new OcrEngineOptions { TessDataPath = "/usr/share/tessdata" };

        Assert.Equal(TextExtractionEngineType.Tesseract,
            options.ResolveType(OcrEngineOptions.BuiltInTesseractKey));
        Assert.Null(OcrEngineConfiguration.Validate(OcrEngineOptions.BuiltInTesseractKey, options));
    }

    [Fact]
    public void ReadEngines_TessDataPathOnlyEnvVarForBuiltIn_StillBindsAsTesseract()
    {
        // Exactly what the Docker image sets: Ocr__Engines__tesseract__TessDataPath and nothing else.
        var engines = OcrEngineConfiguration.ReadEngines(Config(new()
        {
            ["Ocr:Engines:tesseract:TessDataPath"] = "/usr/share/tessdata",
        }));

        var (key, options) = Assert.Single(engines);
        Assert.Equal("/usr/share/tessdata", options.TessDataPath);
        Assert.Null(OcrEngineConfiguration.Validate(key, options));
    }

    [Fact]
    public void ReadDefaultEngineKey_Blank_IsNull_SetValue_IsTrimmed()
    {
        Assert.Null(OcrEngineConfiguration.ReadDefaultEngineKey(Config([])));
        Assert.Null(OcrEngineConfiguration.ReadDefaultEngineKey(Config(new()
        {
            ["Ocr:DefaultEngine"] = "   ",
        })));
        Assert.Equal("azure-layout", OcrEngineConfiguration.ReadDefaultEngineKey(Config(new()
        {
            ["Ocr:DefaultEngine"] = " azure-layout ",
        })));
    }

    // The key side of the resolution chain: configured default -> built-in. No sole-instance arm: the built-in
    // engine always exists, so "unset" already has an answer. A
    // dangling default is returned VERBATIM — the fallback with provenance is TextExtractionHelper's job.
    [Fact]
    public void ResolveDefaultKey_Null_IsBuiltIn_Configured_ReturnedVerbatim()
    {
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, OcrEngineConfiguration.ResolveDefaultKey(null));
        Assert.Equal("azure-layout", OcrEngineConfiguration.ResolveDefaultKey("azure-layout"));
        Assert.Equal("typo", OcrEngineConfiguration.ResolveDefaultKey("typo"));
    }
}
