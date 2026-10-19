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
    // Ai:Providers shape) gets one warning at startup rather than only a line on the admin page. Running AI that
    // uploads PDFs says so once: documents leave the request and sit, briefly, in the key's workspace storage.
    public static void LogAiAvailability(this IServiceProvider services)
    {
        var availability = services.GetRequiredService<IAiProvider>().Availability;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AiServiceExtensions));
        if (availability is { IsPresent: true, IsConfigured: false })
            logger.LogWarning("AI is configured but unavailable: {Reason}", availability.Reason);
        else if (availability.IsConfigured && services.GetRequiredService<AiInputSettings>().PdfInput
                 && services.GetRequiredService<AiOptions>().PdfUpload)
            logger.LogInformation(
                "AI PDF input uploads each PDF to the Claude API's Files API for the call: stored in the API key's workspace (visible to its other keys, not covered by zero data retention), deleted after the call and expiring within {ExpirySeconds} s. Set Ai:PdfUpload=false to send PDFs inline",
                AnthropicAiProvider.UploadExpirySeconds);
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
