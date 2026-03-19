using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.Settings.PatchDocumentSettings;

public class PatchDocumentSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditLogger auditLogger,
    IAppSettingsConfigurationProvider reloader)
{
    public async Task<DocumentSettingsResponse> HandleAsync(
        PatchDocumentSettingsCommand command, CancellationToken ct)
    {
        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.DocumentsModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions) ?? new DocumentSettings()
            : new DocumentSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        // Apply patch operations directly to settings
        var errors = new List<JsonPatchError>();
        command.PatchDocument.ApplyTo(settings, error => errors.Add(error));

        if (errors.Count > 0)
            throw new BadRequestException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        // Validate
        if (settings.MaxUploadSizeMb is <= 0 or > 10000)
            throw new BadRequestException("MaxUploadSizeMb must be between 1 and 10000.");

        if (settings.DefaultTextExtractionEngineId is { } engineId)
        {
            await TextExtractionEngineValidator.ValidateEngineExistsAsync(db, engineId, ct);
        }

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
            MaxUploadSizeMb = settings.MaxUploadSizeMb,
            DefaultTextExtractionEngineId = settings.DefaultTextExtractionEngineId
        };
    }
}
