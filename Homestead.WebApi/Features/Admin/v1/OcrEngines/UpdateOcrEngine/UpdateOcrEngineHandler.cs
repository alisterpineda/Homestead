using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.OcrEngines.UpdateOcrEngine;

public class UpdateOcrEngineHandler(AppDbContext db, ICurrentUserAccessor currentUser, IAuditLogger auditLogger)
    : ICommandHandler<UpdateOcrEngineCommand, OcrEngineResponse>
{
    public async Task<OcrEngineResponse> HandleAsync(UpdateOcrEngineCommand command, CancellationToken ct)
    {
        if (command.Id == BuiltInOcrEngines.TesseractDefaultId)
            throw new BadRequestException("Cannot modify a built-in engine.");

        var engine = await db.OcrEngines
            .Include(e => e.CreatedByUser)
            .FirstOrDefaultAsync(e => e.Id == command.Id, ct);
        if (engine is null)
            throw new NotFoundException("OCR engine not found.");

        var trimmedName = command.Request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new BadRequestException("Name is required.");

        if (string.Equals(trimmedName, BuiltInOcrEngines.TesseractDefaultName, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("An OCR engine with this name already exists.");

        var nameConflict = await db.OcrEngines
            .AnyAsync(e => e.Id != command.Id && e.Name == trimmedName, ct);

        if (nameConflict)
            throw new ConflictException("An OCR engine with this name already exists.");

        OcrEngineConfigValidator.Validate(engine.EngineType, command.Request.Configuration);

        var before = OcrEngineSnapshot.From(engine);

        engine.Name = trimmedName;
        engine.Configuration = command.Request.Configuration;
        engine.UpdatedAt = DateTimeOffset.UtcNow;

        var after = OcrEngineSnapshot.From(engine);
        var changes = before.Diff(after);

        if (changes.Count > 0)
            auditLogger.LogUpdated("OcrEngine", engine.Id, currentUser.UserId, changes);

        await db.SaveChangesAsync(ct);

        return new OcrEngineResponse
        {
            Id = engine.Id,
            Name = engine.Name,
            EngineType = engine.EngineType,
            Configuration = engine.Configuration,
            IsBuiltIn = false,
            CreatedAt = engine.CreatedAt,
            UpdatedAt = engine.UpdatedAt,
            CreatedByUserId = engine.CreatedByUserId,
            CreatedByUserName = engine.CreatedByUser?.DisplayName,
        };
    }
}

public record UpdateOcrEngineCommand
{
    public Guid Id { get; init; }
    public required UpdateOcrEngineRequest Request { get; init; }
}
