using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.UpdateTextExtractionEngine;

public class UpdateTextExtractionEngineHandler(AppDbContext db, IAuditLogger auditLogger)
    : ICommandHandler<UpdateTextExtractionEngineCommand, TextExtractionEngineResponse>
{
    public async Task<TextExtractionEngineResponse> HandleAsync(UpdateTextExtractionEngineCommand command, CancellationToken ct)
    {
        if (command.Id == BuiltInTextExtractionEngines.TesseractDefaultId)
            throw new BadRequestException("Cannot modify a built-in engine.");

        var engine = await db.TextExtractionEngines
            .Include(e => e.CreatedByUser)
            .FirstOrDefaultAsync(e => e.Id == command.Id, ct);
        if (engine is null)
            throw new NotFoundException("Text extraction engine not found.");

        var trimmedName = command.Request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
            throw new BadRequestException("Name is required.");

        if (string.Equals(trimmedName, BuiltInTextExtractionEngines.TesseractDefaultName, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("A text extraction engine with this name already exists.");

        var nameConflict = await db.TextExtractionEngines
            .AnyAsync(e => e.Id != command.Id && e.Name == trimmedName, ct);

        if (nameConflict)
            throw new ConflictException("A text extraction engine with this name already exists.");

        TextExtractionEngineConfigValidator.Validate(engine.EngineType, command.Request.Configuration);

        var before = TextExtractionEngineSnapshot.From(engine);

        engine.Name = trimmedName;
        engine.Configuration = command.Request.Configuration;
        engine.UpdatedAt = DateTimeOffset.UtcNow;

        var after = TextExtractionEngineSnapshot.From(engine);
        var changes = before.Diff(after);

        if (changes.Count > 0)
            auditLogger.LogUpdated("TextExtractionEngine", engine.Id, changes);

        await db.SaveChangesAsync(ct);

        return new TextExtractionEngineResponse
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

public record UpdateTextExtractionEngineCommand
{
    public Guid Id { get; init; }
    public required UpdateTextExtractionEngineRequest Request { get; init; }
}
