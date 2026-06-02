using System.Text.Json.Serialization;

namespace Homestead.WebApi.Modules.Admin.Features.Settings;

public class SystemSettings
{
    /// <summary>
    /// Runtime log-level overrides, keyed by category (e.g. "Default", "Microsoft.AspNetCore", or any
    /// custom namespace). Values are <see cref="LogLevel"/> <b>names</b> so the stored JSON and the audit
    /// diff stay human-readable. A null map means "no overrides have ever been set".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LogLevelOverrides { get; set; }

    /// <summary>
    /// Parses <see cref="LogLevelOverrides"/> into a category→<see cref="LogLevel"/> map for the logging
    /// layer. Unparseable or blank entries are skipped. Returns null when no overrides are stored.
    /// </summary>
    public Dictionary<string, LogLevel>? GetLogLevelOverrides()
    {
        if (LogLevelOverrides is null)
            return null;

        var result = new Dictionary<string, LogLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, level) in LogLevelOverrides)
        {
            if (string.IsNullOrWhiteSpace(category))
                continue;
            // Skip blank/unknown names AND out-of-range numeric values (Enum.TryParse accepts arbitrary
            // integers like "999"), so only the seven defined levels ever reach the logging layer.
            if (Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
                result[category] = parsed;
        }

        return result;
    }
}
