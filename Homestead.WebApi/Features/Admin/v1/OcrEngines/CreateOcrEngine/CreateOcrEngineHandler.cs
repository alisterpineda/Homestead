using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.CreateOcrEngine;

public class CreateOcrEngineHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<CreateOcrEngineRequest, OcrEngineResponse>
{
    public async Task<OcrEngineResponse> HandleAsync(CreateOcrEngineRequest request, CancellationToken ct)
    {
        var trimmedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new BadRequestException("Name is required.");

        if (string.Equals(trimmedName, BuiltInOcrEngines.TesseractDefaultName, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("An OCR engine with this name already exists.");

        var nameExists = await db.OcrEngines
            .AnyAsync(e => e.Name == trimmedName, ct);

        if (nameExists)
            throw new ConflictException("An OCR engine with this name already exists.");

        OcrEngineConfigValidator.Validate(request.EngineType, request.Configuration);

        var userId = currentUser.UserId;

        var engine = new OcrEngine
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            EngineType = request.EngineType,
            Configuration = request.Configuration,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = userId
        };

        db.OcrEngines.Add(engine);
        auditLogger.LogCreated("OcrEngine", engine.Id, userId);
        await db.SaveChangesAsync(ct);

        var creatorName = userId.HasValue
            ? await db.Users.Where(u => u.Id == userId.Value).Select(u => u.DisplayName).FirstOrDefaultAsync(ct)
            : null;

        return new OcrEngineResponse
        {
            Id = engine.Id,
            Name = engine.Name,
            EngineType = engine.EngineType,
            Configuration = engine.Configuration,
            IsBuiltIn = false,
            CreatedAt = engine.CreatedAt,
            CreatedByUserId = engine.CreatedByUserId,
            CreatedByUserName = creatorName,
        };
    }
}
