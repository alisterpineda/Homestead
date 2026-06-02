using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// An in-memory <see cref="ConfigurationProvider"/> — and its own <see cref="IConfigurationSource"/>
/// (<see cref="Build"/> returns <c>this</c>) — that holds only <c>Logging:LogLevel:&lt;category&gt;</c>
/// keys. The single instance is added to the configuration builder <b>last</b> so its values win over
/// appsettings/env, and is also registered in DI so the apply path can push to it. It raises a reload
/// token whenever the levels change. Because Microsoft.Extensions.Logging binds
/// <c>config.GetSection("Logging")</c> via a <c>ConfigurationChangeTokenSource&lt;LoggerFilterOptions&gt;</c>,
/// calling <see cref="SetLevels"/> re-fires <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// and the <see cref="ILoggerFactory"/> refreshes its category filters with no restart.
/// </summary>
/// <remarks>
/// This is only the in-process adapter into MEL's no-restart reconfiguration seam — the persisted
/// <c>settings.app_settings</c> row is the source of truth. Multi-instance propagation is handled outside
/// this class: every instance re-reads the committed row and calls <see cref="SetLevels"/> in response to a
/// PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c> signal (see <c>SettingsChangeListener</c>).
/// </remarks>
public sealed class RuntimeLogLevelProvider : ConfigurationProvider, IConfigurationSource
{
    /// <summary>
    /// Replaces the in-memory log-level overrides and raises a reload token. Passing <see langword="null"/>
    /// or an empty map clears all overrides so categories fall back to the appsettings/env values. Each
    /// entry becomes <c>Logging:LogLevel:{category}</c> = <c>level.ToString()</c>.
    /// </summary>
    public void SetLevels(IReadOnlyDictionary<string, LogLevel>? levels)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (levels is not null)
        {
            foreach (var (category, level) in levels)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                data[$"Logging:LogLevel:{category}"] = level.ToString();
            }
        }

        // Idempotent: only swap the data and raise a reload token when the levels actually changed. This is
        // load-bearing — every re-apply path (a redundant save, a reconnect heal, the 60s safety poll) calls
        // through here, so without this guard MEL's filter pipeline would re-bind on every signal for no
        // behavioural change. It also lets the read-and-apply layer stay simple: it can re-apply freely.
        if (DataEquals(data))
            return;

        Data = data;
        OnReload();
    }

    private bool DataEquals(Dictionary<string, string?> candidate)
    {
        if (Data.Count != candidate.Count)
            return false;
        foreach (var (key, value) in candidate)
        {
            if (!Data.TryGetValue(key, out var existing) || existing != value)
                return false;
        }
        return true;
    }

    /// <summary>This provider is its own source — the config root gets the same instance DI holds.</summary>
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    // The values are pushed in via SetLevels, never loaded from an external store. Start empty.
    public override void Load()
    {
    }
}
