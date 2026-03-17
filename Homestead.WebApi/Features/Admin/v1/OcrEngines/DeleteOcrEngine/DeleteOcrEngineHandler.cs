using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.DeleteOcrEngine;

public class DeleteOcrEngineHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<DeleteOcrEngineRequest, bool>
{
    public async Task<bool> HandleAsync(DeleteOcrEngineRequest request, CancellationToken ct)
    {
        if (request.Id == BuiltInOcrEngines.TesseractDefaultId)
            throw new BadRequestException("Cannot delete a built-in engine.");

        var engine = await db.OcrEngines.FirstOrDefaultAsync(e => e.Id == request.Id, ct);
        if (engine is null)
            throw new NotFoundException("OCR engine not found.");

        db.OcrEngines.Remove(engine);
        auditLogger.LogDeleted("OcrEngine", engine.Id, currentUser.UserId);
        await db.SaveChangesAsync(ct);

        return true;
    }
}

public record DeleteOcrEngineRequest(Guid Id);
