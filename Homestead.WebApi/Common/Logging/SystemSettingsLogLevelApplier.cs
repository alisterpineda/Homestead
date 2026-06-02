using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// Reads the persisted <c>system</c> settings blob and pushes its log-level overrides into the live
/// <see cref="RuntimeLogLevelProvider"/>. The shared read-and-apply the <c>SettingsChangeListener</c> runs
/// on connect and on every NOTIFY / safety poll, so the committed row is always the source of truth.
/// </summary>
/// <remarks>
/// This is deliberately stateless and re-runnable on every signal: the committed row — not a timestamp
/// watermark — is the source of truth, and <see cref="RuntimeLogLevelProvider.SetLevels"/> is idempotent
/// (it only raises a reload token when the levels actually change), so re-reading on every NOTIFY, reconnect,
/// and safety poll never churns MEL's filter pipeline.
/// </remarks>
public static class SystemSettingsLogLevelApplier
{
    /// <summary>
    /// Reads the <c>system</c> <see cref="AppSetting"/> row and applies its log-level overrides to
    /// <paramref name="provider"/>. When the row is absent the overrides are cleared. A malformed blob is
    /// tolerated (logged, then cleared) so it can never brick startup or the listener.
    /// </summary>
    public static async Task ApplyAsync(
        AppDbContext db,
        RuntimeLogLevelProvider provider,
        ILogger log,
        CancellationToken ct)
    {
        var row = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);

        if (row is null)
        {
            // No row persisted — ensure no overrides linger. Idempotent, so this is a no-op (no reload
            // token) when nothing was applied, e.g. a pristine DB or repeated safety polls.
            provider.SetLevels(null);
            return;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<SystemSettings>(row.Value, FieldChange.JsonOptions);
            provider.SetLevels(settings?.GetLogLevelOverrides());
        }
        catch (JsonException ex)
        {
            // A malformed/legacy blob must not brick startup or the listener — degrade to "no overrides".
            log.LogWarning(ex,
                "Could not parse the persisted system settings blob; runtime log-level overrides were not applied.");
            provider.SetLevels(null);
        }
    }
}
