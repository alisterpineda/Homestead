using System.Text.Json;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.TextExtraction;

public class TextExtractionEngineResolver(AppDbContext db) : ITextExtractionEngineResolver
{
    private static readonly TextExtractionContext BuiltInDefault = new(
        TextExtractionEngineType.Tesseract,
        BuiltInTextExtractionEngines.TesseractDefaultConfiguration);

    public async Task<TextExtractionContext> ResolveAsync(Guid? engineId, CancellationToken ct)
    {
        // Resolution chain: explicit engineId -> default from settings -> built-in default
        var resolvedId = engineId;

        if (resolvedId is null)
        {
            var entity = await db.AppSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

            if (entity is not null)
            {
                var settings = JsonSerializer.Deserialize<DocumentSettings>(entity.Value, FieldChange.JsonOptions);
                resolvedId = settings?.DefaultTextExtractionEngineId;
            }
        }

        if (resolvedId is null || resolvedId == BuiltInTextExtractionEngines.TesseractDefaultId)
            return BuiltInDefault;

        var engine = await db.TextExtractionEngines
            .AsNoTracking()
            .Where(e => e.Id == resolvedId)
            .Select(e => new { e.EngineType, e.Configuration })
            .FirstOrDefaultAsync(ct);

        if (engine is null)
            return BuiltInDefault;

        return new TextExtractionContext(engine.EngineType, engine.Configuration);
    }
}
