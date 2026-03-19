using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.TextExtraction;

public class TextExtractionEngineResolver(
    AppDbContext db,
    IOptionsMonitor<DocumentSettings> documentSettings) : ITextExtractionEngineResolver
{
    private static readonly TextExtractionContext BuiltInDefault = new(
        TextExtractionEngineType.Tesseract,
        BuiltInTextExtractionEngines.TesseractDefaultConfiguration);

    public async Task<TextExtractionContext> ResolveAsync(Guid? engineId, CancellationToken ct)
    {
        // Resolution chain: explicit engineId -> default from settings -> built-in default
        var resolvedId = engineId ?? documentSettings.CurrentValue.DefaultTextExtractionEngineId;

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
