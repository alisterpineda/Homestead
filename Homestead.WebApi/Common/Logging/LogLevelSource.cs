namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// Where a category's effective log level comes from, in precedence order (highest wins). Serialized by name
/// via the global <c>JsonStringEnumConverter</c>, so the generated TypeScript model is a clean string union.
/// </summary>
public enum LogLevelSource
{
    /// <summary>A runtime override set from the admin UI — the live <see cref="RuntimeLogLevelProvider"/>.</summary>
    Override,

    /// <summary>An <c>appsettings*.json</c> value (or any other static/in-memory configuration source).</summary>
    AppSettings,

    /// <summary>An environment variable, e.g. <c>Logging__LogLevel__Foo</c>.</summary>
    Environment,

    /// <summary>A code-registered <c>AddFilter</c> rule with no matching configuration entry.</summary>
    Code,

    /// <summary>The fallback minimum — nothing more specific matched the category.</summary>
    Default,
}
