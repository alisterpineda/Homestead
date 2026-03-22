using System.Text.Json;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.Settings.GetDocumentSettings;

public class GetDocumentSettingsHandler(AppDbContext db)
    : IRequestHandler<GetDocumentSettingsRequest, DocumentSettingsResponse>
{
    public async Task<DocumentSettingsResponse> Handle(GetDocumentSettingsRequest request, CancellationToken ct)
    {
        var entity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

        var settings = entity is not null
            ? JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions) ?? new DocumentSettings()
            : new DocumentSettings();

        return new DocumentSettingsResponse
        {
            DefaultTextExtractionEngineId = settings.DefaultTextExtractionEngineId
        };
    }
}
