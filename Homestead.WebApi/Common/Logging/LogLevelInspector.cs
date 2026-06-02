using Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Common.Logging;

/// <summary>The active logging-rule table for this instance, plus the global fallback minimum.</summary>
public record LogLevelTable
{
    public required LogLevel MinLevel { get; init; }
    public required IReadOnlyList<LogLevelRule> Rules { get; init; }
}

/// <summary>
/// Reports the <i>effective</i> backend log level for any namespace and where that level comes from.
/// </summary>
/// <remarks>
/// Effective levels are read by probing <see cref="ILogger.IsEnabled(LogLevel)"/>, which is authoritative:
/// at runtime the only rule sources are appsettings + environment + the runtime overrides (the sole
/// <c>AddFilter</c> in the codebase is build-time-only) and OTel logging adds no category filters, so the
/// composite logger's own answer is the truth — there is no need to re-implement MEL's rule engine. Source
/// attribution is a separate, best-effort walk of the live configuration providers in precedence order.
/// </remarks>
public interface ILogLevelInspector
{
    /// <summary>The full active rule table — one row per configured/known category, Default first.</summary>
    LogLevelTable GetTable();

    /// <summary>Resolves the effective level for a concrete category and the rule it inherited it from.</summary>
    ResolvedLogLevel Resolve(string category);

    /// <summary>
    /// Resolves against an already-built rule set (from <see cref="GetTable"/>), so a request that returns
    /// both the table and a resolution doesn't build the table twice.
    /// </summary>
    ResolvedLogLevel Resolve(string category, IReadOnlyList<LogLevelRule> rules);
}

public sealed class LogLevelInspector(
    ILoggerFactory loggerFactory,
    IConfiguration configuration,
    IOptionsMonitor<LoggerFilterOptions> filterOptions,
    RuntimeLogLevelProvider runtimeProvider) : ILogLevelInspector
{
    // Trace→Critical: the first level IsEnabled accepts is the effective minimum; None when none pass.
    private static readonly LogLevel[] ProbeLevels =
    [
        LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical,
    ];

    // The DI IConfiguration is the ConfigurationManager/root, so its Providers expose source attribution.
    private readonly IConfigurationRoot _configRoot = (IConfigurationRoot)configuration;

    public LogLevelTable GetTable()
    {
        var rules = new Dictionary<string, LogLevelRule>(StringComparer.OrdinalIgnoreCase);

        // (a) Every category configured under Logging:LogLevel, as the merged config root sees it — so an
        // override shadowing an appsettings value yields a single row at the winning level and source.
        foreach (var child in configuration.GetSection("Logging:LogLevel").GetChildren())
        {
            var category = child.Key;
            if (string.IsNullOrWhiteSpace(category))
                continue;
            rules[category] = new LogLevelRule
            {
                Category = category,
                Level = ParseLevel(child.Value) ?? ProbeEffectiveLevel(category),
                Source = SourceFor(category),
            };
        }

        // (b) Provider-agnostic code rules (a real AddFilter) not already represented in config. None exist
        // at runtime today — the lone AddFilter is build-time-only — but this keeps the table honest if one
        // is ever added.
        foreach (var rule in filterOptions.CurrentValue.Rules)
        {
            if (rule.ProviderName is not null || string.IsNullOrWhiteSpace(rule.CategoryName))
                continue;
            if (rules.ContainsKey(rule.CategoryName))
                continue;
            rules[rule.CategoryName] = new LogLevelRule
            {
                Category = rule.CategoryName,
                Level = rule.LogLevel ?? ProbeEffectiveLevel(rule.CategoryName),
                Source = LogLevelSource.Code,
            };
        }

        var ordered = rules.Values
            .OrderByDescending(r => IsDefault(r.Category)) // catch-all first
            .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new LogLevelTable { MinLevel = filterOptions.CurrentValue.MinLevel, Rules = ordered };
    }

    public ResolvedLogLevel Resolve(string category) => Resolve(category, GetTable().Rules);

    public ResolvedLogLevel Resolve(string category, IReadOnlyList<LogLevelRule> rules)
    {
        category = category.Trim();
        // The probe is authoritative for the level; the matched rule only attributes the origin/source.
        var effective = ProbeEffectiveLevel(category);
        var match = MatchRule(rules, category);

        return new ResolvedLogLevel
        {
            Category = category,
            EffectiveLevel = effective,
            MatchedCategory = match?.Category ?? "Default",
            Source = match?.Source ?? LogLevelSource.Default,
        };
    }

    private LogLevel ProbeEffectiveLevel(string category)
    {
        var logger = loggerFactory.CreateLogger(category);
        foreach (var level in ProbeLevels)
        {
            if (logger.IsEnabled(level))
                return level;
        }
        return LogLevel.None;
    }

    // Walk providers highest-precedence first (added last wins, so reverse) and attribute the first one that
    // actually holds the key. Absent from every config provider ⇒ the level came from a code rule.
    private LogLevelSource SourceFor(string category)
    {
        var key = $"Logging:LogLevel:{category}";
        foreach (var provider in _configRoot.Providers.Reverse())
        {
            if (!provider.TryGet(key, out _))
                continue;
            if (ReferenceEquals(provider, runtimeProvider))
                return LogLevelSource.Override;
            if (provider is EnvironmentVariablesConfigurationProvider)
                return LogLevelSource.Environment;
            // appsettings*.json and in-memory defaults both represent static/file baseline configuration.
            return LogLevelSource.AppSettings;
        }
        return LogLevelSource.Code;
    }

    // Longest case-insensitive prefix wins, mirroring MEL. "Default" is the catch-all, used only when no
    // more specific rule matches.
    private static LogLevelRule? MatchRule(IReadOnlyList<LogLevelRule> rules, string category)
    {
        LogLevelRule? best = null;
        LogLevelRule? fallback = null;
        var bestLength = -1;

        foreach (var rule in rules)
        {
            if (IsDefault(rule.Category))
            {
                fallback = rule;
                continue;
            }
            if (TryPrefixMatch(rule.Category, category, out var length) && length > bestLength)
            {
                best = rule;
                bestLength = length;
            }
        }

        return best ?? fallback;
    }

    // A rule matches when its category is a case-insensitive prefix of the logger category. A single '*'
    // wildcard is honoured (prefix*suffix), as MEL allows; the matched length drives longest-wins selection.
    private static bool TryPrefixMatch(string pattern, string category, out int length)
    {
        length = 0;
        var star = pattern.IndexOf('*');
        if (star >= 0)
        {
            var prefix = pattern[..star];
            var suffix = pattern[(star + 1)..];
            if (category.Length >= prefix.Length + suffix.Length &&
                category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                category.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                length = prefix.Length + suffix.Length;
                return true;
            }
            return false;
        }
        if (category.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
        {
            length = pattern.Length;
            return true;
        }
        return false;
    }

    private static LogLevel? ParseLevel(string? value)
        => Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) && Enum.IsDefined(level) ? level : null;

    private static bool IsDefault(string category)
        => string.Equals(category, "Default", StringComparison.OrdinalIgnoreCase);
}
