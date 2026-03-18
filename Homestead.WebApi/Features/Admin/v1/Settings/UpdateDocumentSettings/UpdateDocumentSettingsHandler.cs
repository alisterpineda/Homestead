using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.Settings.UpdateDocumentSettings;

public class UpdateDocumentSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger,
    IAppSettingsConfigurationProvider reloader)
{
    public async Task<DocumentSettingsResponse> HandleAsync(
        UpdateDocumentSettingsRequest request, CancellationToken ct)
    {
        if (request.MaxUploadSizeMb is not null and (<= 0 or > 10000))
            throw new BadRequestException("MaxUploadSizeMb must be between 1 and 10000.");

        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.DocumentsModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions) ?? new DocumentSettings()
            : new DocumentSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        if (request.MaxUploadSizeMb is not null)
            settings.MaxUploadSizeMb = request.MaxUploadSizeMb.Value;

        var newValue = JsonSerializer.Serialize(settings, FieldChange.JsonOptions);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Module = AppSetting.DocumentsModule,
                Value = newValue,
                UpdatedByUserId = currentUser.UserId
            };
            db.AppSettings.Add(entity);

            auditLogger.LogCreated("AppSetting", AppSettingSnapshot.ModuleToGuid(AppSetting.DocumentsModule), currentUser.UserId);
        }
        else
        {
            entity.Value = newValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedByUserId = currentUser.UserId;

            var after = AppSettingSnapshot.From(entity);
            var changes = before!.Diff(after);

            if (changes.Count > 0)
                auditLogger.LogUpdated("AppSetting", AppSettingSnapshot.ModuleToGuid(AppSetting.DocumentsModule),
                    currentUser.UserId, changes);
        }

        await db.SaveChangesAsync(ct);
        reloader.Reload();

        return new DocumentSettingsResponse
        {
            MaxUploadSizeMb = settings.MaxUploadSizeMb
        };
    }
}
