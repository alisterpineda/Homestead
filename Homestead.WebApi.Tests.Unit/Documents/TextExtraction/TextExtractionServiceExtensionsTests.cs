using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// The real registration path: Ocr:DefaultEngine must reach the resolver through AddTextExtraction, not
// only through the pieces the other suites construct by hand.
public class TextExtractionServiceExtensionsTests
{
    private static ServiceProvider Build(Dictionary<string, string?> config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTextExtraction(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTextExtraction_ReadsDefaultEngineIntoSingletonResolver()
    {
        using var sp = Build(new() { ["Ocr:DefaultEngine"] = " azure-typo " });

        var resolver = sp.GetRequiredService<ITextExtractionEngineResolver>();

        // Trimmed, and returned verbatim even though no such engine is defined.
        Assert.Equal("azure-typo", resolver.DefaultKey);
        Assert.Same(resolver, sp.GetRequiredService<ITextExtractionEngineResolver>());
    }

    [Fact]
    public void AddTextExtraction_NoDefaultEngine_ResolverDefaultsToBuiltIn()
    {
        using var sp = Build([]);

        var resolver = sp.GetRequiredService<ITextExtractionEngineResolver>();

        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, resolver.DefaultKey);
        Assert.Equal(OcrEngineOptions.BuiltInTesseractKey, resolver.ResolveKey(null));
    }

    [Fact]
    public void AddTextExtraction_DoclingInstance_RegistersAsADoclingEngine()
    {
        using var sp = Build(new()
        {
            ["Ocr:Engines:docling:Type"] = "Docling",
            ["Ocr:Engines:docling:Endpoint"] = "http://docling:5001",
            ["Ocr:Engines:docling:JobTimeout"] = "00:20:00",
        });

        var engine = Assert.Single(sp.GetServices<ITextExtractionEngine>(), e => e.Key == "docling");

        Assert.IsType<DoclingTextExtractionEngine>(engine);
        Assert.Equal(TextExtractionEngineType.Docling, engine.Type);
        Assert.True(engine.IsConfigured);
        Assert.Equal(TimeSpan.FromMinutes(20), engine.JobTimeout);
    }
}
