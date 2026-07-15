using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

// Shared scaffolding for the job-handler suites, which care about what a handler does with extracted
// text rather than about engines. Keeps the engine-registry shape (key lookup, provenance) in one place.
public static class TestTextExtraction
{
    public const string DefaultKey = OcrEngineOptions.BuiltInTesseractKey;

    public static TextExtractionMetadata Metadata(string engineKey = DefaultKey) =>
        new TesseractTextExtractionMetadata
        {
            EngineKey = engineKey,
            ExtractedAt = DateTimeOffset.UtcNow,
            Language = "eng",
            Dpi = 300,
            Psm = 3,
            Oem = 3
        };

    public static TextExtractionOutput Output(string? text, string engineKey = DefaultKey) =>
        new(text, Metadata(engineKey));

    // A configured engine stub that TextExtractionHelper can resolve by key.
    public static ITextExtractionEngine Engine(string key = DefaultKey)
    {
        var engine = Substitute.For<ITextExtractionEngine>();
        engine.Key.Returns(key);
        engine.IsConfigured.Returns(true);
        engine.ConfigurationError.Returns((string?)null);
        engine.ExtractTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Output(null, key));
        return engine;
    }

    // An unavailable engine — configured as an instance, but not usable. What an explicit pick must be
    // rejected for, and what the default path falls back from.
    public static ITextExtractionEngine UnavailableEngine(
        string key = "azure-layout", string error = "Endpoint is required.")
    {
        var engine = Substitute.For<ITextExtractionEngine>();
        engine.Key.Returns(key);
        engine.Type.Returns(TextExtractionEngineType.AzureDocumentIntelligence);
        engine.IsConfigured.Returns(false);
        engine.ConfigurationError.Returns(error);
        return engine;
    }

    // The shape most handler tests want: a working built-in plus an unavailable Azure instance.
    public static List<ITextExtractionEngine> Engines() => [Engine(), UnavailableEngine()];

    public static TextExtractionHelper Helper(params ITextExtractionEngine[] engines) =>
        new(engines, Substitute.For<ILogger<TextExtractionHelper>>());

    // A resolver that always answers with the built-in key — the default path, with no household setting.
    public static ITextExtractionEngineResolver Resolver(string key = DefaultKey)
    {
        var resolver = Substitute.For<ITextExtractionEngineResolver>();
        resolver.ResolveDefaultKeyAsync(Arg.Any<CancellationToken>()).Returns(key);
        resolver.ResolveKeyAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(key);
        return resolver;
    }
}
