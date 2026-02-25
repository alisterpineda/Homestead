namespace HomeSuiteHome.WebApi.TextExtraction;

public static class TextExtractionServiceExtensions
{
    public static IServiceCollection AddTextExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.AddSingleton<ITextExtractor, TesseractTextExtractor>();

        return services;
    }
}
