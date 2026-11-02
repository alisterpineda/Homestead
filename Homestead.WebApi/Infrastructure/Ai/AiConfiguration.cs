using Homestead.WebApi.Common;

namespace Homestead.WebApi.Infrastructure.Ai;

// Turns the "Ai" section into a FeatureAvailability, and detects the retired multi-provider keys.
//
// Validation returns a reason rather than throwing: there is deliberately no ValidateOnStart (mirroring
// OcrEngineConfiguration) — a misconfigured AI section must not stop the app from booting: the build-time OpenAPI
// generator boots the host with no meaningful config, and AI is optional anyway. The cost is that config errors
// surface at first use; the mitigation is that the reason is on the admin status page and logged once at startup.
public static class AiConfiguration
{
    // The retired shape (named provider instances plus a default). Unconfigured AI is a silent no-op, so an
    // operator who upgrades without renaming must be told rather than quietly lose AI.
    public const string LegacyProvidersSection = "Ai:Providers";
    public const string LegacyDefaultProviderKey = "Ai:DefaultProvider";

    public const string LegacyConfigReason =
        "Legacy AI config found (Ai:Providers / Ai:DefaultProvider are no longer read) — set Ai:ApiKey and Ai:Model.";

    public static bool LegacyKeysPresent(IConfiguration configuration) =>
        configuration.GetSection(LegacyProvidersSection).Exists()
        || configuration[LegacyDefaultProviderKey] is not null;

    // "Present" = the operator said something about AI: a connection field, an effort, or the legacy keys. The
    // input knobs alone (budgets, ImageInput) don't count — they have defaults and mean nothing without a model.
    // Legacy keys are flagged only when the new ApiKey/Model are both absent, so old and new shapes can coexist
    // during the transition (that is the rollback story).
    public static FeatureAvailability Validate(AiOptions options, bool legacyKeysPresent)
    {
        var hasKey = !string.IsNullOrWhiteSpace(options.ApiKey);
        var hasModel = !string.IsNullOrWhiteSpace(options.Model);
        var present = hasKey || hasModel || legacyKeysPresent
            || !string.IsNullOrWhiteSpace(options.Endpoint)
            || !string.IsNullOrWhiteSpace(options.Effort);

        if (!present)
            return FeatureAvailability.Absent;
        if (legacyKeysPresent && !hasKey && !hasModel)
            return FeatureAvailability.Unavailable(LegacyConfigReason);
        if (!string.IsNullOrWhiteSpace(options.Endpoint) && !Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
            return FeatureAvailability.Unavailable("Ai:Endpoint must be an absolute URI.");
        if (!hasKey)
            return FeatureAvailability.Unavailable("Ai:ApiKey is required.");
        if (!hasModel)
            return FeatureAvailability.Unavailable("Ai:Model is required.");
        if (ParseEffort(options.Effort) is null)
            return FeatureAvailability.Unavailable(
                $"Ai:Effort '{options.Effort}' is not one of: {string.Join(", ", Enum.GetValues<AiEffort>().Select(ToWire))}.");

        return FeatureAvailability.Ready;
    }

    // Blank = medium; otherwise the wire string, case-insensitive; null when unrecognised.
    public static AiEffort? ParseEffort(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? AiEffort.Medium
            : Enum.GetValues<AiEffort>().Cast<AiEffort?>()
                .FirstOrDefault(e => string.Equals(ToWire(e!.Value), value.Trim(), StringComparison.OrdinalIgnoreCase));

    // The Claude API's spelling, which is also what config and the admin page use.
    public static string ToWire(AiEffort effort) => effort switch
    {
        AiEffort.Low => "low",
        AiEffort.Medium => "medium",
        AiEffort.High => "high",
        AiEffort.XHigh => "xhigh",
        AiEffort.Max => "max",
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
    };
}
