namespace Homestead.WebApi.Infrastructure.Ai;

// Reads the "Ai:Providers" section into named provider instances, validates each per its Type, and owns the
// key side of the default-provider resolution chain.
//
// Validation returns an error string rather than throwing: there is deliberately no ValidateOnStart
// (mirroring OcrEngineConfiguration) — a misconfigured provider must not stop the app from booting: the
// build-time OpenAPI generator boots the host with no meaningful config, and AI is optional anyway. The
// cost is that config errors surface at first use; the mitigation is that ConfigurationError is exposed on
// the admin status page, so a provider that cannot run reads as "unavailable, because <reason>".
public static class AiProviderConfiguration
{
    public const string DefaultProviderConfigKey = "Ai:DefaultProvider";

    // One entry per "Ai:Providers:{key}" child. No built-in seeding (contrast ReadEngines' tesseract):
    // there is no zero-config AI provider, so zero children = zero providers = AI off, the graceful no-op.
    public static IReadOnlyList<(string Key, AiProviderOptions Options)> ReadProviders(IConfiguration configuration)
    {
        var providers = new List<(string Key, AiProviderOptions Options)>();

        foreach (var child in configuration.GetSection(AiProviderOptions.SectionName).GetChildren())
            providers.Add((child.Key, child.Get<AiProviderOptions>() ?? new AiProviderOptions()));

        return providers;
    }

    public static string? ReadDefaultProviderKey(IConfiguration configuration)
    {
        var key = configuration[DefaultProviderConfigKey];
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    // Returns null when the instance is usable, else a human-readable reason shown on the admin status page.
    public static string? Validate(string key, AiProviderOptions options) => options.Type switch
    {
        AiProviderType.OpenAiCompatible => ValidateOpenAiCompatible(options),
        AiProviderType.AzureFoundry => ValidateAzureFoundry(options),
        // Refuse rather than defaulting to OpenAiCompatible, which would silently run the wrong protocol
        // under the operator's chosen name.
        null => $"Type is required (one of: {string.Join(", ", Enum.GetNames<AiProviderType>())}).",
        var type => $"Unknown provider type '{type}'."
    };

    // The key side of the resolution chain (no explicit request): the configured default when set — returned
    // VERBATIM even when it names no defined provider, because turning a dangling default into
    // unavailable-with-reason is the resolver's job and substituting a healthy sibling would be silent —
    // else the sole defined key, else null (AI off). The sole-provider convenience therefore never
    // overrides an operator's explicit (even if broken) choice.
    public static string? ResolveDefaultKey(string? configuredDefault, IReadOnlyList<string> definedKeys) =>
        configuredDefault ?? (definedKeys.Count == 1 ? definedKeys[0] : null);

    private static string? ValidateOpenAiCompatible(AiProviderOptions options)
    {
        if (ValidateEndpoint(options) is { } endpointError)
            return endpointError;
        if (string.IsNullOrWhiteSpace(options.Model))
            return "Model is required.";

        // No ApiKey check: local runtimes (Ollama, OpenWebUI) ignore it; a dummy is substituted when blank.
        return null;
    }

    private static string? ValidateAzureFoundry(AiProviderOptions options)
    {
        if (ValidateEndpoint(options) is { } endpointError)
            return endpointError;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return "ApiKey is required.";
        if (string.IsNullOrWhiteSpace(options.Model))
            return "Model (the deployment name) is required.";

        return null;
    }

    // Every provider type needs a present, absolute endpoint. The absolute-URI check is a small deliberate
    // upgrade over the old flat AiOptions probe: a malformed endpoint used to pass IsConfigured and only
    // blow up when the client was built. Nothing legal is rejected — both clients require an absolute URI.
    private static string? ValidateEndpoint(AiProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            return "Endpoint is required.";
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
            return "Endpoint must be an absolute URI.";

        return null;
    }
}
