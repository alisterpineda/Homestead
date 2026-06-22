namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Read-only view of the single static AI configuration. The raw API key is never returned — only a
// presence boolean (HasApiKey). MaxInputChars is the resolved effective budget, not the raw nullable.
public record AiStatusResponse(
    bool Configured,
    string? Endpoint,
    string? Model,
    bool HasApiKey,
    int MaxInputChars);
