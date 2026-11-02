namespace Homestead.WebApi.Common;

// The verdict on an optional, config-driven feature (AI, the MCP server), computed once from its section with the
// error-string posture (no ValidateOnStart): absent config, misconfigured config and "ready" are three states callers
// tell apart — absent is quiet, misconfigured carries the reason. Reason is set only when IsPresent && !IsConfigured.
public sealed record FeatureAvailability(bool IsConfigured, bool IsPresent, string? Reason)
{
    public static readonly FeatureAvailability Absent = new(false, false, null);
    public static readonly FeatureAvailability Ready = new(true, true, null);

    public static FeatureAvailability Unavailable(string reason) => new(false, true, reason);

    // The one wording of "can't use it": "{feature} is unavailable: {Reason}" or "{feature} is not configured."
    public void EnsureConfigured(string feature)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                IsPresent ? $"{feature} is unavailable: {Reason}" : $"{feature} is not configured.");
    }
}
