using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;

public class GetSystemSettingsHandler(AppDbContext db)
    : IRequestHandler<GetSystemSettingsQuery, SystemSettingsResponse>
{
    public async Task<SystemSettingsResponse> Handle(GetSystemSettingsQuery request, CancellationToken ct)
    {
        var entity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<SystemSettings>(entity.Value, FieldChange.JsonOptions) ?? new SystemSettings()
            : new SystemSettings();

        return new SystemSettingsResponse
        {
            LogLevelOverrides = settings.GetLogLevelOverrides()
        };
    }
}
