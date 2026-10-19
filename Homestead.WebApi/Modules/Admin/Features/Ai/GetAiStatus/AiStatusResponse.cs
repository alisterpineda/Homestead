namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Read-only view of the AI configuration. The raw API key is never returned — only a presence boolean (HasApiKey).
// IsPresent false = no Ai:* config at all (AI off); IsPresent true with IsConfigured false = config that cannot run,
// with ConfigurationError saying why (including the retired Ai:Providers shape). Endpoint null = the Claude API's
// default base URL. Effort is the resolved wire value; the input knobs are the resolved effective values.
public record AiStatusResponse(
    bool IsConfigured,
    bool IsPresent,
    string? ConfigurationError,
    string? Model,
    string? Endpoint,
    bool HasApiKey,
    string Effort,
    bool RefusalFallback,
    int MaxInputChars,
    int StatementMaxInputChars,
    bool ImageInput,
    int ImageRenderMaxEdge,
    bool PdfInput,
    bool PdfUpload,
    int PdfInputMaxPages,
    long PdfInputMaxBytes);
