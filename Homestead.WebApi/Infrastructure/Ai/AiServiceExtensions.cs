namespace Homestead.WebApi.Infrastructure.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services)
    {
        services.AddSingleton<IAiClientFactory, AiClientFactory>();

        return services;
    }
}
