namespace Homestead.WebApi.Infrastructure.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddSingleton<IAiClientFactory, AiClientFactory>();

        return services;
    }

    // The dedicated AI page-render path (renderer + cache service), mirroring AddThumbnails. Singletons:
    // both are stateless over the file system, like the thumbnail generators.
    public static IServiceCollection AddAiPageRendering(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentPageRenderer, DocumentPageRenderer>();
        services.AddSingleton<AiPageRenderService>();

        return services;
    }
}
