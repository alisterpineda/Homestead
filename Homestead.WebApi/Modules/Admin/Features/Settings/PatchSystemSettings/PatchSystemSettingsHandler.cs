using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Settings;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;

public class PatchSystemSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    ISettingsChangeNotifier settingsChangeNotifier,
    IValidator<PatchSystemSettingsCommand> validator)
{
    public async Task<SystemSettingsResponse> Handle(
        PatchSystemSettingsCommand request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var entity = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<SystemSettings>(entity.Value, FieldChange.JsonOptions) ?? new SystemSettings()
            : new SystemSettings();

        AppSettingSnapshot? before = entity is not null ? AppSettingSnapshot.From(entity) : null;

        // null = leave unchanged; a provided map fully replaces the stored set. Store LogLevel names so the
        // jsonb blob and the audit diff stay readable.
        if (request.LogLevelOverrides is not null)
        {
            settings.LogLevelOverrides = request.LogLevelOverrides
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString());
        }

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

            audit.Created("AppSetting", AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule));
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
                    AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule));
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Fan the change out to every instance — including this one — only AFTER the command transaction
        // commits. Listeners re-read the committed row on a separate connection, so a pre-commit NOTIFY could
        // race ahead of the row's visibility (same reason the repo defers the job-processor signal). There's
        // no privileged saving instance: it processes its own NOTIFY through the identical listener path, so
        // all instances converge on the persisted blob. The response below still returns the written levels
        // immediately.
        if (request.LogLevelOverrides is not null)
            unitOfWork.OnAfterCommit(hookCt => settingsChangeNotifier.NotifyAsync(AppSetting.SystemModule, hookCt));

        return new SystemSettingsResponse
        {
            LogLevelOverrides = settings.GetLogLevelOverrides()
        };
    }
}
