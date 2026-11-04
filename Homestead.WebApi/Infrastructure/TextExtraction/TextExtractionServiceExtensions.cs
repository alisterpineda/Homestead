using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Processing;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public static class TextExtractionServiceExtensions
{
    public static IServiceCollection AddTextExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        // Stateless and shared by every Tesseract instance — it takes its per-run options as a parameter.
        services.AddSingleton<ITesseractRunner, TesseractRunner>();
        services.AddSingleton<IPdfTextRunner, PdfTextRunner>();

        // One registration per configured instance, enumerable rather than keyed: MS DI cannot enumerate
        // keyed registrations, and enumeration is what feeds the picker. That keeps config the single
        // source of truth for what engines exist — the old keyed design paid for a hand-maintained second
        // list of engines that could drift from what was actually registered.
        foreach (var (key, options) in OcrEngineConfiguration.ReadEngines(configuration))
            services.AddSingleton<ITextExtractionEngine>(sp => BuildEngine(sp, key, options));

        // Singleton over static config (mirrors AddAiServices' resolver): the default is Ocr:DefaultEngine,
        // not a settings row, so nothing here needs a DbContext.
        var defaultEngineKey = OcrEngineConfiguration.ReadDefaultEngineKey(configuration);
        services.AddSingleton<ITextExtractionEngineResolver>(sp =>
            new TextExtractionEngineResolver(sp.GetServices<ITextExtractionEngine>(), defaultEngineKey));

        services.AddScoped<TextExtractionHelper>();

        return services;
    }

    private static ITextExtractionEngine BuildEngine(IServiceProvider sp, string key, OcrEngineOptions options) =>
        options.ResolveType(key) switch
        {
            TextExtractionEngineType.Tesseract => new TesseractTextExtractionEngine(
                key,
                options,
                sp.GetRequiredService<ITesseractRunner>(),
                sp.GetRequiredService<IPdfTextRunner>(),
                sp.GetRequiredService<ILogger<TesseractTextExtractionEngine>>()),

            // The runner is built per engine instance rather than resolved from DI: it holds this
            // instance's endpoint and key, which is what lets its client be a single lazy field.
            // Credentials may be blank here — the engine reports ConfigurationError and refuses to run
            // before the runner is ever touched, so a half-configured instance costs nothing.
            TextExtractionEngineType.AzureDocumentIntelligence => new AzureDocumentIntelligenceTextExtractionEngine(
                key,
                options,
                new AzureDocumentIntelligenceRunner(
                    options.Endpoint ?? string.Empty,
                    options.ApiKey ?? string.Empty,
                    sp.GetRequiredService<ILogger<AzureDocumentIntelligenceRunner>>()),
                sp.GetRequiredService<ILogger<AzureDocumentIntelligenceTextExtractionEngine>>()),

            // Same shape as Azure: a runner per instance holding its endpoint, key, and its own HttpClient
            // (see DoclingRunner on why not IHttpClientFactory). A blank endpoint is fine here — the engine
            // reports ConfigurationError and refuses before the runner is touched.
            TextExtractionEngineType.Docling => new DoclingTextExtractionEngine(
                key,
                options,
                sp.GetRequiredService<IOptions<JobProcessingOptions>>(),
                new DoclingRunner(
                    options.Endpoint ?? string.Empty,
                    options.ApiKey,
                    DoclingRunner.CreateHttpClient(),
                    sp.GetRequiredService<ILogger<DoclingRunner>>(),
                    options.MaxConcurrency),
                sp.GetRequiredService<ILogger<DoclingTextExtractionEngine>>()),

            // An instance with no usable Type still has to REGISTER — refusing here would take the whole
            // app down over one typo'd engine, which is what the no-startup-validator posture exists to
            // avoid. It registers as a Tesseract-shaped instance that reports itself unavailable with the
            // reason, so it shows in the picker as broken and can never actually extract anything.
            _ => new TesseractTextExtractionEngine(
                key,
                options,
                sp.GetRequiredService<ITesseractRunner>(),
                sp.GetRequiredService<IPdfTextRunner>(),
                sp.GetRequiredService<ILogger<TesseractTextExtractionEngine>>())
        };
}
