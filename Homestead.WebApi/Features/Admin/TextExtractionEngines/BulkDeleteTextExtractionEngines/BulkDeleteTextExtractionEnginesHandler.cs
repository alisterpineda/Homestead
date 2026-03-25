using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Common.Auditing;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.TextExtractionEngines.BulkDeleteTextExtractionEngines;

public class BulkDeleteTextExtractionEnginesHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<BulkDeleteTextExtractionEnginesCommand, BulkDeleteTextExtractionEnginesResponse>
{
    public async Task<BulkDeleteTextExtractionEnginesResponse> Handle(BulkDeleteTextExtractionEnginesCommand request, CancellationToken ct)
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

        foreach (var engine in engines)
            audit.Deleted("TextExtractionEngine", engine.Id);

        db.TextExtractionEngines.RemoveRange(engines);

        await db.SaveChangesAsync(ct);

        return new BulkDeleteTextExtractionEnginesResponse(engines.Count);
    }
}
