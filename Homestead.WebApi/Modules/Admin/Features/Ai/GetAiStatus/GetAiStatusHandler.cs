using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Pure read off static config — no DB access. A query, not a command, so it never opens the UnitOfWork
// transaction. State comes from the provider's Availability; the displayed values from the options and the
// resolved input settings.
public class GetAiStatusHandler(IAiProvider aiProvider, AiOptions options, AiInputSettings settings)
{
    public Task<AiStatusResponse> HandleAsync(CancellationToken ct)
    {
        var availability = aiProvider.Availability;

        return Task.FromResult(new AiStatusResponse(
            IsConfigured: availability.IsConfigured,
            IsPresent: availability.IsPresent,
            ConfigurationError: availability.Reason,
            Model: Blank(options.Model),
            Endpoint: Blank(options.Endpoint),
            HasApiKey: !string.IsNullOrWhiteSpace(options.ApiKey),
            Effort: AiConfiguration.ToWire(settings.Effort),
            RefusalFallback: options.RefusalFallback,
            MaxInputChars: settings.MaxInputChars,
            StatementMaxInputChars: settings.StatementMaxInputChars,
            ImageInput: settings.ImageInput,
            ImageRenderMaxEdge: settings.ImageRenderMaxEdge,
            PdfInput: settings.PdfInput,
            PdfUpload: options.PdfUpload,
            PdfInputMaxPages: settings.PdfInputMaxPages,
            PdfInputMaxBytes: settings.PdfInputMaxBytes));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
