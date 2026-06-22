namespace Homestead.WebApi.Infrastructure.TextExtraction;

public static class TextExtractionServiceExtensions
{
    public static IServiceCollection AddTextExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TesseractOptions>(configuration.GetSection(TesseractOptions.SectionName));
        services.AddSingleton<ITesseractRunner, TesseractRunner>();
        services.AddSingleton<ITextExtractionEngine, TesseractTextExtractionEngine>();
        services.AddScoped<TextExtractionHelper>();

        return services;
    }
}
