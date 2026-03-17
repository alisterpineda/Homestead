using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.BulkDeleteOcrEngines;

public class BulkDeleteOcrEnginesHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<BulkDeleteOcrEnginesRequest, BulkDeleteOcrEnginesResponse>
{
    public async Task<BulkDeleteOcrEnginesResponse> HandleAsync(BulkDeleteOcrEnginesRequest request, CancellationToken ct)
    {
        var idsToDelete = request.Ids
            .Where(id => id != BuiltInOcrEngines.TesseractDefaultId)
            .ToList();

        if (idsToDelete.Count == 0)
            return new BulkDeleteOcrEnginesResponse(0);

        var engines = await db.OcrEngines
            .Where(e => idsToDelete.Contains(e.Id))
            .ToListAsync(ct);

        if (engines.Count == 0)
            return new BulkDeleteOcrEnginesResponse(0);

        var userId = currentUser.UserId;
        foreach (var engine in engines)
            auditLogger.LogDeleted("OcrEngine", engine.Id, userId);

        db.OcrEngines.RemoveRange(engines);
        await db.SaveChangesAsync(ct);

        return new BulkDeleteOcrEnginesResponse(engines.Count);
    }
}
