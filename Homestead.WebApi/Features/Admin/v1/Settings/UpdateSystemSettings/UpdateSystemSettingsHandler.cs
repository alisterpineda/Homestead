using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.Settings.UpdateSystemSettings;

public class UpdateSystemSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger,
    IAppSettingsConfigurationProvider reloader)
{
    public async Task<SystemSettingsResponse> HandleAsync(
        UpdateSystemSettingsRequest request, CancellationToken ct)
    {
        _ = request; // Placeholder — will apply properties as SystemSettings grows

        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.SystemModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<SystemSettings>(entity.Value, FieldChange.JsonOptions) ?? new SystemSettings()
            : new SystemSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        var newValue = JsonSerializer.Serialize(settings, FieldChange.JsonOptions);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Module = AppSetting.SystemModule,
                Value = newValue,
                UpdatedByUserId = currentUser.UserId
            };
            db.AppSettings.Add(entity);

            auditLogger.LogCreated("AppSetting", AppSettingSnapshot.ModuleToGuid(AppSetting.SystemModule), currentUser.UserId);
        }
        else
        {
            entity.Value = newValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedByUserId = currentUser.UserId;

            var after = AppSettingSnapshot.From(entity);
            var changes = before!.Diff(after);

            if (changes.Count > 0)
                auditLogger.LogUpdated("AppSetting", AppSettingSnapshot.ModuleToGuid(AppSetting.SystemModule),
                    currentUser.UserId, changes);
        }

        await db.SaveChangesAsync(ct);
        reloader.Reload();

        return new SystemSettingsResponse();
    }
}
