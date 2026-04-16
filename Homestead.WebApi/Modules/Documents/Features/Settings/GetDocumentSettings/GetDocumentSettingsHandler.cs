using System.Text.Json;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Features.Settings.GetDocumentSettings;

public class GetDocumentSettingsHandler(AppDbContext db)
    : IRequestHandler<GetDocumentSettingsQuery, DocumentSettingsResponse>
{
    public async Task<DocumentSettingsResponse> Handle(GetDocumentSettingsQuery request, CancellationToken ct)
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
