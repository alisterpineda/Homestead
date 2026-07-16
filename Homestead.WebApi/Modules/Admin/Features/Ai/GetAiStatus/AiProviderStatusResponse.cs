using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Read-only view of one configured AI provider instance. The raw API key is never returned — only a
// presence boolean (HasApiKey). MaxInputChars is the resolved effective budget, not the raw nullable.
// Type is null when config omitted it (the provider is then unavailable with "Type is required").
public record AiProviderStatusResponse(
    string Key,
    AiProviderType? Type,
    bool IsConfigured,
    string? ConfigurationError,
    bool IsDefault,
    string? Endpoint,
    string? Model,
    bool HasApiKey,
    int MaxInputChars);
