using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Read-only view of one configured AI provider instance. The raw API key is never returned — only a
// presence boolean (HasApiKey). MaxInputChars is the resolved effective budget, not the raw nullable.
// Temperature and StructuredOutput are the request-shaping knobs exactly as configured: Temperature is the
// raw nullable, where null means "not sent" (the model's own default applies) — or a non-finite value, which
// can't be written as JSON and is already reported through ConfigurationError — and StructuredOutput=false
// means schema requests are downgraded to plain JSON mode, so replies are no longer schema-enforced.
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
    int MaxInputChars,
    float? Temperature,
    bool StructuredOutput);
