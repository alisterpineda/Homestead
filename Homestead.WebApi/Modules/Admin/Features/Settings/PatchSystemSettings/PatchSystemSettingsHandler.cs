using Homestead.WebApi.Common.Auditing;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Admin.Features.Settings;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public class PatchSystemSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger)
    : IRequestHandler<PatchSystemSettingsCommand, SystemSettingsResponse>
{
    public async Task<SystemSettingsResponse> Handle(
        PatchSystemSettingsCommand request, CancellationToken ct)
    {
        _ = request; // Placeholder — will apply properties as SystemSettings grows

        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<SystemSettings>(entity.Value, FieldChange.JsonOptions) ?? new SystemSettings()
            : new SystemSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        var newValue = JsonSerializer.Serialize(settings, FieldChange.JsonOptions);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Key = AppSetting.SystemModule,
                Value = newValue,
                UpdatedByUserId = currentUser.UserId
            };
            db.AppSettings.Add(entity);

            auditLogger.LogCreated("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule));
        }
        else
        {
            entity.Value = newValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedByUserId = currentUser.UserId;

            var after = AppSettingSnapshot.From(entity);
            var changes = before!.Diff(after);

            if (changes.Count > 0)
                auditLogger.LogUpdated("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule),
                    changes);
        }

        await db.SaveChangesAsync(ct);

        return new SystemSettingsResponse();
    }
}
