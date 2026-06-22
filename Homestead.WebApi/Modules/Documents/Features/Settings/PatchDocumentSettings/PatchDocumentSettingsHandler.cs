using Homestead.WebApi.Common.Auditing;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;

public class PatchDocumentSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
    : IRequestHandler<PatchDocumentSettingsCommand, DocumentSettingsResponse>
{
    public async Task<DocumentSettingsResponse> Handle(
        PatchDocumentSettingsCommand command, CancellationToken ct)
    {
        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions) ?? new DocumentSettings()
            : new DocumentSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        // Apply patch operations directly to settings
        var errors = new List<JsonPatchError>();
        command.PatchDocument.ApplyTo(settings, error => errors.Add(error));

        if (errors.Count > 0)
            throw new BusinessRuleException(errors.Select(e => new
            {
                Operation = e.Operation?.ToString(),
                e.ErrorMessage
            }));

        var newValue = JsonSerializer.Serialize(settings, FieldChange.JsonOptions);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Key = AppSetting.DocumentsModule,
                Value = newValue,
                UpdatedByUserId = currentUser.UserId
            };
            db.AppSettings.Add(entity);

            audit.Created("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.DocumentsModule));
        }
        else
        {
            entity.Value = newValue;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedByUserId = currentUser.UserId;

            var after = AppSettingSnapshot.From(entity);
            var changes = before!.Diff(after);

            if (changes.Count > 0)
                audit.Track(() => changes, "AppSetting",
                    AppSettingSnapshot.KeyToGuid(AppSetting.DocumentsModule));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new DocumentSettingsResponse
        {
            AutoClassifyOnUpload = settings.AutoClassifyOnUpload
        };
    }
}
