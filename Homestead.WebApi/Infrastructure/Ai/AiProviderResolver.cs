using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

public sealed class AiProviderResolver : IAiProviderResolver
{
    private readonly IReadOnlyList<IAiProvider> _providers;
    // Computed once: the provider set and the configured default are both fixed at startup, and the
    // no-explicit-key branch is the hot path (every classify/upload/statement resolve).
    private readonly string? _defaultKey;

    public AiProviderResolver(IEnumerable<IAiProvider> providers, string? defaultProviderKey)
    {
        _providers = providers.ToList();
        _defaultKey = AiProviderConfiguration.ResolveDefaultKey(
            defaultProviderKey, _providers.Select(p => p.Key).ToList());
    }

    public IAiProvider? Resolve(string? requestedKey = null)
    {
        var key = string.IsNullOrWhiteSpace(requestedKey) ? _defaultKey : requestedKey.Trim();

        if (key is null)
            return null;

        return Find(key) ?? new UndefinedAiProvider(key);
    }

    // SingleOrDefault so a duplicate key is a loud throw, never one instance shadowing the other.
    // Case-insensitive to match config-key semantics (IConfiguration keys are case-insensitive, so config
    // itself can never define two keys differing only in case).
    private IAiProvider? Find(string key) =>
        _providers.SingleOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    // What a dangling key resolves to: unavailable-with-reason under the REQUESTED key. Keeps the
    // no-silent-substitution guarantee — the caller sees exactly what it asked for, and why it can't run.
    private sealed class UndefinedAiProvider(string key) : IAiProvider
    {
        public string Key => key;
        public AiProviderType? Type => null;
        public bool IsConfigured => false;
        public string? ConfigurationError => $"AI provider '{key}' is not defined under Ai:Providers.";

        public string? Endpoint => null;
        public string? Model => null;
        public bool HasApiKey => false;

        public int MaxInputChars => AiProviderOptions.DefaultMaxInputChars;
        public int StatementMaxInputChars => AiProviderOptions.DefaultStatementMaxInputChars;
        public bool ImageInput => false;
        public int ImageRenderMaxEdge => AiProviderOptions.DefaultImageRenderMaxEdge;
        public float? Temperature => null;
        public bool StructuredOutput => true;

        public IChatClient GetChatClient() =>
            throw new InvalidOperationException($"AI provider '{key}' is unavailable: {ConfigurationError}");
    }
}
