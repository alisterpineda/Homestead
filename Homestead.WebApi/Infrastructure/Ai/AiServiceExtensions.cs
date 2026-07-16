namespace Homestead.WebApi.Infrastructure.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // One registration per configured instance, enumerable rather than keyed: MS DI cannot enumerate
        // keyed registrations, and enumeration is what feeds the admin status page — config stays the
        // single source of truth for what providers exist. An instance with an unusable Type still
        // registers (as unavailable-with-reason) so one typo cannot take the app down; the resolver and
        // status page surface the reason instead.
        foreach (var (key, options) in AiProviderConfiguration.ReadProviders(configuration))
            services.AddSingleton<IAiProvider>(new AiProvider(key, options));

        var defaultProviderKey = AiProviderConfiguration.ReadDefaultProviderKey(configuration);
        services.AddSingleton<IAiProviderResolver>(sp =>
            new AiProviderResolver(sp.GetServices<IAiProvider>(), defaultProviderKey));

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
