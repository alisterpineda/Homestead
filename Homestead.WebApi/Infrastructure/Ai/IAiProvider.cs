using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// A named, config-defined AI provider instance (mirrors ITextExtractionEngine). Implementations are
// registered as IEnumerable<IAiProvider> singletons, NOT keyed services: MS DI cannot enumerate keyed
// registrations, and enumeration is what feeds the admin status page from a single source of truth.
public interface IAiProvider
{
    // The config instance name, i.e. the {key} in "Ai:Providers:{key}". Looked up case-insensitively with
    // SingleOrDefault, so a duplicate registration throws loudly rather than one shadowing the other.
    string Key { get; }

    // Null when config omitted it — the provider is then unavailable with "Type is required".
    AiProviderType? Type { get; }

    bool IsConfigured { get; }

    // Why the provider cannot run, when IsConfigured is false. Surfaced on the admin status page and in
    // job errors so a broken provider reads as "unavailable, because <reason>".
    string? ConfigurationError { get; }

    // Read-only config surface for the admin status page. Never the raw ApiKey.
    string? Endpoint { get; }
    string? Model { get; }
    bool HasApiKey { get; }

    // Resolved (floored/defaulted/clamped) per-call knobs — see AiProviderOptions for semantics.
    int MaxInputChars { get; }
    int StatementMaxInputChars { get; }
    bool ImageInput { get; }
    int ImageRenderMaxEdge { get; }

    // The chat client for this provider. Built once and reused for the app lifetime (the underlying client
    // is a stateless, thread-safe HTTP wrapper) — callers must NOT dispose it. Throws
    // InvalidOperationException naming the key and ConfigurationError when the provider is unavailable.
    IChatClient GetChatClient();
}
