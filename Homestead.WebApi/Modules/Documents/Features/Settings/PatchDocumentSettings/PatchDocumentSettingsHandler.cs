using Homestead.WebApi.Common.Auditing;
using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;

public class PatchDocumentSettingsHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork,
    IEnumerable<ITextExtractionEngine> textExtractionEngines)
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

        // Checked here, after the patch is applied, rather than in a FluentValidation validator: a JSON
        // Patch command carries operations, not values, so ValidationBehavior never sees the engine key —
        // the only place it exists as a value is on the patched object. (Same reason
        // PatchSystemSettingsCommandValidator can guard its LogLevel map but this cannot.)
        ValidateDefaultEngineKey(settings.DefaultEngineKey);

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
            AutoClassifyOnUpload = settings.AutoClassifyOnUpload,
            DefaultEngineKey = settings.DefaultEngineKey
        };
    }

    // Existence, not availability: an engine that exists but is currently misconfigured is still a
    // legitimate thing to prefer (an admin may be mid-way through setting its credentials), and the
    // picker already reports why it cannot run. A key that names nothing at all is just a typo.
    private void ValidateDefaultEngineKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var known = textExtractionEngines
            .Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        if (!known)
        {
            var available = string.Join(", ", textExtractionEngines.Select(e => $"'{e.Key}'"));
            throw new BusinessRuleException(
                $"Text extraction engine '{key}' is not configured. Available engines: {available}.");
        }
    }
}
