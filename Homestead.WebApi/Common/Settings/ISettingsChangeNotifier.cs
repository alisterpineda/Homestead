namespace Homestead.WebApi.Common.Settings;

/// <summary>
/// Broadcasts that a persisted settings blob changed so every WebApi instance can re-read it with no
/// restart. The payload carries only the setting <paramref name="key"/> — recipients always re-read the
/// committed row (signal-to-re-read), never trust a payload value.
/// </summary>
public interface ISettingsChangeNotifier
{
    /// <summary>
    /// Signals that the settings identified by <paramref name="key"/> changed. Best-effort: a dropped
    /// signal only delays propagation until each instance's safety poll, so callers must not depend on it
    /// for correctness (the committed row is the source of truth).
    /// </summary>
    Task NotifyAsync(string key, CancellationToken ct);
}
