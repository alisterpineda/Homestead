namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// Applies log-level overrides to the running process. A thin wrapper over
/// <see cref="RuntimeLogLevelProvider"/> so handlers and startup stay off the configuration internals.
/// </summary>
public interface ILogLevelService
{
    /// <summary>
    /// Applies the given category→level overrides system-wide and immediately. A null or empty map clears
    /// all overrides so every category falls back to the appsettings/env values.
    /// </summary>
    void Apply(IReadOnlyDictionary<string, LogLevel>? overrides);
}
