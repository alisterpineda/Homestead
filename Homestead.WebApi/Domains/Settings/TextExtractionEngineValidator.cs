using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Domains.Settings;

public static class TextExtractionEngineValidator
{
    public static async Task ValidateEngineExistsAsync(AppDbContext db, Guid engineId, CancellationToken ct)
    {
        if (engineId == BuiltInTextExtractionEngines.TesseractDefaultId)
            return;

        var exists = await db.TextExtractionEngines.AnyAsync(e => e.Id == engineId, ct);
        if (!exists)
            throw new BadRequestException("The specified text extraction engine does not exist.");
    }
}
