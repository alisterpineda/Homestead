using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The resolution chain: explicit key -> household default -> built-in "tesseract".
public class TextExtractionEngineResolverTests : IDisposable
{
    private readonly TestAppDbContext _db = TestAppDbContext.Create();

    public void Dispose() => _db.Dispose();

    private TextExtractionEngineResolver Resolver(params ITextExtractionEngine[] engines) =>
        new(_db, engines.Length > 0 ? engines : [TestTextExtraction.Engine()]);

    private async Task SeedSettingsAsync(string json)
    {
        _db.AppSettings.Add(new AppSetting { Key = AppSetting.DocumentsModule, Value = json });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveKeyAsync_ExplicitConfiguredKey_ReturnsIt()
    {
        var resolver = Resolver(TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-read"));

        var key = await resolver.ResolveKeyAsync("azure-read", TestContext.Current.CancellationToken);

        Assert.Equal("azure-read", key);
    }

    // Casing is normalized to the configured key, so the payload and provenance record what config calls
    // the engine rather than however the caller spelled it.
    [Fact]
    public async Task ResolveKeyAsync_ExplicitKeyDifferentCasing_ReturnsConfiguredCasing()
    {
        var resolver = Resolver(TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-layout"));

        var key = await resolver.ResolveKeyAsync("AZURE-Layout", TestContext.Current.CancellationToken);

        Assert.Equal("azure-layout", key);
    }

    [Fact]
    public async Task ResolveKeyAsync_ExplicitUnknownKey_Throws()
    {
        var resolver = Resolver();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            resolver.ResolveKeyAsync("nope", TestContext.Current.CancellationToken));

        Assert.Contains("nope", ex.Message);
    }

    // The one place the fall-back rule deliberately does NOT apply: silently running Tesseract would
    // hand back exactly the output the caller rejected by picking something else.
    [Fact]
    public async Task ResolveKeyAsync_ExplicitUnavailableKey_ThrowsRatherThanFallingBack()
    {
        var resolver = Resolver(TestTextExtraction.Engine(), TestTextExtraction.UnavailableEngine());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            resolver.ResolveKeyAsync("azure-layout", TestContext.Current.CancellationToken));

        Assert.Contains("azure-layout", ex.Message);
        Assert.Contains("Endpoint is required.", ex.Message);
    }

    [Fact]
    public async Task ResolveKeyAsync_NoExplicitKey_UsesHouseholdDefault()
    {
        await SeedSettingsAsync("""{"defaultEngineKey":"azure-layout"}""");
        var resolver = Resolver(TestTextExtraction.Engine(), TestTextExtraction.Engine("azure-layout"));

        var key = await resolver.ResolveKeyAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("azure-layout", key);
    }

    [Fact]
    public async Task ResolveDefaultKeyAsync_NoSettingsRow_ReturnsBuiltIn()
    {
        var key = await Resolver().ResolveDefaultKeyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, key);
    }

    [Fact]
    public async Task ResolveDefaultKeyAsync_SettingsWithoutEngineKey_ReturnsBuiltIn()
    {
        await SeedSettingsAsync("""{"autoClassifyOnUpload":true}""");

        var key = await Resolver().ResolveDefaultKeyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, key);
    }

    // A household default that config has since dropped is returned verbatim, NOT substituted here.
    // Falling back is TextExtractionHelper's job, because only there can the substitution be recorded in
    // provenance metadata — swapping it silently at enqueue would lose that.
    [Fact]
    public async Task ResolveDefaultKeyAsync_DanglingDefault_ReturnedVerbatimForExtractionToFallBack()
    {
        await SeedSettingsAsync("""{"defaultEngineKey":"deleted-engine"}""");

        var key = await Resolver().ResolveDefaultKeyAsync(TestContext.Current.CancellationToken);

        Assert.Equal("deleted-engine", key);
    }
}
