using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// An in-memory <see cref="ConfigurationProvider"/> that holds only <c>Logging:LogLevel:&lt;category&gt;</c>
/// keys. It is registered as the <b>last</b> configuration source so its values win over appsettings/env,
/// and it raises a reload token whenever the levels change. Because Microsoft.Extensions.Logging binds
/// <c>config.GetSection("Logging")</c> via a <c>ConfigurationChangeTokenSource&lt;LoggerFilterOptions&gt;</c>,
/// calling <see cref="SetLevels"/> re-fires <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// and the <see cref="ILoggerFactory"/> refreshes its category filters with no restart.
/// </summary>
/// <remarks>
/// The override is applied in-process only. In a multi-instance deployment a change saved on one instance
/// is not pushed to the others — they pick it up on their next restart via the startup load. Homestead
/// runs single-instance, so this is acceptable.
/// </remarks>
public sealed class RuntimeLogLevelProvider : ConfigurationProvider
{
    /// <summary>
    /// Replaces the in-memory log-level overrides and raises a reload token. Passing an empty map clears
    /// all overrides so categories fall back to the appsettings/env values. Each entry becomes
    /// <c>Logging:LogLevel:{category}</c> = <c>level.ToString()</c>.
    /// </summary>
    public void SetLevels(IReadOnlyDictionary<string, LogLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, level) in levels)
        {
            if (string.IsNullOrWhiteSpace(category))
                continue;
            data[$"Logging:LogLevel:{category}"] = level.ToString();
        }

        Data = data;
        OnReload();
    }

    // The values are pushed in via SetLevels, never loaded from an external store. Start empty.
    public override void Load()
    {
    }
}
