using System.Text.Json;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Documents.Services;

public class TextExtractionEngineResolver(AppDbContext db, IEnumerable<ITextExtractionEngine> engines)
    : ITextExtractionEngineResolver
{
    public async Task<string> ResolveKeyAsync(string? requestedKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
            return await ResolveDefaultKeyAsync(ct);

        var engine = engines.SingleOrDefault(e =>
            string.Equals(e.Key, requestedKey, StringComparison.OrdinalIgnoreCase));

        if (engine is null)
            throw new BusinessRuleException($"Text extraction engine '{requestedKey}' is not configured.");

        if (!engine.IsConfigured)
            throw new BusinessRuleException(
                $"Text extraction engine '{requestedKey}' is unavailable: {engine.ConfigurationError}");

        // Canonical casing, so the payload and provenance metadata record the configured key rather than
        // however the caller happened to spell it.
        return engine.Key;
    }

    public async Task<string> ResolveDefaultKeyAsync(CancellationToken ct)
    {
        var settingsEntity = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);

        var defaultKey = settingsEntity is null
            ? null
            : JsonSerializer.Deserialize<DocumentSettings>(settingsEntity.Value, FieldChange.JsonOptions)
                ?.DefaultEngineKey;

        return string.IsNullOrWhiteSpace(defaultKey)
            ? OcrEngineOptions.BuiltInTesseractKey
            : defaultKey;
    }
}
