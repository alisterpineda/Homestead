namespace Homestead.WebApi.Modules.Admin.Features.Settings;

public record SystemSettingsResponse
{
    /// <summary>
    /// Runtime log-level overrides, keyed by category. The global <c>JsonStringEnumConverter</c> serializes
    /// the <see cref="LogLevel"/> values as their names, producing a clean <c>'Trace'|'Debug'|…</c> string
    /// union in the generated TypeScript model.
    /// </summary>
    public Dictionary<string, LogLevel>? LogLevelOverrides { get; init; }
}
