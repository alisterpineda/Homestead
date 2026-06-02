using Homestead.WebApi.Common.Logging;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetLogLevels;

/// <summary>
/// A read-only snapshot of this instance's live logging configuration. <see cref="Rules"/> is always
/// present; <see cref="Resolved"/> is populated only when the request supplied a category to probe.
/// </summary>
public record LogLevelsResponse
{
    /// <summary>The global fallback minimum level (MEL's <c>LoggerFilterOptions.MinLevel</c>).</summary>
    public required LogLevel MinLevel { get; init; }

    /// <summary>Every active category rule, with the level it resolves to and where that level comes from.</summary>
    public required IReadOnlyList<LogLevelRule> Rules { get; init; }

    /// <summary>The result of resolving a specific category, when one was requested; otherwise null.</summary>
    public ResolvedLogLevel? Resolved { get; init; }
}

/// <summary>One active log-level rule: a category, its configured level, and where that level comes from.</summary>
public record LogLevelRule
{
    public required string Category { get; init; }
    public required LogLevel Level { get; init; }
    public required LogLevelSource Source { get; init; }
}

/// <summary>The effective level a concrete category logs at, and which rule it inherited that level from.</summary>
public record ResolvedLogLevel
{
    /// <summary>The category that was probed (as supplied, trimmed).</summary>
    public required string Category { get; init; }

    /// <summary>
    /// The effective minimum enabled level, determined authoritatively by probing
    /// <see cref="ILogger.IsEnabled(LogLevel)"/>.
    /// </summary>
    public required LogLevel EffectiveLevel { get; init; }

    /// <summary>
    /// The rule category <see cref="Category"/> matched — equal to it when set directly, an ancestor
    /// (e.g. "Homestead") when inherited, or "Default" when only the catch-all applied.
    /// </summary>
    public required string MatchedCategory { get; init; }

    /// <summary>The source of the matched rule.</summary>
    public required LogLevelSource Source { get; init; }
}
