using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public class BulkDeleteTextExtractionEnginesHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<BulkDeleteTextExtractionEnginesRequest, BulkDeleteTextExtractionEnginesResponse>
{
    public async Task<BulkDeleteTextExtractionEnginesResponse> HandleAsync(BulkDeleteTextExtractionEnginesRequest request, CancellationToken ct)
    {
        var idsToDelete = request.Ids
            .Where(id => id != BuiltInTextExtractionEngines.TesseractDefaultId)
            .ToList();

        if (idsToDelete.Count == 0)
            return new BulkDeleteTextExtractionEnginesResponse(0);

        var engines = await db.TextExtractionEngines
            .Where(e => idsToDelete.Contains(e.Id))
            .ToListAsync(ct);

        if (engines.Count == 0)
            return new BulkDeleteTextExtractionEnginesResponse(0);

        var userId = currentUser.UserId;
        foreach (var engine in engines)
            auditLogger.LogDeleted("TextExtractionEngine", engine.Id, userId);

        db.TextExtractionEngines.RemoveRange(engines);
        await db.SaveChangesAsync(ct);

        return new BulkDeleteTextExtractionEnginesResponse(engines.Count);
    }
}
