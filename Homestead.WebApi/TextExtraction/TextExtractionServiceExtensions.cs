namespace Homestead.WebApi.TextExtraction;

public static class TextExtractionServiceExtensions
{
    public static IServiceCollection AddTextExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.AddSingleton<TesseractRunner>();
        services.AddSingleton<ITextExtractor, TesseractTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();
        services.AddScoped<ITextExtractionEngineResolver, TextExtractionEngineResolver>();
        services.AddScoped<TextExtractionHelper>();

        return services;
    }
}
