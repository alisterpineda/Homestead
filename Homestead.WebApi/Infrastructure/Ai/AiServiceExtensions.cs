namespace Homestead.WebApi.Infrastructure.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // One provider over static config. A misconfigured (or absent) section still registers — as
        // unavailable-with-reason — so one typo cannot take the app down; Availability and the status page carry
        // the reason instead.
        var options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        var legacyKeysPresent = AiConfiguration.LegacyKeysPresent(configuration);

        services.AddSingleton(options);
        services.AddSingleton(AiInputSettings.From(options));
        services.AddSingleton<AiTelemetry>();
        services.AddSingleton<IAiProvider>(sp => new AnthropicAiProvider(options, legacyKeysPresent,
            sp.GetRequiredService<ILogger<AnthropicAiProvider>>(), sp.GetRequiredService<AiTelemetry>()));

        return services;
    }

    // Unconfigured AI is a silent no-op, so config that is there but cannot run (a typo, the retired
    // Ai:Providers shape) gets one warning at startup rather than only a line on the admin page.
    public static void LogAiAvailability(this IServiceProvider services)
    {
        var availability = services.GetRequiredService<IAiProvider>().Availability;
        if (availability is { IsPresent: true, IsConfigured: false })
            services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiServiceExtensions))
                .LogWarning("AI is configured but unavailable: {Reason}", availability.Reason);
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
