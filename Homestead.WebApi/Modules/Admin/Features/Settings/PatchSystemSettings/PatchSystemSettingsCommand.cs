using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public record PatchSystemSettingsCommand : ICommand<SystemSettingsResponse>
{
    /// <summary>
    /// New log-level overrides. Null means "not provided — leave the stored set unchanged". A provided map
    /// fully <b>replaces</b> the stored set (the settings page owns the whole map, so adding or removing a
    /// category is just a different map; an empty map clears all overrides).
    /// </summary>
    public Dictionary<string, LogLevel>? LogLevelOverrides { get; init; }
}
