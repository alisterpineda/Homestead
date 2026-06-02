using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Logging;
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
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    ILogLevelService logLevelService)
    : IRequestHandler<PatchSystemSettingsCommand, SystemSettingsResponse>
{
    public async Task<SystemSettingsResponse> Handle(
        PatchSystemSettingsCommand request, CancellationToken ct)
    {
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

        // Apply the new levels to the live MEL filters only AFTER the command transaction commits — running
        // it inline here (post-flush, pre-commit) would permanently diverge the in-process loggers from the
        // persisted blob if the commit later fails and rolls back. The override is in-process only; other
        // replicas pick it up on their next restart via the startup load.
        if (request.LogLevelOverrides is { } overrides)
            unitOfWork.OnAfterCommit(_ =>
            {
                logLevelService.Apply(overrides);
                return Task.CompletedTask;
            });

        return new SystemSettingsResponse
        {
            LogLevelOverrides = settings.GetLogLevelOverrides()
        };
    }
}
