namespace Homestead.WebApi.Infrastructure.AiClassification;

public static class AiClassificationServiceExtensions
{
    public static IServiceCollection AddAiClassification(this IServiceCollection services)
    {
        services.AddSingleton<IAiClientFactory, AiClientFactory>();
        services.AddScoped<IAiProviderResolver, AiProviderResolver>();
        services.AddScoped<IAiClassificationService, AiClassificationService>();

        return services;
    }
}
