using Homestead.WebApi.Domain.Settings;

namespace Homestead.WebApi.Infrastructure.TextExtraction;

public static class TextExtractionServiceExtensions
{
    public static IServiceCollection AddTextExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        // Tesseract
        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.AddSingleton<ITesseractRunner, TesseractRunner>();
        services.AddKeyedSingleton<ITextExtractionEngine, TesseractTextExtractionEngine>(
            TextExtractionEngineType.Tesseract);

        // Azure Document Intelligence
        services.AddSingleton<IAzureDocumentIntelligenceRunner, AzureDocumentIntelligenceRunner>();
        services.AddKeyedSingleton<ITextExtractionEngine, AzureDocumentIntelligenceTextExtractionEngine>(
            TextExtractionEngineType.AzureDocumentIntelligence);

        // Shared
        services.AddScoped<ITextExtractionEngineResolver, TextExtractionEngineResolver>();
        services.AddScoped<TextExtractionHelper>();

        return services;
    }
}
